using Godot;

/// <summary>
/// 一段漫画过场（2.0 设计 §4.4）。四段：港口开场、工厂开场、Boss 登场、Boss 死亡。
/// 只走 UI 层，不改 3D 管线。
/// </summary>
[GlobalClass]
public partial class CutsceneData : Resource
{
	[Export] public string Id { get; set; } = string.Empty;
	[Export] public string Title { get; set; } = string.Empty;
	[Export] public Godot.Collections.Array<CutscenePanelData> Panels { get; set; } = new();
	[Export] public float PanelFadeTime { get; set; } = 0.25f;
}
