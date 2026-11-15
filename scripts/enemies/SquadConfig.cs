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
	/// <summary>只剩方向时，队友朝该方向摸出的距离（不隔墙锁人）。</summary>
	[Export] public float ShareDirectionProbeDistance { get; set; } = 6f;
	/// <summary>只听见动静（枪声 / 脚步）时的初始置信度，低于目视。</summary>
	[Export] public float ShareNoiseConfidence { get; set; } = 0.55f;

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
	/// <summary>
	/// 接敌试探期：首次交火后的这段时间内只发放 <see cref="EngageRampTokens"/> 个令牌，
	/// 给玩家"被看见 → 找掩体"的反应窗口；之后恢复 <see cref="FireTokens"/>。0 表示关闭。
	/// </summary>
	[Export] public float EngageRampDuration { get; set; } = 6f;
	[Export] public int EngageRampTokens { get; set; } = 1;
	/// <summary>试探期内的交接间隔更长：即使轮换开火，也保证开场有可见的"停顿—反应"窗口。</summary>
	[Export] public float EngageRampHandoverGap { get; set; } = 1.5f;
	/// <summary>
	/// 试探期的散布倍率：开场敌人还在校准射界，命中率更低，避免"第一轮点射就打死人"。
	/// 只在接敌前 <see cref="EngageRampDuration"/> 秒生效，不改变常规散布数值。
	/// </summary>
	[Export] public float EngageRampSpreadScale { get; set; } = 1.5f;

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
	/// <summary>被压制暗角与掠弹音冷却（§2.7 表的 3s）。</summary>
	[Export] public float SuppressCalloutCooldown { get; set; } = 3f;
}
