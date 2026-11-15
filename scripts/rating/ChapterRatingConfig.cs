using Godot;

/// <summary>
/// 单章评级配置（设计文档 §6.2、§6.3、§6.7）。
/// 阈值全部外置到 <c>res://data/rating/&lt;chapter_id&gt;_rating.tres</c>，
/// 代码只按"三项全部满足"的规则套用。
/// </summary>
[GlobalClass]
public partial class ChapterRatingConfig : Resource
{
	[ExportGroup("时间")]
	/// <summary>基准时间（秒）；时间维度按它的比例判定。</summary>
	[Export] public float BaselineTime { get; set; } = 720f;
	[Export] public float ChapterMultiplier { get; set; } = 1f;
	[Export] public float TimeRatioS { get; set; } = 0.75f;
	[Export] public float TimeRatioA { get; set; } = 0.90f;
	[Export] public float TimeRatioB { get; set; } = 1.10f;

	[ExportGroup("战术机动")]
	/// <summary>机动动作密度（次/分钟）。</summary>
	[Export] public float MobilityS { get; set; } = 8f;
	[Export] public float MobilityA { get; set; } = 6f;
	[Export] public float MobilityB { get; set; } = 4f;
	/// <summary>摔伤次数上限。</summary>
	[Export] public int FallsS { get; set; }
	[Export] public int FallsA { get; set; } = 1;
	[Export] public int FallsB { get; set; } = 2;

	[ExportGroup("武器大师")]
	[Export] public float AccuracyS { get; set; } = 0.55f;
	[Export] public float AccuracyA { get; set; } = 0.45f;
	[Export] public float AccuracyB { get; set; } = 0.35f;
	[Export] public float HeadshotS { get; set; } = 0.25f;
	[Export] public float HeadshotA { get; set; } = 0.15f;
	[Export] public float HeadshotB { get; set; } = 0.08f;
	[Export] public int EmptyMagsS { get; set; }
	[Export] public int EmptyMagsA { get; set; } = 1;
	[Export] public int EmptyMagsB { get; set; } = 3;

	[ExportGroup("忍杀")]
	[Export] public float StealthRatioS { get; set; } = 0.60f;
	[Export] public float StealthRatioA { get; set; } = 0.40f;
	[Export] public float StealthRatioB { get; set; } = 0.20f;
	/// <summary>警报升级次数上限。</summary>
	[Export] public int AlertsS { get; set; }
	[Export] public int AlertsA { get; set; } = 1;
	[Export] public int AlertsB { get; set; } = 2;
	/// <summary>暗杀 + 处决次数下限。</summary>
	[Export] public int ExecutionsS { get; set; } = 5;
	[Export] public int ExecutionsA { get; set; } = 3;
	[Export] public int ExecutionsB { get; set; } = 1;

	[ExportGroup("点数")]
	/// <summary>基础点 C / B / A / S，乘章节系数后四舍五入。</summary>
	[Export] public int PointsC { get; set; } = 1;
	[Export] public int PointsB { get; set; } = 2;
	[Export] public int PointsA { get; set; } = 3;
	[Export] public int PointsS { get; set; } = 4;
}
