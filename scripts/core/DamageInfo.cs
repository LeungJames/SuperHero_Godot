using Godot;

/// <summary>
/// 一次伤害的完整描述。readonly struct 避免 GC（设计文档 §9.4）。
/// </summary>
public readonly struct DamageInfo
{
	public readonly float Damage;
	public readonly Vector3 HitPoint;
	public readonly Vector3 Normal;
	public readonly bool IsHeadshot;

	public DamageInfo(float damage, Vector3 hitPoint, Vector3 normal, bool isHeadshot)
	{
		Damage = damage;
		HitPoint = hitPoint;
		Normal = normal;
		IsHeadshot = isHeadshot;
	}
}

/// <summary>
/// 可受伤实体。命中盒沿父链找到该接口后转发伤害。
/// </summary>
public interface IDamageable
{
	void TakeDamage(in DamageInfo info);
}
