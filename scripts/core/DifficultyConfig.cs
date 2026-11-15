using Godot;

/// <summary>
/// 难度配置（2.0 设计 §10.5）。只允许调整敌人侧参数：
/// 反应时间、命中散布、同时交战人数、弹药补给。
/// 明确不含玩家生命上限、回血、伤害免疫等字段——零回血是硬约束。
/// </summary>
[GlobalClass]
public partial class DifficultyConfig : Resource
{
	[Export] public string Id { get; set; } = "normal";
	[Export] public string DisplayName { get; set; } = "普通";

	[ExportGroup("敌人")]
	/// <summary>相对 EnemyData.ReactionTime 的偏移（秒）：简单 +，困难 -。</summary>
	[Export] public float ReactionTimeDelta { get; set; }
	/// <summary>命中散布的加减值（度）：简单 +，困难 -。</summary>
	[Export] public float SpreadBonusDegrees { get; set; }
	/// <summary>同屏同时开火人数；0 表示沿用小队配置里的 FireTokens。</summary>
	[Export] public int FireTokens { get; set; } = 2;

	[ExportGroup("补给")]
	/// <summary>敌人掉落弹药包的倍率；不改玩家武器基础备弹。</summary>
	[Export] public float AmmoMultiplier { get; set; } = 1f;
}
