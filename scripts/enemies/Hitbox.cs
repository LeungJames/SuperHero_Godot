using Godot;

/// <summary>
/// 部位命中盒：按倍率放大伤害，再沿父链交给 IDamageable。
/// 只负责部位倍率与转发，不管生命值（设计文档 §9.3、§9.4）。
/// </summary>
public partial class Hitbox : Area3D
{
	/// <summary>头部 2.5 / 躯干 1.0 / 四肢 0.75（设计文档 §4.2）。</summary>
	[Export] public float DamageMultiplier { get; set; } = 1f;

	[Export] public bool IsHead { get; set; }

	/// <summary>沿父链找最近的 IDamageable（范围伤害需要按实体去重时用）。</summary>
	public IDamageable FindDamageable()
	{
		for (Node node = GetParent(); node != null; node = node.GetParent())
			if (node is IDamageable damageable)
				return damageable;

		return null;
	}

	/// <summary>传入未乘部位倍率的伤害，由本节点完成取整后转发。</summary>
	public void ApplyDamage(float damageBeforeMultiplier, Vector3 hitPoint, Vector3 normal)
	{
		float damage = Mathf.Round(damageBeforeMultiplier * DamageMultiplier);
		if (damage <= 0f)
			return;

		var info = new DamageInfo(damage, hitPoint, normal, IsHead);

		IDamageable target = FindDamageable();
		if (target == null)
		{
			GD.PushWarning($"Hitbox '{Name}': 父链上找不到 IDamageable。");
			return;
		}

		target.TakeDamage(info);
	}
}
