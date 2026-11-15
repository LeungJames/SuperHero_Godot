using Godot;

/// <summary>
/// 潜行支路节点（2.0 设计 §3.2、§3.7）。只描述路线，不含行为；
/// 三关各一份，坐标可在编辑器里直接改，不需要动代码。
/// </summary>
[GlobalClass]
public partial class StealthRoute : Resource
{
	[Export] public string ChapterId { get; set; } = "port";
	/// <summary>支路入口。</summary>
	[Export] public Vector3 EntryPoint { get; set; }
	/// <summary>与主线汇合点。</summary>
	[Export] public Vector3 MergePoint { get; set; }
	/// <summary>汇合点前的掩体。</summary>
	[Export] public Vector3 CoverPoint { get; set; }
	/// <summary>支路折线点，按行走顺序排列。</summary>
	[Export] public Vector3[] Waypoints { get; set; } = System.Array.Empty<Vector3>();
	/// <summary>实现说明 / 已知几何缺口，供策划与关卡美术核对。</summary>
	[Export(PropertyHint.MultilineText)] public string Note { get; set; } = string.Empty;
}
