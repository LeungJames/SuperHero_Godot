using Godot;

/// <summary>一个小队的编制：队长与成员用关卡内的节点名指定，避免写死坐标。</summary>
[GlobalClass]
public partial class SquadEntry : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string Leader { get; set; } = "";
	[Export] public string[] Members { get; set; } = System.Array.Empty<string>();
}
