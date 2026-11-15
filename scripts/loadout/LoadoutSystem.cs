using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 武器库规则（设计文档 §7）。只做章节池、解锁校验与单局组合传递，不含 UI 与开火逻辑：
/// 章节选择 → 武器库菜单 → 写待选组合 → 关卡内 WeaponController 读取。
/// 港口为固定例外：任何入口进入港口都恒为 P-9 + 刀（§12.2）。
/// </summary>
public static class LoadoutSystem
{
	public const string KnifeId = "knife";
	public const string KnifeName = "刀";
	/// <summary>起始手枪：UnlockChapter = 0，任何章节都视为已解锁，始终可作为兜底武器。</summary>
	public const string BaselineWeaponId = "p9";

	private const string ArsenalPath = "res://data/weapons/Arsenal.tres";

	/// <summary>武器库中的一行：枪或刀；未解锁时置灰并标 [锁]。</summary>
	public readonly struct Option
	{
		public Option(string id, string name, bool unlocked)
		{
			Id = id;
			Name = name;
			Unlocked = unlocked;
		}

		public string Id { get; }
		public string Name { get; }
		public bool Unlocked { get; }
	}

	private static ArsenalData _arsenal;

	/// <summary>章节号与存档章节 ID 的对应；港口同时接受 0 / 1（旧数据）。</summary>
	public static int ChapterNumber(string chapterId) => chapterId switch
	{
		"factory" => 2,
		"anchor" => 3,
		_ => 0,
	};

	public static string ChapterIdFor(int chapterNumber) => chapterNumber switch
	{
		>= 3 => "anchor",
		2 => "factory",
		_ => "port",
	};

	private static ArsenalData Arsenal
	{
		get
		{
			if (_arsenal == null && ResourceLoader.Exists(ArsenalPath))
				_arsenal = GD.Load<ArsenalData>(ArsenalPath);
			return _arsenal;
		}
	}

	public static WeaponData FindWeapon(string id)
	{
		if (string.IsNullOrEmpty(id) || Arsenal == null)
			return null;

		foreach (WeaponData weapon in Arsenal.Weapons)
		{
			if (weapon != null && weapon.Id == id)
				return weapon;
		}

		return null;
	}

	public static string DisplayName(string id) =>
		id == KnifeId ? KnifeName : FindWeapon(id)?.DisplayName ?? id;

	/// <summary>本章可选：UnlockChapter 不大于本章号的枪 + 刀；未解锁的仍列出，由 UI 置灰。</summary>
	public static List<Option> GetOptions(string chapterId)
	{
		var options = new List<Option>();
		int number = ChapterNumber(chapterId);

		if (Arsenal != null)
		{
			foreach (WeaponData weapon in Arsenal.Weapons)
			{
				if (weapon == null || string.IsNullOrEmpty(weapon.Id) || weapon.UnlockChapter > number)
					continue;
				options.Add(new Option(weapon.Id, weapon.DisplayName, IsUnlocked(weapon.Id)));
			}
		}

		options.Add(new Option(KnifeId, KnifeName, true));
		return options;
	}

	public static bool IsUnlocked(string id) =>
		id == KnifeId || id == BaselineWeaponId ||
		(SaveSystem.HasInstance && SaveSystem.Instance.IsWeaponUnlocked(id));

	/// <summary>两格都合法：恰好 2 件、不重复、都在本章池内且已解锁。</summary>
	public static bool IsValid(string chapterId, IReadOnlyList<string> ids)
	{
		if (ids == null || ids.Count != 2 || ids[0] == ids[1])
			return false;

		List<Option> options = GetOptions(chapterId);
		foreach (string id in ids)
		{
			bool found = false;
			foreach (Option option in options)
			{
				if (option.Id != id)
					continue;
				if (!option.Unlocked)
					return false;
				found = true;
				break;
			}

			if (!found)
				return false;
		}

		return true;
	}

