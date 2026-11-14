using Godot;

/// <summary>
/// 近战（枪托 / 刀）数值配置。只承载数值，不含逻辑；对应设计文档 §5.2。
/// </summary>
[GlobalClass]
public partial class MeleeData : Resource
{
	[ExportGroup("伤害")]
	/// <summary>命中一次的基础伤害（未乘部位倍率）。</summary>
	[Export] public float Damage { get; set; } = 55f;
	/// <summary>只有正前方该角度内的目标会被打中（度）。</summary>
	[Export] public float ConeDegrees { get; set; } = 70f;

	[ExportGroup("判定")]
	/// <summary>有效距离（米）。超出该距离不结算。</summary>
	[Export] public float Range { get; set; } = 2.1f;
	/// <summary>判定球的半径（米），给一点容错，不必像素级对准。</summary>
	[Export] public float HitRadius { get; set; } = 0.55f;

	[ExportGroup("节奏")]
	/// <summary>两次挥击的最小间隔（秒）。</summary>
	[Export] public float Cooldown { get; set; } = 0.75f;
	/// <summary>一次挥击的总时长（秒）。</summary>
	[Export] public float SwingTime { get; set; } = 0.32f;
	/// <summary>挥动到多少比例时结算伤害（0~1）。</summary>
	[Export] public float HitTimeRatio { get; set; } = 0.4f;

	[ExportGroup("潜行暗杀（§5.3）")]
	/// <summary>背面暗杀的最大距离（米）。</summary>
	[Export] public float TakedownRange { get; set; } = 2f;
	/// <summary>玩家必须位于敌人背后：敌人朝向与「敌→玩家」方向的点积小于该值。</summary>
	[Export] public float TakedownRearDot { get; set; } = -0.25f;
	/// <summary>暗杀后的冷却（秒）。</summary>
	[Export] public float TakedownCooldown { get; set; } = 1.1f;
}
