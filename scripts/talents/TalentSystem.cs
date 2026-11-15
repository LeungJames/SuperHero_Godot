using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 天赋解锁、加点、重置与点数校验（设计文档 §5.3、§5.4）。
/// 点数不落盘为独立字段，而是由"评级历史最佳点数 − 已点节点花费 − 重置罚分"推导，
/// 保证重玩补差额与洗点退还在同一套账上。
/// </summary>
public static class TalentSystem
{
	private static readonly string[] MobilityIds = { "M0", "M1", "M2", "M3", "M4", "M5", "M6", "M8" };
	private static readonly string[] WeaponIds = { "W0", "W1", "W2", "W3", "W4", "W5", "W6", "W7", "W8" };
	private static readonly string[] AssassinIds = { "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8" };

	private static readonly Dictionary<string, TalentNodeData> Nodes = new();
	private static TalentResetConfig _resetConfig;

	public static TalentResetConfig ResetConfig
	{
		get
		{
			if (_resetConfig == null)
			{
				const string path = "res://data/talents/reset_config.tres";
				_resetConfig = ResourceLoader.Exists(path) ? GD.Load<TalentResetConfig>(path) : new TalentResetConfig();
			}

			return _resetConfig;
		}
	}

	public static string LineKey(TalentLine line) => line switch
	{
		TalentLine.Mobility => "mobility",
		TalentLine.Weapon => "weapon",
		_ => "assassin",
	};

	public static TalentLine LineOf(string key) => key switch
	{
		"weapon" => TalentLine.Weapon,
		"assassin" => TalentLine.Assassin,
		_ => TalentLine.Mobility,
	};

	public static IReadOnlyList<TalentNodeData> NodesOf(TalentLine line)
	{
		string[] ids = line switch
		{
			TalentLine.Weapon => WeaponIds,
			TalentLine.Assassin => AssassinIds,
			_ => MobilityIds,
		};

		var result = new List<TalentNodeData>();
		foreach (string id in ids)
		{
			TalentNodeData node = Get(id);
			if (node != null)
				result.Add(node);
		}

		return result;
	}

	public static TalentNodeData Get(string id)
	{
		if (string.IsNullOrEmpty(id))
			return null;
		if (Nodes.TryGetValue(id, out TalentNodeData cached))
			return cached;

		TalentLine line = id[0] switch
		{
			'W' => TalentLine.Weapon,
			'A' => TalentLine.Assassin,
			_ => TalentLine.Mobility,
		};
		string path = $"res://data/talents/{LineKey(line)}/{id}.tres";
		TalentNodeData node = ResourceLoader.Exists(path) ? GD.Load<TalentNodeData>(path) : null;
		if (node != null)
			Nodes[id] = node;
		return node;
	}

	// ---------------------------------------------------------------- 点数

	/// <summary>该线从评级获得的总点数（历史最佳，防重复发放）。</summary>
	public static int EarnedPoints(TalentLine line)
	{
		if (!SaveSystem.HasInstance)
			return 0;

		string dimension = RatingSystem.DimensionKey(line);
		int total = 0;
		foreach (SaveSystem.ChapterInfo chapter in SaveSystem.Chapters)
			total += SaveSystem.Instance.GetRating(chapter.Id, dimension).Points;
		return total;
	}

	public static int SpentPoints(TalentLine line)
	{
		int spent = 0;
		foreach (TalentNodeData node in NodesOf(line))
		{
			if (node != null && node.Cost > 0 && IsPurchased(node.Id))
				spent += node.Cost;
		}

		return spent;
	}

	public static int PenaltyPoints(TalentLine line) =>
		SaveSystem.HasInstance ? SaveSystem.Instance.GetTalentPenalty(LineKey(line)) : 0;

	public static int AvailablePoints(TalentLine line) =>
		Mathf.Max(0, EarnedPoints(line) - SpentPoints(line) - PenaltyPoints(line));

	// ---------------------------------------------------------------- 状态

	/// <summary>免费起点（cost = 0）视为始终拥有，不写存档。</summary>
	public static bool IsPurchased(string id)
	{
		TalentNodeData node = Get(id);
		if (node == null)
			return false;
		if (node.Cost <= 0)
			return true;
		return SaveSystem.HasInstance && SaveSystem.Instance.HasTalent(LineKey(node.Line), id);
	}

	public static bool IsPrerequisiteMet(TalentNodeData node)
	{
		if (node == null || string.IsNullOrEmpty(node.PrerequisiteId))
			return true;
		return IsPurchased(node.PrerequisiteId);
	}

	/// <summary>A7 需要先通关第二章；其余节点无章节门槛。</summary>
	public static bool IsChapterAllowed(TalentNodeData node)
	{
		if (node == null || string.IsNullOrEmpty(node.RequiredClearedChapter))
			return true;
		return SaveSystem.HasInstance && SaveSystem.Instance.IsChapterCleared(node.RequiredClearedChapter);
	}

	public static bool CanPurchase(TalentNodeData node)
	{
		if (node == null || IsPurchased(node.Id) || !IsPrerequisiteMet(node) || !IsChapterAllowed(node))
			return false;
		return AvailablePoints(node.Line) >= node.Cost;
	}

	public static bool Purchase(string id)
	{
		TalentNodeData node = Get(id);
		if (!CanPurchase(node))
			return false;
		return SaveSystem.HasInstance && SaveSystem.Instance.AddTalent(LineKey(node.Line), id);
	}

	// ---------------------------------------------------------------- 重置

	public static bool CanReset(TalentLine line, bool inChapter)
	{
		if (inChapter && !ResetConfig.AllowMidChapter)
			return false;
		return SpentPoints(line) > 0;
	}

	/// <summary>单线重置：全额退还已点节点，按配置扣代价并写档（§5.4）。</summary>
	public static bool ResetLine(TalentLine line, bool inChapter)
	{
		if (!CanReset(line, inChapter) || !SaveSystem.HasInstance)
			return false;

		string key = LineKey(line);
		int resets = SaveSystem.Instance.GetTalentResetCount(key);
		int cost = resets < ResetConfig.FreeResets ? 0 : Mathf.Max(0, ResetConfig.PointCost);

		if (cost > 0)
		{
			int earned = EarnedPoints(line);
			int penalty = Mathf.Min(earned, SaveSystem.Instance.GetTalentPenalty(key) + cost);
			SaveSystem.Instance.SetTalentPenalty(key, penalty);
		}

		SaveSystem.Instance.ClearTalents(key);
		SaveSystem.Instance.SetTalentResetCount(key, resets + 1);
		return true;
	}
}
