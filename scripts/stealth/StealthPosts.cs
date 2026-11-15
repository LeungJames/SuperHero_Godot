using Godot;

/// <summary>
/// 一关的哨位归类表（2.0 设计 §3.3、§3.7）。每关一份，引用现有关卡里的敌人节点名。
/// </summary>
[GlobalClass]
public partial class StealthPosts : Resource
{
	[Export] public string ChapterId { get; set; } = "port";
	[Export] public Godot.Collections.Array<StealthPostData> Posts { get; set; } = new();
}
