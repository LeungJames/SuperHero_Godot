using Godot;

/// <summary>
/// 枪械数值配置。只承载数值与资源引用，不含逻辑；对应设计文档 §4.2、§4.5、§4.7、§5.2。
/// 同一份资源既用于手枪，也用于冲锋枪 / 霰弹枪 / 狙击枪，差异只在数值与模型。
/// </summary>
[GlobalClass]
public partial class WeaponData : Resource
{
	[ExportGroup("标识与解锁")]
	/// <summary>稳定 ID：存档 unlocks 与武器库选择按它匹配，改名不影响存档。</summary>
	[Export] public string Id { get; set; } = string.Empty;

	[Export] public string DisplayName { get; set; } = "P-9";

	/// <summary>解锁章节：0 = 序章 / 第一章（港口），2 = 赤蛇工厂，3 = 铁锚据点。</summary>
	[Export] public int UnlockChapter { get; set; }

	/// <summary>第一人称视角模型（枢轴分件）。</summary>
	[Export] public PackedScene Viewmodel { get; set; }

	/// <summary>把视角模型的纯色材质换成 toon 平涂（按原材质的基色逐个替换 surface）。</summary>
	[Export] public bool ToonViewmodel { get; set; }

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
	/// <summary>空仓扣扳机的最短间隔（秒），避免按住开火时"咔咔"连成一片。</summary>
	[Export] public float DryFireCooldown { get; set; } = 0.35f;

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
	/// <summary>开镜姿势相对通用开镜位的额外偏移（米）；用于长枪把枪身推远，避免遮住准星。</summary>
	[Export] public Vector3 AdsPositionOffset { get; set; } = Vector3.Zero;
	/// <summary>开镜姿势相对腰射基准的额外旋转（度），与位置一起决定镜心位置。</summary>
	[Export] public Vector3 AdsRotationDegrees { get; set; } = Vector3.Zero;

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
	/// <summary>弹匣脱出点的垂直位移（米，负值向下）：沿弹匣井方向把匣拽出，滚转后朝屏幕左侧脱出。</summary>
	[Export] public float ReloadMagVerticalOffset { get; set; } = -0.12f;
	/// <summary>弹匣脱出点向武器左侧（屏幕左）的位移，保证离开握把后仍在视野下缘。</summary>
	[Export] public float ReloadMagLateral { get; set; } = 0.1f;
	/// <summary>弹匣脱出点朝相机方向的位移，制造"被手带走"的纵深感。</summary>
	[Export] public float ReloadMagPullBack { get; set; } = 0.03f;
	/// <summary>弹匣脱出时绕 X 轴的前倾角度（度），避免看起来像整块平移。</summary>
	[Export] public float ReloadMagTiltDegrees { get; set; } = 26f;
	/// <summary>拔匣后的惯性过冲比例（1.0 = 多冲出一个脱出点距离），随后回落。</summary>
	[Export] public float ReloadMagOutOvershoot { get; set; } = 0.22f;
	[Export] public float ReloadMagDropStart { get; set; } = 0.12f;
	[Export] public float ReloadMagDropEnd { get; set; } = 0.34f;
	/// <summary>新弹匣从脱出点插回（相位区间），末端加速顶到位。</summary>
	[Export] public float ReloadMagInsertStart { get; set; } = 0.46f;
	[Export] public float ReloadMagInsertEnd { get; set; } = 0.70f;
	/// <summary>拉套筒区间：快拉到底 → 短停 → 急促复进。</summary>
	[Export] public float ReloadSlideStart { get; set; } = 0.80f;
	[Export] public float ReloadSlideEnd { get; set; } = 0.90f;
	[Export] public float ReloadSlideTravel { get; set; } = 0.035f;

	/// <summary>抓握点相对弹匣枢轴的偏移（弹匣局部空间，米）：左手用恒定偏移绑在弹匣上。</summary>
	[Export] public Vector3 ReloadGripOffset { get; set; } = new(-0.11f, -0.04f, 0.03f);
	/// <summary>抓握姿态相对弹匣的旋转偏移（度）。</summary>
	[Export] public Vector3 ReloadGripRotationDegrees { get; set; } = Vector3.Zero;
	/// <summary>右手随套筒后拉的位移比例（相对 ReloadSlideTravel），保持握把接触。</summary>
	[Export] public float ReloadHandRSlideFollow { get; set; } = 0.35f;
	/// <summary>右手拉套筒时的后仰角度（度）。</summary>
	[Export] public float ReloadHandRTiltDegrees { get; set; } = 10f;
	/// <summary>左手离开护木（伸向弹匣）的区间；结束相位即抓握绑定起点。</summary>
	[Export] public float ReloadHandLeaveStart { get; set; } = 0.04f;
	[Export] public float ReloadHandLeaveEnd { get; set; } = 0.1f;
	/// <summary>左手回到护木的区间；起点相位即抓握绑定终点（弹匣到位帧）。</summary>
	[Export] public float ReloadHandReturnStart { get; set; } = 0.70f;
	[Export] public float ReloadHandReturnEnd { get; set; } = 0.78f;
	/// <summary>骨骼姿势跟随速度（越大越跟手）；取消 / 完成时也靠它平滑回位。</summary>
	[Export] public float ReloadBoneBlendSpeed { get; set; } = 14f;

	[ExportGroup("换弹冲击")]
	/// <summary>弹匣顶到位 / 套筒复进时枪身的瞬时下沉（米）。</summary>
	[Export] public float ReloadImpactDip { get; set; } = 0.012f;
	/// <summary>到位冲击的枪身抬头角（度）。</summary>
	[Export] public float ReloadImpactPitchDegrees { get; set; } = 5f;
	/// <summary>到位冲击的枪身侧倾角（度）。</summary>
	[Export] public float ReloadImpactRollDegrees { get; set; } = 3f;
	/// <summary>到位帧的镜头抖动幅度（度）与衰减时间（秒）。</summary>
	[Export] public float ReloadImpactShakeDegrees { get; set; } = 0.45f;
	[Export] public float ReloadImpactShakeTime { get; set; } = 0.12f;

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

	[ExportGroup("换弹姿势（配重）")]
	/// <summary>换弹时把枪抬进画面，露出弹匣井（米）。幅度小，但足以让匣与手都留在画面内。</summary>
	[Export] public float ReloadPoseLift { get; set; } = 0.05f;
	/// <summary>换弹时枪身向内滚转（度）；负值让弹匣朝屏幕左侧脱出，避开画面中心。</summary>
	[Export] public float ReloadPoseRollDegrees { get; set; } = -68f;
	/// <summary>换弹时枪口上抬角度（度）。</summary>
	[Export] public float ReloadPosePitchDegrees { get; set; } = 6f;
	[Export] public float ReloadPoseInStart { get; set; } = 0.03f;
	[Export] public float ReloadPoseInEnd { get; set; } = 0.13f;
	[Export] public float ReloadPoseOutStart { get; set; } = 0.74f;
	[Export] public float ReloadPoseOutEnd { get; set; } = 0.92f;
}
