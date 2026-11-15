using Godot;

/// <summary>
/// Boss 数值与场地配置（2.0 设计 §8.2、§8.5、§8.6）。
/// 只承载数值，不含逻辑；具体招式表见 <see cref="BossMoveSet"/>。
/// </summary>
[GlobalClass]
public partial class BossConfig : Resource
{
	[ExportGroup("身份与血量")]
	[Export] public string DisplayName { get; set; } = "霍克";
	[Export] public float MaxHealth { get; set; } = 3000f;
	[Export] public float Phase2Threshold { get; set; } = 0.70f;
	[Export] public float Phase3Threshold { get; set; } = 0.35f;

	[ExportGroup("弱点")]
	[Export] public float WeakPointMultiplier { get; set; } = 2f;
	/// <summary>每次收招转身时背部核心的暴露时长（§8.3）。</summary>
	[Export] public float WeakPointExposure { get; set; } = 2.5f;

	[ExportGroup("移动与节奏")]
	[Export] public float ChaseSpeed { get; set; } = 3.2f;
	[Export] public float EnrageSpeed { get; set; } = 4.0f;
	[Export] public float TurnSpeedDegrees { get; set; } = 220f;
	[Export] public float RecoverTime { get; set; } = 0.65f;
	[Export] public float TurnTime { get; set; } = 0.45f;
	[Export] public float PhaseShiftTime { get; set; } = 0.8f;
	[Export] public float AttackInterval { get; set; } = 0.6f;

	[ExportGroup("召唤")]
	[Export] public PackedScene SummonScene { get; set; }
	[Export] public int SummonCount { get; set; } = 2;
	[Export] public Vector3 SummonLeft { get; set; } = new(-12f, 0.1f, -14f);
	[Export] public Vector3 SummonRight { get; set; } = new(12f, 0.1f, -14f);

	[ExportGroup("死亡演出")]
	[Export] public float DeathSlowMotionScale { get; set; } = 0.35f;
	[Export] public float DeathSlowMotionRealTime { get; set; } = 1.2f;
	[Export] public float DeathBlackoutTime { get; set; } = 0.8f;

	[ExportGroup("场地")]
	[Export] public Vector3 ArenaCenter { get; set; } = new(0f, 0f, -17f);
	[Export] public Vector2 ArenaSize { get; set; } = new(30f, 30f);
	[Export] public Vector3 CheckpointPosition { get; set; } = new(0f, 0.2f, -4f);
	[Export] public float CheckpointYaw { get; set; }
	[Export] public Vector3 AnchorLeft { get; set; } = new(-12f, 7f, -17f);
	[Export] public Vector3 AnchorRight { get; set; } = new(12f, 7f, -17f);
	[Export] public Vector3 WinchPosition { get; set; } = new(0f, 0f, -29f);
	[Export] public float WinchHealth { get; set; } = 300f;
	[Export] public bool BuildArenaGeometry { get; set; } = true;
}
