using Godot;

/// <summary>
/// 爆炸桶数值：击穿后的引信、半径与伤害。对应设计文档 §5.3 环境击杀。
/// </summary>
[GlobalClass]
public partial class ExplosiveData : Resource
{
	/// <summary>被打中到爆炸的延迟（秒），给玩家反应时间。</summary>
	[Export] public float FuseTime { get; set; } = 0.35f;
	/// <summary>爆炸半径（米）。</summary>
	[Export] public float Radius { get; set; } = 4.2f;
	/// <summary>中心伤害，边缘按距离衰减到一半。</summary>
	[Export] public float Damage { get; set; } = 120f;
	/// <summary>爆炸特效场景（留空则只闪光）。</summary>
	[Export] public PackedScene BurstScene { get; set; }
}
