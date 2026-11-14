using Godot;

/// <summary>
/// 玩家数值配置。只承载数值，不含逻辑；对应设计文档 §3.1 与 §3.4。
/// </summary>
[GlobalClass]
public partial class PlayerData : Resource
{
	[Export] public float MaxHealth { get; set; } = 100f;

	[Export] public float WalkSpeed { get; set; } = 4.8f;
	[Export] public float SprintSpeed { get; set; } = 6.2f;
	[Export] public float SlowWalkSpeed { get; set; } = 2.2f;

	[Export] public float JumpVelocity { get; set; } = 5f;
	[Export] public float Gravity { get; set; } = 18f;

	[Export] public float GroundAcceleration { get; set; } = 60f;
	[Export] public float GroundFriction { get; set; } = 50f;
	[Export] public float AirAcceleration { get; set; } = 12f;

	[Export] public float CoyoteTime { get; set; } = 0.12f;
	[Export] public float JumpBuffer { get; set; } = 0.1f;

	[Export] public float MouseSensitivity { get; set; } = 0.12f;

	[Export] public float DefaultFov { get; set; } = 75f;
	[Export] public float AdsFov { get; set; } = 55f;
	/// <summary>ADS 期间的鼠标灵敏度倍率。</summary>
	[Export] public float AdsSensitivityScale { get; set; } = 0.7f;

	/// <summary>落差超过该值才计算摔伤（米）。</summary>
	[Export] public float FallSafeHeight { get; set; } = 4f;

	/// <summary>超出安全落差后每米伤害。</summary>
	[Export] public float FallDamagePerMeter { get; set; } = 14f;

	/// <summary>落地垂直速度低于该值时触发相机下沉（m/s）。</summary>
	[Export] public float LandingImpactSpeed { get; set; } = 6f;
}
