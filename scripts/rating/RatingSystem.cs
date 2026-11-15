using Godot;
using System;
using System.Collections.Generic;

/// <summary>单局原始指标（§6.2）。不含血量：零回血约束下血量不参与评分。</summary>
public struct RunStats
{
	public float Time;
	public int MobilityActions;
	public int Falls;
	public int Shots;
	public int Hits;
	public int Headshots;
	public int EmptyMags;
	public int Kills;
	public int StealthKills;
	public int AlertEscalations;
	public int Executions;
}

/// <summary>单维度评级结果。</summary>
public sealed class DimensionRating
{
	public string Dimension { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public string Grade { get; set; } = "C";
	public int Points { get; set; }
	/// <summary>本次实际补发的差额点数；重复或更低评级为 0。</summary>
	public int Awarded { get; set; }
	public string Detail { get; set; } = string.Empty;
}

/// <summary>一局三维结算结果。</summary>
public sealed class RatingOutcome
{
	public string ChapterId { get; set; } = string.Empty;
	public List<DimensionRating> Results { get; } = new();
	public int AwardedTotal { get; set; }
	public int BestTotal { get; set; }

	public DimensionRating Mobility => Results.Count > 0 ? Results[0] : null;
	public DimensionRating Weapon => Results.Count > 1 ? Results[1] : null;
	public DimensionRating Assassin => Results.Count > 2 ? Results[2] : null;
}

/// <summary>
/// 三维评级与发点（设计文档 §6）。规则全部来自 <c>data/rating/&lt;chapter&gt;_rating.tres</c>；
/// 只保留历史最高评级，重玩提升时补差额（§6.5）。
/// </summary>
public static class RatingSystem
{
	public const string MobilityKey = "mobility";
	public const string WeaponKey = "weapon";
	public const string AssassinKey = "assassin";

	public static ChapterRatingConfig LoadConfig(string chapterId)
	{
		string path = $"res://data/rating/{chapterId}_rating.tres";
		return ResourceLoader.Exists(path) ? GD.Load<ChapterRatingConfig>(path) : null;
	}

