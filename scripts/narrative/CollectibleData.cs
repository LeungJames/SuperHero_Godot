using Godot;

/// <summary>收集品在关卡里的位置类型（2.0 设计 §4.2 的选点规则）。</summary>
public enum CollectiblePlacement
{
	MainPath,
	StealthRoute,
	Hidden,
}

/// <summary>
/// 单件收集品（2.0 设计 §4.2）。只提供叙事文本，不提供任何数值收益。
/// 数据放在 res://data/narrative/collectibles/&lt;chapter&gt;/&lt;id&gt;.tres。
/// </summary>
[GlobalClass]
public partial class CollectibleData : Resource
{
	[Export] public string Id { get; set; } = string.Empty;
	[Export] public string ChapterId { get; set; } = "port";
	[Export] public string GangId { get; set; } = "salt";
	[Export] public string Name { get; set; } = string.Empty;
	[Export(PropertyHint.MultilineText)] public string Text { get; set; } = string.Empty;
	/// <summary>世界坐标；由关卡作者在编辑器里调整。</summary>
	[Export] public Vector3 Position { get; set; }
	[Export] public CollectiblePlacement Placement { get; set; } = CollectiblePlacement.MainPath;
}
