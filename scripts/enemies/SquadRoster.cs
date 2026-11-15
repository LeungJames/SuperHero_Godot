using Godot;

/// <summary>
/// 每关一份的小队编制表（2.0 设计 §2.2、§2.8）。
/// 关卡里的敌人数量与摆放不变，只按这张表重新分组；数据全部来自 .tres。
/// </summary>
[GlobalClass]
public partial class SquadRoster : Resource
{
	[Export] public SquadConfig Config { get; set; }
	[Export] public Godot.Collections.Array<SquadEntry> Squads { get; set; } = new();
}
