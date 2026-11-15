using Godot;
using System.Collections.Generic;

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
/// W5 穿甲弹只穿透标记了 <c>penetrable</c> 组的薄掩体，并允许再命中 1 名敌人。
/// </summary>
public static class HitscanResolver
{
	/// <summary>玩家子弹只打世界几何与敌人命中盒（层 1 | 4，对应掩码 1 | 8）。</summary>
	private const uint PlayerBulletMask = 1u | 8u;

	/// <summary>穿甲弹只认这个组：栏杆 / 木板 / 铁皮等薄掩体，不改几何与物理层。</summary>
	public const string PenetrableGroup = "penetrable";

	private const int MaxEnemyHits = 2;
	private const int MaxPenetrableCovers = 1;

	public static HitscanHit Resolve(World3D world, Vector3 from, Vector3 direction, WeaponData data,
		Godot.Collections.Array<Rid> exclude, float damageScale = 1f)
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
			hitbox.ApplyDamage(CalculateDamage(distance, data) * Mathf.Max(damageScale, 0f), point, normal);

		return new HitscanHit(true, point, normal, distance, collider);
	}

	/// <summary>
	/// W5 穿甲解算：穿过 1 层 <c>penetrable</c> 薄掩体、最多命中 2 名敌人；
	/// 穿透后的命中伤害乘以 <see cref="TalentEffects.PierceDamageScale"/>。
	/// 返回按命中顺序排列的段列表；第一个命中可能是掩体，调用方按需挑首个敌人做统计。
	/// </summary>
	public static List<HitscanHit> ResolvePiercing(World3D world, Vector3 from, Vector3 direction,
		WeaponData data, Godot.Collections.Array<Rid> exclude, float firstHitScale = 1f)
	{
		var hits = new List<HitscanHit>();
		var damaged = new HashSet<IDamageable>();

		Vector3 origin = from;
		float travelled = 0f;
		float scale = Mathf.Max(firstHitScale, 0f);
		int covers = 0;
		int enemies = 0;

		for (int segment = 0; segment < MaxEnemyHits + MaxPenetrableCovers + 1; segment++)
		{
			float remaining = data.MaxRange - travelled;
			if (remaining <= 0.01f)
				break;

			var query = PhysicsRayQueryParameters3D.Create(
				origin, origin + direction * remaining, PlayerBulletMask);
			query.CollideWithAreas = true;
			query.Exclude = exclude;

			var result = world.DirectSpaceState.IntersectRay(query);
			if (result.Count == 0)
				break;

			Vector3 point = result["position"].AsVector3();
			Vector3 normal = result["normal"].AsVector3();
			float segmentDistance = origin.DistanceTo(point);
			float distance = travelled + segmentDistance;
			Node collider = result["collider"].As<Node>();

			if (collider is Hitbox hitbox)
			{
				IDamageable target = hitbox.FindDamageable();

				// 同一名敌人的其它命中盒不重复结算，继续往后找下一名敌人
				if (target != null && damaged.Contains(target))
				{
					travelled = distance + 0.02f;
					origin = point + direction * 0.02f;
					continue;
				}

				if (target != null)
					damaged.Add(target);

				hits.Add(new HitscanHit(true, point, normal, distance, collider));
				hitbox.ApplyDamage(CalculateDamage(distance, data) * scale, point, normal);
				enemies++;

				if (enemies >= MaxEnemyHits)
					break;

				scale *= TalentEffects.PierceDamageScale;
				travelled = distance + 0.02f;
				origin = point + direction * 0.02f;
				continue;
			}

			if (IsPenetrable(collider) && covers < MaxPenetrableCovers)
			{
				covers++;
				scale *= TalentEffects.PierceDamageScale;
				travelled = distance + 0.02f;
				origin = point + direction * 0.02f;
				continue;
			}

			// 实体墙 / 地面：作为弹道终点记录，不再继续
			hits.Add(new HitscanHit(true, point, normal, distance, collider));
			break;
		}

		return hits;
	}

	/// <summary>碰撞体自身或近亲节点带 <c>penetrable</c> 组即视为可穿透薄掩体。</summary>
	public static bool IsPenetrable(Node node)
	{
		Node current = node;
		for (int depth = 0; current != null && depth < 3; depth++, current = current.GetParent())
		{
			if (current.IsInGroup(PenetrableGroup))
				return true;
		}

		return false;
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
