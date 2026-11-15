using Godot;

/// <summary>天赋重置代价（设计文档 §5.4），路径 <c>res://data/talents/reset_config.tres</c>。</summary>
[GlobalClass]
public partial class TalentResetConfig : Resource
{
	/// <summary>每条线前几次重置免费。</summary>
	[Export] public int FreeResets { get; set; } = 1;
	/// <summary>免费次数用完后，每次重置从该线扣除的点数。</summary>
	[Export] public int PointCost { get; set; } = 2;
	/// <summary>是否允许关卡中途重置；默认 false（§5.4）。</summary>
	[Export] public bool AllowMidChapter { get; set; }
}
