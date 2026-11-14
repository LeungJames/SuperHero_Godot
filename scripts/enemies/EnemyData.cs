using Godot;

public enum EnemyBehavior
{
	/// <summary>街头枪手：主动压上。</summary>
	Runner,

	/// <summary>守卫：守掩体、探头压制。</summary>
	Holder,
}

/// <summary>
/// 敌人数值配置。只承载数值，不含逻辑；对应设计文档 §5.1–§5.4。
/// </summary>
[GlobalClass]
public partial class EnemyData : Resource
{
	[ExportGroup("身份")]
	[Export] public EnemyBehavior Behavior { get; set; } = EnemyBehavior.Runner;
	[Export] public float MaxHealth { get; set; } = 90f;

	[ExportGroup("移动")]
	[Export] public float PatrolSpeed { get; set; } = 1.8f;
	[Export] public float CombatSpeed { get; set; } = 4.2f;

	[ExportGroup("感知")]
	[Export] public float ViewDistance { get; set; } = 18f;
	[Export] public float ViewAngleHorizontal { get; set; } = 120f;
	[Export] public float ViewAngleVertical { get; set; } = 35f;
	[Export] public float HearingRadius { get; set; } = 12f;
	[Export] public float ReactionTime { get; set; } = 0.3f;
	/// <summary>敌人在高处时的视野加成（米，§5.2）。</summary>
	[Export] public float HighGroundViewBonus { get; set; } = 8f;
	/// <summary>敌人在低处时的视野倍率（§5.2）。</summary>
	[Export] public float LowGroundViewScale { get; set; } = 0.6f;
	/// <summary>超过该垂直落差只进入 Alert，不进入 Combat（§5.2）。</summary>
	[Export] public float VerticalCombatLimit { get; set; } = 20f;

	[ExportGroup("交战")]
	[Export] public float EngageMinRange { get; set; } = 4f;
	[Export] public float EngageMaxRange { get; set; } = 9f;
	[Export] public int BurstShots { get; set; } = 2;
	[Export] public float BurstShotInterval { get; set; } = 0.3f;
	[Export] public float BurstCooldown { get; set; } = 1.2f;
	[Export] public float Damage { get; set; } = 30f;
	[Export] public float SpreadClose { get; set; } = 3.5f;
	[Export] public float SpreadMid { get; set; } = 5f;
	[Export] public float SpreadFar { get; set; } = 7f;
	[Export] public float SpreadMidDistance { get; set; } = 8f;
	[Export] public float SpreadFarDistance { get; set; } = 15f;
	/// <summary>玩家移动 / 空中时附加的散布（§5.2）。</summary>
	[Export] public float PlayerMovingSpreadBonus { get; set; } = 2f;
	[Export] public float LoseSightTime { get; set; } = 1f;
	[Export] public float AlertTimeout { get; set; } = 2f;
	[Export] public float SearchTimeout { get; set; } = 4f;

	[ExportGroup("掩体（守卫）")]
	[Export] public float PeekDuration { get; set; } = 1.2f;
	[Export] public float HideDuration { get; set; } = 1f;

	[ExportGroup("巡逻")]
	[Export] public float PatrolWaitMin { get; set; } = 1f;
	[Export] public float PatrolWaitMax { get; set; } = 2f;

	[ExportGroup("受击与死亡")]
	[Export] public float StaggerThreshold { get; set; } = 30f;
	[Export] public float StaggerDuration { get; set; } = 0.3f;
	[Export] public float DeathFallTime { get; set; } = 0.35f;
	[Export] public float DeathFadeTime { get; set; } = 2f;
	[Export] public int AmmoDropRounds { get; set; } = 6;

	[ExportGroup("动画（骨架动作）")]
	/// <summary>walk / run / aim_walk 剪辑自带的前进速度（米/秒），用于按实际移速缩放播放速率。</summary>
	[Export] public float WalkClipSpeed { get; set; } = 0.77f;
	[Export] public float RunClipSpeed { get; set; } = 1.77f;
	[Export] public float AimWalkClipSpeed { get; set; } = 0.65f;
	[Export] public float MinPlaybackScale { get; set; } = 0.55f;
	[Export] public float MaxPlaybackScale { get; set; } = 2.8f;
	/// <summary>状态之间的交叉淡化时间（秒）。</summary>
	[Export] public float StateBlendTime { get; set; } = 0.15f;
	/// <summary>三档 LOD 的距离阈值（米）。</summary>
	[Export] public float Lod0Distance { get; set; } = 10f;
	[Export] public float Lod1Distance { get; set; } = 25f;

	[ExportGroup("武器挂点")]
	/// <summary>敌人手持的枪模型（沿用 WPN_*.glb）；不填则只播动作不显示枪。</summary>
	[Export] public PackedScene WeaponScene { get; set; }
	/// <summary>握把相对 hand_r 的偏移（米，武器自身坐标系）。</summary>
	[Export] public Vector3 WeaponGripOffset { get; set; } = Vector3.Zero;
	/// <summary>枪口相对武器原点的位置（米），曳光与枪火从这里出。</summary>
	[Export] public Vector3 WeaponMuzzleOffset { get; set; } = new(0f, 0.026f, -0.21f);
}
