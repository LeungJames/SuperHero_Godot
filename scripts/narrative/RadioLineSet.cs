using Godot;

/// <summary>
/// 一个帮派在一个语境下的无线电台词（2.0 设计 §4.3）。
/// 每个语境 3–5 条，播放时按冷却与 30s 去重随机抽取。
/// </summary>
[GlobalClass]
public partial class RadioLineSet : Resource
{
	[Export] public string GangId { get; set; } = "salt";
	/// <summary>spot / suppress / flank / reinforce / retreat / alert_up / clear。</summary>
	[Export] public string Context { get; set; } = "spot";
	[Export] public string[] Lines { get; set; } = System.Array.Empty<string>();
}
