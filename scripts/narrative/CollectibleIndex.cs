using Godot;

/// <summary>
/// 15 件收集品的总索引（2.0 设计 §4.2）。菜单与关卡按章过滤，避免运行时扫目录。
/// </summary>
[GlobalClass]
public partial class CollectibleIndex : Resource
{
	[Export] public Godot.Collections.Array<CollectibleData> Items { get; set; } = new();
}
