using Godot;

/// <summary>
/// 爆炸桶：被子弹或近战击穿后延时爆炸，对半径内的敌人与玩家结算伤害（设计文档 §5.3）。
/// 未爆炸时就是普通静态几何，不参与任何逐帧逻辑。
/// </summary>
public partial class ExplosiveBarrel : StaticBody3D, IBulletReceiver, IDamageable
{
	[Export] public ExplosiveData Data { get; set; }

	private bool _armed;
	private bool _exploded;
	private float _fuse;

	public override void _Ready()
	{
		SetProcess(false);
	}

	public void OnBulletHit(in HitscanHit hit)
	{
		Arm();
	}

	public void TakeDamage(in DamageInfo info)
	{
		if (info.Damage > 0f)
			Arm();
	}

	private void Arm()
	{
		if (_armed || _exploded)
			return;

		_armed = true;
		_fuse = Data != null ? Data.FuseTime : 0.35f;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!_armed || _exploded)
			return;

		_fuse -= (float)delta;
		if (_fuse <= 0f)
			Explode();
	}

	private void Explode()
	{
		_exploded = true;
		SetProcess(false);

		ExplosiveData data = Data ?? new ExplosiveData();
		Vector3 center = GlobalPosition + Vector3.Up * 0.45f;

		DamageRadius(center, data);
		SpawnBurst(center, data);
		NoiseEmitter.Emit(center, NoiseKind.Gunshot);

		// 桶本身消失，碰撞一并关掉
		CollisionLayer = 0;
		Visible = false;
		foreach (Node child in GetChildren())
			if (child is Node3D visual)
				visual.Visible = false;
	}

	private void DamageRadius(Vector3 center, ExplosiveData data)
	{
		var sphere = new SphereShape3D { Radius = data.Radius };
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = sphere,
			Transform = new Transform3D(Basis.Identity, center),
			CollisionMask = 2u | 8u,
			CollideWithAreas = true,
		};

		// 同一个敌人有多个命中盒，按实体去重，只结算一次
		var targets = new System.Collections.Generic.Dictionary<IDamageable, (Hitbox Box, float Distance)>();

		foreach (Godot.Collections.Dictionary hit in GetWorld3D().DirectSpaceState.IntersectShape(query, 32))
		{
			if (hit["collider"].As<GodotObject>() is not Hitbox hitbox)
				continue;

			IDamageable target = hitbox.FindDamageable();
			if (target == null)
				continue;

			float distance = hitbox.GlobalPosition.DistanceTo(center);
			if (!targets.TryGetValue(target, out (Hitbox Box, float Distance) best) || distance < best.Distance)
				targets[target] = (hitbox, distance);
		}

		foreach (System.Collections.Generic.KeyValuePair<IDamageable, (Hitbox Box, float Distance)> entry in targets)
		{
			float falloff = Mathf.Lerp(1f, 0.5f, Mathf.Clamp(entry.Value.Distance / data.Radius, 0f, 1f));
			entry.Value.Box.ApplyDamage(data.Damage * falloff, entry.Value.Box.GlobalPosition, Vector3.Up);
		}

		if (GetTree().GetFirstNodeInGroup("player") is PlayerController player)
		{
			float distance = player.GlobalPosition.DistanceTo(center);
			if (distance <= data.Radius)
			{
				float falloff = Mathf.Lerp(1f, 0.4f, Mathf.Clamp(distance / data.Radius, 0f, 1f));
				player.GetNodeOrNull<PlayerHealth>("Health")?.TakeDamage(data.Damage * falloff * 0.5f, DeathCause.Explosion);
			}
		}
	}

	private void SpawnBurst(Vector3 center, ExplosiveData data)
	{
		if (data.BurstScene != null)
		{
			Node burst = data.BurstScene.Instantiate();
			GetParent().AddChild(burst);
			if (burst is Node3D node)
				node.GlobalPosition = center;
		}

		var light = new OmniLight3D
		{
			LightColor = new Color(1f, 0.72f, 0.35f),
			LightEnergy = 6f,
			OmniRange = data.Radius * 2.5f,
			ShadowEnabled = false,
		};
		GetParent().AddChild(light);
		light.GlobalPosition = center;

		SceneTree tree = GetTree();
		light.CreateTween().TweenProperty(light, "light_energy", 0f, 0.35f);
		tree.CreateTimer(0.4f).Timeout += light.QueueFree;
	}
}
