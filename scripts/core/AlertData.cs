using Godot;

/// <summary>
/// 警报与增援参数：持续交战多久叫增援、几波、每波几人。对应设计文档 §5.3。
/// </summary>
[GlobalClass]
public partial class AlertData : Resource
{
	/// <summary>需要持续交战的时长（秒）才呼叫增援。</summary>
	[Export] public float AlertHoldTime { get; set; } = 3.5f;
	/// <summary>两波增援之间的最小间隔（秒）。</summary>
	[Export] public float WaveInterval { get; set; } = 16f;
	/// <summary>最多几波。</summary>
	[Export] public int MaxWaves { get; set; } = 2;
	/// <summary>每波人数。</summary>
	[Export] public int WaveSize { get; set; } = 2;
	/// <summary>同一波里每个敌人的出场间隔（秒）。</summary>
	[Export] public float SpawnStagger { get; set; } = 0.45f;
	/// <summary>增援模板。</summary>
	[Export] public PackedScene ReinforceScene { get; set; }
	/// <summary>呼叫增援时的提示。</summary>
	[Export] public string CallMessage { get; set; } = "盐帮增援正在赶来";
	[Export] public string ArriveMessage { get; set; } = "增援抵达";
}
