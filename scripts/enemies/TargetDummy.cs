using Godot;

/// <summary>
/// 静态射击靶（M2 验证用）：100 血、分头 / 躯干 / 腿三段命中盒，死亡后隐藏并关闭碰撞。
/// 正式敌人由 M3 的 EnemyAgent + EnemyHealth 取代。
/// </summary>
public partial class TargetDummy : StaticBody3D, IDamageable
{
	[Export] public float MaxHealth { get; set; } = 100f;

	public float Health { get; private set; }

	public override void _Ready()
	{
		Health = MaxHealth;
	}

	public void TakeDamage(in DamageInfo info)
	{
		if (Health <= 0f)
			return;

		Health = Mathf.Max(0f, Health - info.Damage);
		GD.Print($"[TargetDummy] {Name} 受到 {info.Damage:F0} 伤害（{(info.IsHeadshot ? "爆头" : "身体")}），剩余 {Health:F0}");

		if (Health <= 0f)
			Die();
	}

	private void Die()
	{
		GD.Print($"[TargetDummy] {Name} 被击倒。");

		SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0u);

		foreach (Node child in GetChildren())
		{
			if (child is Node3D node3D)
				node3D.SetDeferred(Node3D.PropertyName.Visible, false);
		}
	}
}
