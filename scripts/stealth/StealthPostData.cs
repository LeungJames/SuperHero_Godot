using Godot;

/// <summary>哨位分类（2.0 设计 §3.3）。</summary>
public enum StealthPostRole
{
	/// <summary>可绕开：固定巡逻、视野窄。</summary>
	Avoidable,
	/// <summary>必须处理：卡住支路关键节点。</summary>
	MustHandle,
	/// <summary>队长：携带无线电，死亡会短暂提升全队警戒。</summary>
	Captain,
}

/// <summary>
/// 单个哨位的归类数据（2.0 设计 §3.3）。只做标记，不复制敌人；
/// 用现有关卡里的敌人节点名引用，不改敌人数量与摆位。
/// </summary>
[GlobalClass]
public partial class StealthPostData : Resource
{
	[Export] public string EnemyName { get; set; } = string.Empty;
	[Export] public StealthPostRole Role { get; set; } = StealthPostRole.Avoidable;
	[Export] public string Note { get; set; } = string.Empty;
}