	/// <summary>只计算，不写存档。</summary>
	public static RatingOutcome Evaluate(string chapterId, RunStats stats)
	{
		ChapterRatingConfig config = LoadConfig(chapterId) ?? new ChapterRatingConfig();
		float minutes = Mathf.Max(stats.Time, 0f) / 60f;
		float density = minutes > 0.001f ? stats.MobilityActions / minutes : 0f;
		float accuracy = stats.Shots > 0 ? (float)stats.Hits / stats.Shots : 0f;
		float headshot = stats.Hits > 0 ? (float)stats.Headshots / stats.Hits : 0f;
		float stealth = stats.Kills > 0 ? (float)stats.StealthKills / stats.Kills : 0f;

		var outcome = new RatingOutcome { ChapterId = chapterId };

		// 战术机动：时间 / 密度 / 摔伤三项全部满足才给该级（§6.2 判定规则）
		string mobilityGrade = Grade(
			stats.Time > 0f && stats.Time <= config.BaselineTime * config.TimeRatioS &&
				density >= config.MobilityS && stats.Falls <= config.FallsS,
			stats.Time > 0f && stats.Time <= config.BaselineTime * config.TimeRatioA &&
				density >= config.MobilityA && stats.Falls <= config.FallsA,
			stats.Time > 0f && stats.Time <= config.BaselineTime * config.TimeRatioB &&
				density >= config.MobilityB && stats.Falls <= config.FallsB);
		outcome.Results.Add(new DimensionRating
		{
			Dimension = MobilityKey,
			Label = "战术机动",
			Grade = mobilityGrade,
			Points = Scale(Points(mobilityGrade, config), config.ChapterMultiplier),
			Detail = $"用时 {FormatTime(stats.Time)}（基准 {FormatTime(config.BaselineTime)}） · " +
				$"机动 {density:F1}/分 · 摔伤 {stats.Falls}",
		});

		// 武器大师：命中率 / 爆头率 / 空仓次数；0 发射击按 0% 计，只能拿 C，明细显示「—」
		bool hasShots = stats.Shots > 0;
		string weaponGrade = hasShots
			? Grade(
				accuracy >= config.AccuracyS && headshot >= config.HeadshotS && stats.EmptyMags <= config.EmptyMagsS,
				accuracy >= config.AccuracyA && headshot >= config.HeadshotA && stats.EmptyMags <= config.EmptyMagsA,
				accuracy >= config.AccuracyB && headshot >= config.HeadshotB && stats.EmptyMags <= config.EmptyMagsB)
			: "C";
		outcome.Results.Add(new DimensionRating
		{
			Dimension = WeaponKey,
			Label = "武器大师",
			Grade = weaponGrade,
			Points = Scale(Points(weaponGrade, config), config.ChapterMultiplier),
			Detail = $"命中 {(hasShots ? Percent(accuracy) : "—")}（{stats.Hits}/{stats.Shots}） · " +
				$"爆头 {(stats.Hits > 0 ? Percent(headshot) : "—")} · 空仓 {stats.EmptyMags}",
		});

		// 忍杀：潜行击杀占比 / 警报升级次数 / 暗杀 + 处决次数；0 击杀按 0% 计，只能拿 C，明细显示「—」
		bool hasKills = stats.Kills > 0;
		string assassinGrade = hasKills
			? Grade(
				stealth >= config.StealthRatioS && stats.AlertEscalations <= config.AlertsS &&
					stats.Executions >= config.ExecutionsS,
				stealth >= config.StealthRatioA && stats.AlertEscalations <= config.AlertsA &&
					stats.Executions >= config.ExecutionsA,
				stealth >= config.StealthRatioB && stats.AlertEscalations <= config.AlertsB &&
					stats.Executions >= config.ExecutionsB)
			: "C";
		outcome.Results.Add(new DimensionRating
		{
			Dimension = AssassinKey,
			Label = "忍杀",
			Grade = assassinGrade,
			Points = Scale(Points(assassinGrade, config), config.ChapterMultiplier),
			Detail = $"潜行击杀 {(hasKills ? Percent(stealth) : "—")}（{stats.StealthKills}/{stats.Kills}） · " +
				$"警报 {stats.AlertEscalations} · 暗杀/处决 {stats.Executions}",
		});

		return outcome;
	}

	/// <summary>计算并写档：只补发提升差额。</summary>
	public static RatingOutcome Settle(string chapterId, RunStats stats)
	{
		RatingOutcome outcome = Evaluate(chapterId, stats);
		if (!SaveSystem.HasInstance)
			return outcome;

		foreach (DimensionRating result in outcome.Results)
		{
			result.Awarded = SaveSystem.Instance.ImproveRating(
				chapterId, result.Dimension, SaveSystem.GradeValue(result.Grade), result.Grade, result.Points);
			outcome.AwardedTotal += result.Awarded;
		}

		outcome.BestTotal = BestTotal(chapterId);
		return outcome;
	}

	/// <summary>该章三维历史最佳点数之和。</summary>
	public static int BestTotal(string chapterId)
	{
		if (!SaveSystem.HasInstance)
			return 0;

		int total = 0;
		foreach (string dimension in new[] { MobilityKey, WeaponKey, AssassinKey })
			total += SaveSystem.Instance.GetRating(chapterId, dimension).Points;
		return total;
	}

	public static string DimensionKey(TalentLine line) => line switch
	{
		TalentLine.Mobility => MobilityKey,
		TalentLine.Weapon => WeaponKey,
		_ => AssassinKey,
	};

	private static string Grade(bool s, bool a, bool b) => s ? "S" : a ? "A" : b ? "B" : "C";

	private static int Points(string grade, ChapterRatingConfig config) => grade switch
	{
		"S" => config.PointsS,
		"A" => config.PointsA,
		"B" => config.PointsB,
		_ => config.PointsC,
	};

	/// <summary>round_half_up(基础点 × 关卡系数)（§6.4）。</summary>
	private static int Scale(int basePoints, float multiplier)
	{
		float value = basePoints * Mathf.Max(multiplier, 0f);
		return (int)Math.Round(value, MidpointRounding.AwayFromZero);
	}

	private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

	private static string FormatTime(float seconds)
	{
		int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
		return $"{total / 60:00}:{total % 60:00}";
	}
}
