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

	[ExportGroup("动画（程序化步态）")]
	/// <summary>腿长（米），用于由步幅反解摆腿角度。</summary>
	[Export] public float AnimLegLength { get; set; } = 0.86f;
	/// <summary>基准步幅（米/完整步态周期），步频在此附近浮动。</summary>
	[Export] public float AnimCycleBase { get; set; } = 1.5f;
	[Export] public float AnimCadenceMin { get; set; } = 0.85f;
	[Export] public float AnimCadenceMax { get; set; } = 2.1f;
	/// <summary>屈膝角度上限（度）。</summary>
	[Export] public float AnimKneeBend { get; set; } = 42f;
	/// <summary>髋部起伏（米）。</summary>
	[Export] public float AnimBob { get; set; } = 0.035f;
	[Export] public float AnimHipRoll { get; set; } = 2.5f;
	/// <summary>上身反向扭转（度）。</summary>
	[Export] public float AnimTorsoCounter { get; set; } = 4f;
	/// <summary>满速前倾（度）。</summary>
	[Export] public float AnimLean { get; set; } = 5f;
	/// <summary>站定 ↔ 行走的姿势过渡时间（秒）。</summary>
	[Export] public float AnimBlendTime { get; set; } = 0.18f;
}
