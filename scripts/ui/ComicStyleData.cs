using Godot;

/// <summary>
/// 漫画化表现参数：网点、速度线、拟声词、分镜框与受击墨线。
/// 只承载数值与颜色，不含逻辑；对应设计文档 §4.2.2–§4.2.4。
/// </summary>
[GlobalClass]
public partial class ComicStyleData : Resource
{
	[ExportGroup("配色")]
	[Export] public Color InkColor { get; set; } = new(0.06f, 0.05f, 0.09f);
	[Export] public Color PaperColor { get; set; } = new(0.97f, 0.93f, 0.85f);
	[Export] public Color AccentColor { get; set; } = new(0.89f, 0.34f, 0.18f);
	[Export] public Color KillColor { get; set; } = new(0.85f, 0.24f, 0.22f);

	[ExportGroup("网点")]
	/// <summary>网点间距（像素）。</summary>
	[Export] public float HalftoneSpacing { get; set; } = 14f;
	/// <summary>受击 / 击杀时的网点强度（0~1）。</summary>
	[Export] public float HalftoneStrength { get; set; } = 0.45f;
	/// <summary>网点从屏幕边缘多深开始出现（0~1）。</summary>
	[Export] public float HalftoneEdgeStart { get; set; } = 0.42f;
	/// <summary>网点衰减时间（秒）。</summary>
	[Export] public float HalftoneFadeTime { get; set; } = 0.7f;

	[ExportGroup("速度线")]
	/// <summary>速度线最大强度（0~1）。</summary>
	[Export] public float SpeedLineStrength { get; set; } = 0.55f;
	[Export] public float SpeedLineCount { get; set; } = 84f;
	/// <summary>低于该速度不出现速度线（m/s）。</summary>
	[Export] public float SpeedLineMinSpeed { get; set; } = 6.2f;
	/// <summary>速度线从屏幕中心多远开始（0~1），越大越靠边。</summary>
	[Export] public float SpeedLineInner { get; set; } = 0.42f;
	/// <summary>强度跟随速度的平滑速度（1/秒）。</summary>
	[Export] public float SpeedLineBlend { get; set; } = 8f;

	[ExportGroup("拟声词")]
	/// <summary>单个拟声词的持续时间（秒）。</summary>
	[Export] public float PopupLifetime { get; set; } = 0.75f;
	[Export] public int PopupFontSize { get; set; } = 54;
	/// <summary>弹出时上浮的像素距离。</summary>
	[Export] public float PopupRise { get; set; } = 34f;
	/// <summary>随机旋转幅度（度），让拟声词像手贴上去的。</summary>
	[Export] public float PopupTilt { get; set; } = 12f;

	[ExportGroup("分镜框 / 墨线")]
	/// <summary>击杀分镜框持续时间（秒）。</summary>
	[Export] public float KillPanelTime { get; set; } = 0.55f;
	/// <summary>分镜框黑边厚度（像素）。</summary>
	[Export] public float KillPanelThickness { get; set; } = 46f;
	/// <summary>受击墨线厚度（像素）与持续时间。</summary>
	[Export] public float HitInkThickness { get; set; } = 26f;
	[Export] public float HitInkTime { get; set; } = 0.35f;
}
