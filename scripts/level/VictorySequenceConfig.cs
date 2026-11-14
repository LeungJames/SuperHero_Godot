using Godot;

/// <summary>
/// 胜利演出的时间轴与机位参数，供编辑器调参（设计文档 §2.3、§9.7）。
/// </summary>
[GlobalClass]
public partial class VictorySequenceConfig : Resource
{
	[Export] public float BannerTime { get; set; } = 0.2f;
	[Export] public float RadioStart { get; set; } = 0.4f;
	[Export] public float RadioEnd { get; set; } = 1.4f;
	[Export] public float ApproachStart { get; set; } = 1.4f;
	[Export] public float LandTime { get; set; } = 9.4f;
	[Export] public float BoardStart { get; set; } = 9.4f;
	[Export] public float BoardEnd { get; set; } = 11.4f;
	[Export] public float FadeStart { get; set; } = 11.4f;
	[Export] public float FadeEnd { get; set; } = 12.4f;
	[Export] public float ResultsTime { get; set; } = 12.4f;

	[Export] public string BannerText { get; set; } = "区域清空 · 撤离直升机抵达中";
	[Export] public string RadioText { get; set; } = "无线电：目标已清除，直升机正在进场，请到停机坪待撤。";

	[Export] public Vector3 HelicopterApproachFrom { get; set; } = new(96f, 46f, -170f);
	[Export] public Vector3 HelicopterApproachControl { get; set; } = new(52f, 34f, -104f);
	[Export] public Vector3 HelicopterLandingPosition { get; set; } = new(10f, 0.35f, -50f);
	[Export] public Vector3 CinematicCameraPosition { get; set; } = new(23f, 4.5f, -41f);
	[Export] public Vector3 BoardTargetOffset { get; set; } = new(0f, 0f, -2.2f);
	[Export] public float RotorSpinDegreesPerSecond { get; set; } = 1080f;
}
