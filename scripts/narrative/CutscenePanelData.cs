using Godot;

/// <summary>程序化漫画分格的画面类型（2.0 设计 §4.4，全部 UI 层绘制）。</summary>
public enum CutsceneArtKind
{
	Sea,
	Crane,
	Silhouette,
	Yard,
	Smoke,
	Snake,
	Door,
	Gate,
	Anchor,
	Aftermath,
}

/// <summary>单格分镜：文案 + 画面类型 + 时长 + 配色。</summary>
[GlobalClass]
public partial class CutscenePanelData : Resource
{
	[Export(PropertyHint.MultilineText)] public string Caption { get; set; } = string.Empty;
	[Export] public string SfxText { get; set; } = string.Empty;
	[Export] public CutsceneArtKind Art { get; set; } = CutsceneArtKind.Sea;
	[Export] public float Duration { get; set; } = 2.4f;
	[Export] public Color Sky { get; set; } = new(0.11f, 0.13f, 0.19f);
	[Export] public Color Accent { get; set; } = new(0.95f, 0.55f, 0.22f);
}
