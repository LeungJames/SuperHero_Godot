using Godot;

/// <summary>
/// 潜行警戒与感知调参（2.0 设计 §3.4–§3.6）。只放数值与文案，不含逻辑；
/// 三关共用一份 <c>data/stealth/alert_config.tres</c>，由 StealthDirector 在关卡开始时写入 StealthSystem。
/// </summary>
[GlobalClass]
public partial class StealthConfig : Resource
{
	[ExportGroup("警戒等级（§3.4）")]
	/// <summary>L0 局部警戒持续时间（秒）。</summary>
	[Export] public float L0Duration { get; set; } = 8f;
	/// <summary>L1 搜索持续时间（秒）。</summary>
	[Export] public float L1Duration { get; set; } = 30f;
	/// <summary>L2 升级增援持续时间（秒）。</summary>
	[Export] public float L2Duration { get; set; } = 90f;
	/// <summary>玩家完全脱离视野并静止多久后降低一级（秒）。</summary>
	[Export] public float DeescalateStationaryTime { get; set; } = 10f;
	/// <summary>判定"静止"的水平速度上限（m/s）。</summary>
	[Export] public float DeescalateSpeedThreshold { get; set; } = 0.3f;

	[ExportGroup("噪音半径（§3.4、§5.4）")]
	/// <summary>静步（walk_slow）不发声。</summary>
	[Export] public float QuietNoiseRadius { get; set; }
	[Export] public float WalkNoiseRadius { get; set; } = 8f;
	[Export] public float SprintNoiseRadius { get; set; } = 14f;
	[Export] public float GunshotNoiseRadius { get; set; } = 30f;

	[ExportGroup("尸体与痕迹（§3.5）")]
	/// <summary>敌人发现尸体的距离（米）。</summary>
	[Export] public float CorpseVisibleRadius { get; set; } = 4f;
	/// <summary>发现尸体后的迟疑时间（秒），到时才提升警戒。</summary>
	[Export] public float CorpseNoticeDelay { get; set; } = 3f;
	/// <summary>藏尸耗时（秒）。</summary>
	[Export] public float CorpseHideTime { get; set; } = 1.5f;
	/// <summary>藏尸期间的移动速度上限（m/s）。</summary>
	[Export] public float CorpseDragSpeed { get; set; } = 1.5f;
	/// <summary>可以开始藏尸的交互距离（米）。</summary>
	[Export] public float CorpseInteractRadius { get; set; } = 2.5f;

	[ExportGroup("可读反馈（§3.6）")]
	/// <summary>敌人头顶 ?/! 的显示时长（秒）。</summary>
	[Export] public float IconDuration { get; set; } = 1.2f;
	/// <summary>L0 可疑点周围参与搜索的距离（米）。</summary>
	[Export] public float SuspiciousSearchRadius { get; set; } = 15f;

	[ExportGroup("增援")]
	/// <summary>L1 搜索时增援人数。</summary>
	[Export] public int L1ReinforceCount { get; set; } = 2;
	/// <summary>L2 增援波人数；0 表示沿用 AlertData.WaveSize。</summary>
	[Export] public int L2WaveSize { get; set; }

	[ExportGroup("文案（字幕 + 程序化无线电）")]
	[Export] public string L0Message { get; set; } = "无线电：好像有动静。";
	[Export] public string L1Message { get; set; } = "无线电：有人闯进来了，搜！";
	[Export] public string L2Message { get; set; } = "无线电：全叫起来，把这片翻一遍！";
	[Export] public string CorpseFoundMessage { get; set; } = "无线电：有尸体！";
	[Export] public string CaptainDeathMessage { get; set; } = "无线电：队长倒下了！";
	[Export] public string HidePrompt { get; set; } = "按 F 藏尸";
	[Export] public string HideDoneMessage { get; set; } = "尸体已藏好";
}
