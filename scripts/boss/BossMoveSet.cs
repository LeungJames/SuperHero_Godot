using Godot;

/// <summary>
/// Boss 招式表（2.0 设计 §8.3、§8.6）。数据全部在
/// <c>res://data/boss/anchor_boss_moves.tres</c>，不在代码里写死。
/// </summary>
[GlobalClass]
public partial class BossMoveSet : Resource
{
	[Export] public Godot.Collections.Array<BossMoveData> Moves { get; set; } = new();
}