	/// <summary>章节默认组合：港口 P-9 + 刀；工厂 P-9 + ST-9；铁锚 AR-5 + M-870。不足时按池顺序补齐。</summary>
	public static string[] DefaultSelection(string chapterId)
	{
		string[] preferred = ChapterNumber(chapterId) switch
		{
			>= 3 => new[] { "ar", "shotgun" },
			2 => new[] { BaselineWeaponId, "smg" },
			_ => new[] { BaselineWeaponId, KnifeId },
		};

		List<Option> options = GetOptions(chapterId);
		var result = new List<string>();

		foreach (string id in preferred)
		{
			if (IsSelectable(options, id) && !result.Contains(id))
				result.Add(id);
		}

		foreach (Option option in options)
		{
			if (result.Count >= 2)
				break;
			if (option.Unlocked && !result.Contains(option.Id))
				result.Add(option.Id);
		}

		return OrderForSlots(result).ToArray();
	}

	/// <summary>刀固定占第二格：枪在前、刀最后。</summary>
	public static List<string> OrderForSlots(IEnumerable<string> ids)
	{
		var ordered = new List<string>();
		bool knife = false;

		foreach (string id in ids)
		{
			if (id == KnifeId)
				knife = true;
			else
				ordered.Add(id);
		}

		if (knife)
			ordered.Add(KnifeId);
		return ordered;
	}

	private static bool IsSelectable(List<Option> options, string id)
	{
		foreach (Option option in options)
		{
			if (option.Id == id)
				return option.Unlocked;
		}

		return false;
	}

	// ------------------------------------------------------------ 单局待选

	private static string _pendingChapter = string.Empty;
	private static string[] _pendingIds = Array.Empty<string>();

	/// <summary>章节开始前写入；只对本章生效，重试沿用，换章自动失效。</summary>
	public static void SetPending(string chapterId, IEnumerable<string> ids)
	{
		var list = new List<string>(ids);
		if (!IsValid(chapterId, list))
			return;

		_pendingChapter = chapterId;
		_pendingIds = OrderForSlots(list).ToArray();
	}

	public static bool HasPendingFor(string chapterId) =>
		!string.IsNullOrEmpty(chapterId) && chapterId == _pendingChapter && IsValid(chapterId, _pendingIds);

	public static string[] GetPendingFor(string chapterId) =>
		HasPendingFor(chapterId) ? (string[])_pendingIds.Clone() : Array.Empty<string>();

	public static void ClearPending()
	{
		_pendingChapter = string.Empty;
		_pendingIds = Array.Empty<string>();
	}

	/// <summary>
	/// 本章实际装配的 ID：港口恒为 P-9 + 刀（跨章武器不生效）；
	/// 其余章节有合法待选时用待选，否则返回空数组表示按章节默认。
	/// </summary>
	public static string[] ResolveForChapter(string chapterId)
	{
		if (ChapterNumber(chapterId) == 0)
			return new[] { BaselineWeaponId, KnifeId };

		return HasPendingFor(chapterId) ? GetPendingFor(chapterId) : Array.Empty<string>();
	}

	/// <summary>单局埋点用的组合字符串。</summary>
	public static string DescribeFor(string chapterId)
	{
		if (HasPendingFor(chapterId))
			return string.Join("+", OrderForSlots(_pendingIds));

		return ChapterNumber(chapterId) == 0 ? $"{BaselineWeaponId}+{KnifeId}" : "default";
	}

	// ------------------------------------------------------------ 解锁

	/// <summary>通关某章后按章节号累积解锁；刀始终写入。</summary>
	public static void GrantUnlocksForChapter(string chapterId)
	{
		if (!SaveSystem.HasInstance || string.IsNullOrEmpty(chapterId))
			return;

		int number = ChapterNumber(chapterId);
		SaveSystem.Instance.UnlockWeapon(KnifeId);

		if (Arsenal == null)
			return;

		foreach (WeaponData weapon in Arsenal.Weapons)
		{
			if (weapon != null && !string.IsNullOrEmpty(weapon.Id) && weapon.UnlockChapter <= number)
				SaveSystem.Instance.UnlockWeapon(weapon.Id);
		}
	}

	/// <summary>旧存档迁移：按已通关章节补齐 unlocks，幂等。</summary>
	public static void EnsureUnlocksFromClearedChapters()
	{
		if (!SaveSystem.HasInstance)
			return;

		foreach (SaveSystem.ChapterInfo chapter in SaveSystem.Chapters)
		{
			if (SaveSystem.Instance.IsChapterCleared(chapter.Id))
				GrantUnlocksForChapter(chapter.Id);
		}
	}
}
