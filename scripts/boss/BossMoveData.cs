using Godot;

public enum BossMoveKind
{
	/// <summary>锚钩横扫：前方扇形。</summary>
	Sweep,
	/// <summary>冲撞：直线 12m。</summary>
	Charge,
	/// <summary>锚链拉拽：20m 直线 + 拉近。</summary>
	ChainPull,
	/// <summary>召唤打手：场地两侧，仅一次。</summary>
	Summon,
	/// <summary>落锚雨：3 个 4m 圆并砸断 1 根柱。</summary>
	AnchorRain,
	/// <summary>双钩连击：前方两段。</summary>
	DoubleHook,
}

/// <summary>
/// 单个 Boss 招式（2.0 设计 §8.3）。前摇、伤害、范围与冷却全部外置到 tres。
/// </summary>
[GlobalClass]
public partial class BossMoveData : Resource
{
	[Export] public string Id { get; set; } = string.Empty;
	[Export] public BossMoveKind Kind { get; set; } = BossMoveKind.Sweep;

	/// <summary>可用阶段位掩码：bit0=P1、bit1=P2、bit2=P3。</summary>
	[Export] public int PhaseMask { get; set; } = 1;

	[Export] public float Windup { get; set; } = 0.6f;
	[Export] public float Damage { get; set; } = 60f;
	[Export] public float Range { get; set; } = 4f;
	[Export] public float AngleDegrees { get; set; } = 120f;
	[Export] public float Cooldown { get; set; } = 3f;

	[ExportGroup("第二段（双钩）")]
	[Export] public float SecondWindup { get; set; } = 0.4f;
	[Export] public float SecondDamage { get; set; } = 60f;

	[ExportGroup("特殊")]
	[Export] public float ChargeDistance { get; set; } = 12f;
	[Export] public float PullDistance { get; set; } = 8f;
	[Export] public float Radius { get; set; } = 4f;
	[Export] public string Telegraph { get; set; } = "CLANG!";
	[Export] public string Sfx { get; set; } = "melee";
}
