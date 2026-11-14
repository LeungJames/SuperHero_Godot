using Godot;

/// <summary>
/// 枪械数值配置。只承载数值与资源引用，不含逻辑；对应设计文档 §4.2、§4.5、§4.7、§5.2。
/// 同一份资源既用于手枪，也用于冲锋枪 / 霰弹枪 / 狙击枪，差异只在数值与模型。
/// </summary>
[GlobalClass]
public partial class WeaponData : Resource
{
	[ExportGroup("标识与解锁")]
	[Export] public string DisplayName { get; set; } = "P-9";

	/// <summary>解锁章节：0 = 序章 / 第一章（港口），2 = 赤蛇工厂，3 = 铁锚据点。</summary>
	[Export] public int UnlockChapter { get; set; }

	/// <summary>第一人称视角模型（枢轴分件）。</summary>
	[Export] public PackedScene Viewmodel { get; set; }

	/// <summary>枪口位置（相对武器节点，Godot 坐标：-Z 朝前）。</summary>
	[Export] public Vector3 MuzzleOffset { get; set; } = new(0f, 0.026f, -0.21f);

	[ExportGroup("伤害")]
	[Export] public float BaseDamage { get; set; } = 40f;
	[Export] public float FullDamageDistance { get; set; } = 25f;
	[Export] public float MinDamageDistance { get; set; } = 50f;
	[Export] public float MinDamageMultiplier { get; set; } = 0.6f;

	[ExportGroup("射击")]
	[Export] public float ShotsPerSecond { get; set; } = 5.5f;
	/// <summary>按住左键持续射击（冲锋枪）。false 时为半自动（手枪、霰弹枪、狙击枪）。</summary>
	[Export] public bool Automatic { get; set; }
	/// <summary>单次击发的弹丸数（霰弹枪 8），每颗独立散布、伤害按各自数值结算。</summary>
	[Export] public int PelletsPerShot { get; set; } = 1;
	[Export] public int MagazineSize { get; set; } = 12;
	[Export] public int StartingReserve { get; set; } = 36;
	[Export] public int MaxReserve { get; set; } = 60;
	[Export] public float ReloadTime { get; set; } = 2f;
	[Export] public float MaxRange { get; set; } = 200f;

	[ExportGroup("散布（度）")]
	[Export] public float HipSpread { get; set; } = 1.2f;
	[Export] public float AdsSpread { get; set; } = 0.25f;
	[Export] public float MovingSpreadPenalty { get; set; } = 1.2f;
	[Export] public float AirSpreadPenalty { get; set; } = 3f;
	/// <summary>滑铲中开火的额外散布（度），机动越快精度越差（§5.1）。</summary>
	[Export] public float SlideSpreadPenalty { get; set; } = 2.5f;

	[ExportGroup("开镜")]
	[Export] public float AdsTime { get; set; } = 0.15f;
	[Export] public float AdsMoveMultiplier { get; set; } = 0.55f;
	[Export] public float AdsRecoilMultiplier { get; set; } = 0.7f;
	/// <summary>开镜后的相机 FOV（狙击枪用小值做瞄准镜效果）。</summary>
	[Export] public float AdsFov { get; set; } = 55f;

	[ExportGroup("后坐（度）")]
	[Export] public float RecoilPitch { get; set; } = 1.6f;
	[Export] public float RecoilYaw { get; set; } = 0.35f;
	[Export] public float RecoilRecovery { get; set; } = 14f;
	[Export] public float RecoilStackPerShot { get; set; } = 0.1f;
	[Export] public float RecoilStackMax { get; set; } = 2f;
	/// <summary>停火多久后连射后坐累积归零（秒）。</summary>
	[Export] public float RecoilStackResetTime { get; set; } = 0.35f;

	[ExportGroup("换弹")]
	[Export] public float ReloadMoveMultiplier { get; set; } = 0.75f;

	[ExportGroup("换弹动画（枢轴驱动，归一化相位）")]
	/// <summary>弹匣掉出握把的距离（米，沿握把向下）。</summary>
	[Export] public float ReloadMagDropDistance { get; set; } = 0.24f;
	[Export] public float ReloadMagDropStart { get; set; } = 0.12f;
	[Export] public float ReloadMagDropEnd { get; set; } = 0.34f;
	/// <summary>新弹匣从下方插回（相位区间），结束前有轻微过冲。</summary>
	[Export] public float ReloadMagInsertStart { get; set; } = 0.50f;
	[Export] public float ReloadMagInsertEnd { get; set; } = 0.66f;
	/// <summary>拉套筒区间：先拉到底再瞬间复进。</summary>
	[Export] public float ReloadSlideStart { get; set; } = 0.70f;
	[Export] public float ReloadSlideEnd { get; set; } = 0.80f;
	[Export] public float ReloadSlideTravel { get; set; } = 0.035f;
	/// <summary>换弹时左手离开握把的下沉角度（度）。</summary>
	[Export] public float ReloadHandDropDegrees { get; set; } = 34f;
	[Export] public float ReloadHandLeaveStart { get; set; } = 0.06f;
	[Export] public float ReloadHandLeaveEnd { get; set; } = 0.20f;
	[Export] public float ReloadHandReturnStart { get; set; } = 0.62f;
	[Export] public float ReloadHandReturnEnd { get; set; } = 0.78f;
	/// <summary>骨骼姿势跟随速度（越大越跟手）；取消 / 完成时也靠它平滑回位。</summary>
	[Export] public float ReloadBoneBlendSpeed { get; set; } = 14f;

	[ExportGroup("表现（§4.7）")]
	[Export] public float MuzzleFlashTime { get; set; } = 0.05f;
	[Export] public float FireKickBack { get; set; } = 0.035f;
	[Export] public float FireKickPitch { get; set; } = 4f;
	[Export] public float FireKickRecoverTime { get; set; } = 0.08f;
	[Export] public float FireKickPitchRecoverTime { get; set; } = 0.12f;
	[Export] public float SwayAmountX { get; set; } = 0.012f;
	[Export] public float SwayAmountY { get; set; } = 0.008f;
	[Export] public float AdsSwayScale { get; set; } = 0.25f;
	[Export] public float AdsSwayRateScale { get; set; } = 0.6f;
	[Export] public float ReloadDrop { get; set; } = 0.12f;
	[Export] public float ReloadRoll { get; set; } = 25f;
}
