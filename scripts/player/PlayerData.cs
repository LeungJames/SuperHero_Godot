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

	[ExportGroup("角色视觉")]
	/// <summary>全身模型相对于素材原始身高（2.01m）的缩放，使可见身高与 1.8m 胶囊一致；只影响外观，不动胶囊与移动数值。</summary>
	[Export] public float ModelScale { get; set; } = 0.895f;

	[ExportGroup("相机 · 第三人称")]
	/// <summary>第三人称相机到头部枢轴的距离（米）。</summary>
	[Export] public float ThirdPersonDistance { get; set; } = 2.6f;
	/// <summary>第三人称相机向右的肩部偏移（米），避免身体挡住准星。</summary>
	[Export] public float ThirdPersonShoulderOffset { get; set; } = 0.42f;
	/// <summary>第三人称相机相对头部的高度偏移（米）。</summary>
	[Export] public float ThirdPersonHeightOffset { get; set; } = 0.12f;
	/// <summary>第三人称相机遮挡回拉时与墙面保持的半径（米）。</summary>
	[Export] public float CameraCollisionRadius { get; set; } = 0.22f;
	/// <summary>第一 / 第三人称切换的相机过渡时间（秒）。</summary>
	[Export] public float CameraModeBlendTime { get; set; } = 0.2f;

	/// <summary>落差超过该值才计算摔伤（米）。</summary>
	[Export] public float FallSafeHeight { get; set; } = 4f;

	/// <summary>超出安全落差后每米伤害。</summary>
	[Export] public float FallDamagePerMeter { get; set; } = 14f;

	/// <summary>落地垂直速度低于该值时触发相机下沉（m/s）。</summary>
	[Export] public float LandingImpactSpeed { get; set; } = 6f;

	// ---------------------------------------------------------------- 机动（§5.1）
	// 以下数值都是首版默认值，可在 data/player/PlayerData.tres 里直接调。

	[ExportGroup("机动 · 滑铲")]
	/// <summary>触发滑铲所需的水平速度（m/s），必须处于冲刺状态。</summary>
	[Export] public float SlideMinSpeed { get; set; } = 5f;
	/// <summary>滑铲初速 = max(当前速度, SprintSpeed) × 该倍率。</summary>
	[Export] public float SlideBoost { get; set; } = 1.12f;
	/// <summary>滑铲速度衰减（m/s²）。</summary>
	[Export] public float SlideFriction { get; set; } = 7f;
	/// <summary>速度低于该值自动结束滑铲（m/s）。</summary>
	[Export] public float SlideEndSpeed { get; set; } = 3f;
	/// <summary>滑铲最长时间（秒）。</summary>
	[Export] public float SlideMaxTime { get; set; } = 1.1f;
	/// <summary>滑铲时的转向速率（度/秒），用于贴着方向键微调。</summary>
	[Export] public float SlideSteerDegrees { get; set; } = 90f;
	/// <summary>滑铲时的碰撞胶囊高度（米）。</summary>
	[Export] public float SlideCapsuleHeight { get; set; } = 1.2f;
	/// <summary>滑铲时的视点高度（米）。</summary>
	[Export] public float SlideEyeHeight { get; set; } = 1.02f;
	/// <summary>蹲伏 / 起身的过渡时间（秒）。</summary>
	[Export] public float CrouchBlendTime { get; set; } = 0.12f;

	[ExportGroup("机动 · 蹬墙跑")]
	/// <summary>贴墙所需的最低水平速度（m/s）。</summary>
	[Export] public float WallRunMinSpeed { get; set; } = 4f;
	/// <summary>单次蹬墙跑最长时间（秒）。</summary>
	[Export] public float WallRunMaxTime { get; set; } = 1.2f;
	/// <summary>沿墙下滑加速度（m/s²），远小于正常重力。</summary>
	[Export] public float WallRunGravity { get; set; } = 2.2f;
	/// <summary>沿墙下滑速度上限（m/s）。</summary>
	[Export] public float WallRunMaxFall { get; set; } = 2.5f;
	/// <summary>贴墙推力（m/s），避免跑离墙面。</summary>
	[Export] public float WallRunStickSpeed { get; set; } = 1.6f;
	/// <summary>蹬墙跑探墙的射线长度（米）。</summary>
	[Export] public float WallRunProbeDistance { get; set; } = 0.55f;
	/// <summary>蹬墙起跳的向上 / 离墙速度（m/s）。</summary>
	[Export] public float WallRunJumpUp { get; set; } = 5f;
	[Export] public float WallRunJumpOut { get; set; } = 5.5f;
	/// <summary>蹬墙跑的相机侧倾（度）。</summary>
	[Export] public float WallRunCameraRoll { get; set; } = 8f;
	/// <summary>脱离墙面后再次贴墙的冷却（秒），防止同一面墙反复贴。</summary>
	[Export] public float WallRunCooldown { get; set; } = 0.35f;

	[ExportGroup("机动 · 翻越")]
	/// <summary>可翻越的最高障碍（米），超过则视为墙。</summary>
	[Export] public float VaultMaxHeight { get; set; } = 1.25f;
	/// <summary>低于该高度直接走上去，不触发翻越（米）。</summary>
	[Export] public float VaultMinHeight { get; set; } = 0.4f;
	/// <summary>向前探障碍的距离（米）。</summary>
	[Export] public float VaultReach { get; set; } = 0.85f;
	/// <summary>翻越耗时（秒）。</summary>
	[Export] public float VaultDuration { get; set; } = 0.35f;
	/// <summary>落点越过障碍边缘的距离（米）。</summary>
	[Export] public float VaultLandingOffset { get; set; } = 0.45f;
	/// <summary>翻越结束时保留的水平速度下限（m/s）。</summary>
	[Export] public float VaultExitSpeed { get; set; } = 4.5f;

	[ExportGroup("机动 · 边缘攀爬")]
	/// <summary>悬挂探测的前向距离（米）。</summary>
	[Export] public float LedgeGrabReach { get; set; } = 0.75f;
	/// <summary>悬挂探测的胸口高度（米，相对脚底）。</summary>
	[Export] public float LedgeProbeHeight { get; set; } = 1.35f;
	/// <summary>可抓取边缘相对脚底的高度区间（米）。</summary>
	[Export] public float LedgeMinHeight { get; set; } = 1.05f;
	[Export] public float LedgeMaxHeight { get; set; } = 2.3f;
	/// <summary>悬挂时脚底到边缘的高度差（米），决定挂出来的身体位置。</summary>
	[Export] public float LedgeHangOffset { get; set; } = 1.85f;
	/// <summary>悬挂时贴墙修正速度（1/秒），越大越稳。</summary>
	[Export] public float LedgeHangStiffness { get; set; } = 12f;
	/// <summary>上翻耗时（秒）。</summary>
	[Export] public float LedgeClimbTime { get; set; } = 0.55f;
}
