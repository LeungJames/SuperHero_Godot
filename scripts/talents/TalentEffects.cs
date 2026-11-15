using Godot;

/// <summary>
/// 天赋效果查询（设计文档 §5.6）：把已点节点翻译成移动 / 武器 / 近战组件能直接读的数值。
/// 本类不持有任何回血逻辑（零回血硬约束）；数值全部来自 <see cref="TalentEffectsConfig"/>。
/// </summary>
public static class TalentEffects
{
	private static TalentEffectsConfig _config;

	public static TalentEffectsConfig Config
	{
		get
		{
			if (_config == null)
			{
				const string path = "res://data/talents/effects_config.tres";
				_config = ResourceLoader.Exists(path) ? GD.Load<TalentEffectsConfig>(path) : new TalentEffectsConfig();
			}

			return _config;
		}
	}

	public static bool Has(string id) => SaveSystem.HasInstance && TalentSystem.IsPurchased(id);

	// ---------------------------------------------------------------- 战术机动

	public static bool WallRunExtended => Has("M1");
	public static bool AirDash => Has("M2");
	public static bool DoubleJump => Has("M3");
	public static bool Grapple => Has("M4");
	public static bool EdgeSpeed => Has("M5");
	public static bool SlideExtended => Has("M6");
	public static bool FallBuffer => Has("M8");

	public static float WallRunMaxTimeAdd => WallRunExtended ? Config.WallRunMaxTimeAdd : 0f;
	public static float WallRunFallScale => WallRunExtended ? Config.WallRunFallScale : 1f;
	public static float AirDashDistance => Config.AirDashDistance;
	public static float AirDashCooldown => Config.AirDashCooldown;
	public static float DoubleJumpVelocityScale => Mathf.Sqrt(Mathf.Max(0f, Config.DoubleJumpHeightScale));
	public static float GrappleRange => Config.GrappleRange;
	public static float GrappleCooldown => Config.GrappleCooldown;
	public static float EdgeSpeedBonus => EdgeSpeed ? Config.EdgeSpeedBonus : 0f;
	public static float EdgeSpeedDuration => Config.EdgeSpeedDuration;
	public static float SlideDistanceScale => SlideExtended ? Config.SlideDistanceScale : 1f;
	public static float FallSafeHeightAdd => FallBuffer ? Config.FallSafeHeightAdd : 0f;
	public static float FallDamageScale => FallBuffer ? Config.FallDamageScale : 1f;

	// ---------------------------------------------------------------- 武器大师

	public static bool MagazinePlus => Has("W1");
	public static bool ReloadFast => Has("W2");
	public static bool SwapFast => Has("W3");
	public static bool RecoilControl => Has("W4");
	public static bool ArmorPierce => Has("W5");
	public static bool AmmoRecover => Has("W6");
	public static bool AdsMaster => Has("W7");
	public static bool BurstFire => Has("W8");

	public static float MagazineScale => MagazinePlus ? Config.MagazineScale : 1f;
	public static float ReloadScale => ReloadFast ? Config.ReloadScale : 1f;
	public static float SwapScale => SwapFast ? Config.SwapScale : 1f;
	public static float SwapSpreadScale => SwapFast ? Config.SwapSpreadScale : 1f;
	public static float SwapSpreadWindow => Config.SwapSpreadWindow;
	public static float RecoilScale => RecoilControl ? Config.RecoilScale : 1f;
	public static float RecoilRecoveryScale => RecoilControl ? Config.RecoilRecoveryScale : 1f;
	public static float PierceDamageScale => Config.PierceDamageScale;
	public static int AmmoRecoverRounds => AmmoRecover ? Config.AmmoRecoverRounds : 0;
	public static float AdsTimeScale => AdsMaster ? Config.AdsTimeScale : 1f;
	public static float AdsMoveScale => AdsMaster ? Config.AdsMoveScale : 1f;
	public static int BurstHitCount => Config.BurstHitCount;
	public static float BurstDamageScale => Config.BurstDamageScale;

	// ---------------------------------------------------------------- 忍杀

	public static bool BladeSharp => Has("A1");
	public static bool SwiftSwing => Has("A2");
	public static bool SilentApproach => Has("A3");
	public static bool ExecutionExtended => Has("A4");
	public static bool CanBlock => Has("A5");
	public static bool ChainAssassination => Has("A6");
	public static bool KillSense => Has("A8");

	public static float MeleeDamageScale => BladeSharp ? Config.MeleeDamageScale : 1f;
	public static float SwingIntervalScale => SwiftSwing ? Config.SwingIntervalScale : 1f;
	public static float SlowWalkSpeed => SilentApproach ? Config.SlowWalkSpeed : 0f;
	public static float FootstepRangeScale => SilentApproach ? Config.FootstepRangeScale : 1f;
	public static float ExecutionRangeAdd => ExecutionExtended ? Config.ExecutionRangeAdd : 0f;
	public static float ExecutionDamageReduction => Config.ExecutionDamageReduction;
	public static float ExecutionReductionTime => Config.ExecutionReductionTime;
	public static float BlockWindow => Config.BlockWindow;
	public static float BlockCooldown => Config.BlockCooldown;
	public static float ChainWindow => Config.ChainWindow;
	public static float ChainRangeAdd => ChainAssassination ? Config.ChainRangeAdd : 0f;
	public static float SenseDuration => Config.SenseDuration;
	public static float SenseRadius => Config.SenseRadius;

	/// <summary>A7 需要先通关第二章才可加点（§5.2.1）。</summary>
	public static bool LaserUnlocked =>
		Has("A7") && SaveSystem.HasInstance && SaveSystem.Instance.IsChapterCleared("factory");

	/// <summary>剑气只在第三章起可释放；港口与工厂内即使已点也不可用。</summary>
	public static bool LaserUsable(string chapterId) =>
		LaserUnlocked && LoadoutSystem.ChapterNumber(chapterId) >= 3;
}
