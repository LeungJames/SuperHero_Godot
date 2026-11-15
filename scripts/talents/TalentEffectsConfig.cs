using Godot;

/// <summary>
/// 天赋数值表（§5.2 的全部百分比与距离/时间）。代码只读取，不写死；改平衡只改
/// <c>res://data/talents/effects_config.tres</c>。
/// </summary>
[GlobalClass]
public partial class TalentEffectsConfig : Resource
{
	[ExportGroup("战术机动")]
	[Export] public float WallRunMaxTimeAdd { get; set; } = 0.4f;
	[Export] public float WallRunFallScale { get; set; } = 0.75f;
	[Export] public float AirDashDistance { get; set; } = 6f;
	[Export] public float AirDashCooldown { get; set; } = 4f;
	/// <summary>二段跳高度相对首次跳跃的比例（速度按平方根换算）。</summary>
	[Export] public float DoubleJumpHeightScale { get; set; } = 0.6f;
	[Export] public float GrappleRange { get; set; } = 40f;
	[Export] public float GrappleCooldown { get; set; } = 1.2f;
	[Export] public float EdgeSpeedBonus { get; set; } = 0.15f;
	[Export] public float EdgeSpeedDuration { get; set; } = 2f;
	[Export] public float SlideDistanceScale { get; set; } = 1.3f;
	[Export] public float FallSafeHeightAdd { get; set; } = 4f;
	[Export] public float FallDamageScale { get; set; } = 0.7f;

	[ExportGroup("武器大师")]
	[Export] public float MagazineScale { get; set; } = 1.25f;
	[Export] public float ReloadScale { get; set; } = 0.8f;
	[Export] public float SwapScale { get; set; } = 0.6f;
	[Export] public float SwapSpreadScale { get; set; } = 0.5f;
	[Export] public float SwapSpreadWindow { get; set; } = 1f;
	[Export] public float RecoilScale { get; set; } = 0.8f;
	[Export] public float RecoilRecoveryScale { get; set; } = 1.15f;
	[Export] public float PierceDamageScale { get; set; } = 0.85f;
	[Export] public int AmmoRecoverRounds { get; set; } = 4;
	[Export] public float AdsTimeScale { get; set; } = 0.75f;
	[Export] public float AdsMoveScale { get; set; } = 0.7f;
	[Export] public int BurstHitCount { get; set; } = 3;
	[Export] public float BurstDamageScale { get; set; } = 1.3f;

	[ExportGroup("忍杀")]
	[Export] public float MeleeDamageScale { get; set; } = 1.3f;
	[Export] public float SwingIntervalScale { get; set; } = 0.8f;
	[Export] public float SlowWalkSpeed { get; set; } = 3f;
	[Export] public float FootstepRangeScale { get; set; } = 0.6f;
	[Export] public float ExecutionRangeAdd { get; set; } = 1.5f;
	[Export] public float ExecutionDamageReduction { get; set; } = 0.5f;
	[Export] public float ExecutionReductionTime { get; set; } = 1f;
	[Export] public float BlockWindow { get; set; } = 0.25f;
	[Export] public float BlockCooldown { get; set; } = 6f;
	[Export] public float ChainWindow { get; set; } = 2f;
	[Export] public float ChainRangeAdd { get; set; } = 2f;
	[Export] public float LaserDamage { get; set; } = 60f;
	[Export] public float LaserRange { get; set; } = 25f;
	[Export] public float LaserWidth { get; set; } = 1.2f;
	[Export] public float LaserCooldown { get; set; } = 15f;
	[Export] public float LaserStockScale { get; set; } = 0.8f;
	[Export] public float SenseDuration { get; set; } = 5f;
	[Export] public float SenseRadius { get; set; } = 8f;
}
