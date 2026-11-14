using Godot;

/// <summary>一次 hitscan 的结果，供表现层（弹道 / 命中特效）使用。</summary>
public readonly struct HitscanHit
{
	public readonly bool HasHit;
	public readonly Vector3 Point;
	public readonly Vector3 Normal;
	public readonly float Distance;
	public readonly Node Collider;

	public static readonly HitscanHit Miss = new(false, Vector3.Zero, Vector3.Zero, 0f, null);

	public HitscanHit(bool hasHit, Vector3 point, Vector3 normal, float distance, Node collider)
	{
		HasHit = hasHit;
		Point = point;
		Normal = normal;
		Distance = distance;
		Collider = collider;
	}
}

/// <summary>
/// 纯 hitscan 解算：射线、距离衰减、部位伤害转发。不持有任何武器状态（设计文档 §4.3、§4.4、§9.3）。
/// </summary>
public static class HitscanResolver
{
	/// <summary>玩家子弹只打世界几何与敌人命中盒（层 1 | 4，对应掩码 1 | 8）。</summary>
	private const uint PlayerBulletMask = 1u | 8u;

	public static HitscanHit Resolve(World3D world, Vector3 from, Vector3 direction, WeaponData data,
		Godot.Collections.Array<Rid> exclude)
	{
		var query = PhysicsRayQueryParameters3D.Create(from, from + direction * data.MaxRange, PlayerBulletMask);
		query.CollideWithAreas = true;
		query.Exclude = exclude;

		var result = world.DirectSpaceState.IntersectRay(query);
		if (result.Count == 0)
			return HitscanHit.Miss;

		Vector3 point = result["position"].AsVector3();
		Vector3 normal = result["normal"].AsVector3();
		float distance = from.DistanceTo(point);
		Node collider = result["collider"].As<Node>();

		if (collider is Hitbox hitbox)
			hitbox.ApplyDamage(CalculateDamage(distance, data), point, normal);

		return new HitscanHit(true, point, normal, distance, collider);
	}

	/// <summary>§4.3：25m 内满伤害，50m 外 ×0.6，中间线性插值。</summary>
	public static float CalculateDamage(float distance, WeaponData data)
	{
		float span = data.MinDamageDistance - data.FullDamageDistance;
		float t = span > 0f
			? Mathf.Clamp((distance - data.FullDamageDistance) / span, 0f, 1f)
			: 1f;

		return data.BaseDamage * Mathf.Lerp(1f, data.MinDamageMultiplier, t);
	}

	/// <summary>在散布圆锥内随机偏转方向（圆盘均匀采样），玩家与敌人共用。</summary>
	public static Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
	{
		if (spreadDegrees <= 0f)
			return direction;

		float radius = Mathf.Tan(Mathf.DegToRad(spreadDegrees)) * Mathf.Sqrt((float)GD.Randf());
		float angle = (float)GD.Randf() * Mathf.Tau;

		Vector3 reference = Mathf.Abs(direction.Y) > 0.99f ? Vector3.Right : Vector3.Up;
		Vector3 right = direction.Cross(reference).Normalized();
		Vector3 up = right.Cross(direction).Normalized();

		return (direction + right * (Mathf.Cos(angle) * radius) + up * (Mathf.Sin(angle) * radius)).Normalized();
	}
}
