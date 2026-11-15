using Godot;

/// <summary>小队槽位。Combat 下的任务分工，不是新的顶层状态（2.0 设计 §2.3、§2.4）。</summary>
public enum SquadSlot
{
	Suppress,
	Flank,
	Support,
	Retreat,
}

/// <summary>
/// 小队协同调参（2.0 设计 §2.8）。只放数值，不含逻辑；
/// 每关一份 .tres，挂在 <see cref="SquadRoster"/> 上。
/// </summary>
[GlobalClass]
public partial class SquadConfig : Resource
{
	[ExportGroup("编制")]
	[Export] public int MaxSize { get; set; } = 3;
	[Export] public int FlankMax { get; set; } = 1;

	[ExportGroup("槽位距离")]
	[Export] public float SuppressMinRange { get; set; } = 10f;
	[Export] public float SuppressMaxRange { get; set; } = 18f;
	[Export] public float FlankMinRange { get; set; } = 4f;
	[Export] public float FlankMaxRange { get; set; } = 12f;
	[Export] public float SupportMinRange { get; set; } = 3f;
	[Export] public float SupportMaxRange { get; set; } = 8f;

	[ExportGroup("后撤")]
	[Export] public float RetreatHealthRatio { get; set; } = 0.35f;
	[Export] public float RetreatDuration { get; set; } = 4f;
	[Export] public float RetreatCooldown { get; set; } = 20f;
	/// <summary>后撤找掩体的搜索半径；半径内没有可断视线的点就死战。</summary>
	[Export] public float RetreatCoverRadius { get; set; } = 8f;

	[ExportGroup("共享感知")]
	[Export] public float ShareDelay { get; set; } = 0.8f;
	/// <summary>每秒置信度衰减；低于 0.3 时只保留大致方向（§2.5）。</summary>
	[Export] public float ShareConfidenceDecay { get; set; } = 0.5f;
	[Export] public float ShareDirectionOnlyConfidence { get; set; } = 0.3f;

	[ExportGroup("节奏")]
	[Export] public float SlotReviewInterval { get; set; } = 3f;
	/// <summary>槽位切换后的决策停顿，避免每帧抖动。</summary>
	[Export] public float SlotSwitchPause { get; set; } = 0.5f;
	[Export] public float FlankAbortLookTime { get; set; } = 1.5f;
	/// <summary>同一槽位任务持续这么久没有进展就强制重评（§2.9 超时兜底）。</summary>
	[Export] public float SlotStuckTimeout { get; set; } = 12f;

	[ExportGroup("开火令牌（§2.6）")]
	/// <summary>同屏同时开火人数（普通难度 2）。</summary>
	[Export] public int FireTokens { get; set; } = 2;
	[Export] public float TokenHoldTime { get; set; } = 2.5f;
	/// <summary>令牌连续开火后的强制交接间隔，制造点射间隙而不是整齐齐射。</summary>
	[Export] public float TokenHandoverGap { get; set; } = 0.35f;

	[ExportGroup("压力曲线（§2.6 备注）")]
	/// <summary>压力波间隔；到点临时多给 1 个令牌，持续 WaveDuration，制造"压上来再松口气"。</summary>
	[Export] public float PressureWaveInterval { get; set; } = 9f;
	[Export] public float PressureWaveDuration { get; set; } = 2.5f;
	/// <summary>玩家最近这么久没有受伤才会触发压力波（说明玩家已获得喘息）。</summary>
	[Export] public float PressureWavePlayerCalmTime { get; set; } = 3.5f;

	[ExportGroup("提示（§2.7）")]
	[Export] public float FlankCalloutCooldown { get; set; } = 8f;
	[Export] public float RetreatCalloutCooldown { get; set; } = 5f;
	[Export] public float HelpCalloutCooldown { get; set; } = 15f;
}
