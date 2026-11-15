using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 近战（枪托 / 刀）：短距胶囊判定 + 第一人称挥动（设计文档 §5.2）。
/// 只负责判定与节奏，数值在 MeleeData；命中走 Hitbox，与子弹共用部位倍率。
/// A7 激光剑气也在这里释放：长按近战键 0.4s 或双击近战键（§5.2.1）。
/// </summary>
public partial class MeleeController : Node3D
{
	[Export] public MeleeData Data { get; set; }

	/// <summary>开始挥击（音效用）。</summary>
	public event Action Swung;

	/// <summary>结算结果：命中点（未命中为零向量）。</summary>
	public event Action<Vector3, bool> Resolved;

	/// <summary>A7：本关是否允许释放剑气（已点 A7、已通关第二章、第三章起）。</summary>
	public bool LaserAvailable => _game != null && TalentEffects.LaserUsable(_game.ChapterId);

	/// <summary>剑气是否已就绪（章节允许且 CD 已转好）。</summary>
	public bool LaserReady => LaserAvailable && _laserCooldown <= 0f;

	private const float HoldThreshold = 0.4f;
	private const float DoubleTapWindow = 0.3f;
	private const uint WorldMask = 1u;
	private const uint HitboxMask = 8u;

	private static readonly PackedScene LaserScene = GD.Load<PackedScene>("res://scenes/fx/LaserSlash.tscn");
	private static readonly PackedScene ImpactScene = GD.Load<PackedScene>("res://scenes/fx/Impact.tscn");

	private PlayerController _player;
	private PlayerHealth _health;
	private Camera3D _camera;
	private WeaponController _weapon;
	private GameManager _game;
	private readonly Godot.Collections.Array<Rid> _exclude = new();

	private float _cooldown;
	private float _swingTimer;
	private bool _resolved;

	private float _laserCooldown;
	private bool _holding;
	private float _holdTimer;
	private float _lastPressTime = -10f;
	private bool _laserPending;

	private bool CanAct => _player != null && _player.InputEnabled && _health != null && !_health.IsDead;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_health = _player.GetNodeOrNull<PlayerHealth>("Health");
		_camera = _player.GetNodeOrNull<Camera3D>("Head/Camera3D");
		_weapon = _player.GetNodeOrNull<WeaponController>("Head/Camera3D/ViewModel/Weapon");
		_game = GetNodeOrNull<GameManager>("../../../Managers/GameManager");
		_exclude.Add(_player.GetRid());
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("melee"))
			OnMeleePressed();
		else if (@event.IsActionReleased("melee"))
			OnMeleeReleased();
	}

	/// <summary>没有剑气时保持按下即挥；有剑气时按下只开始蓄力，避免长按和普通挥砍抢输入。</summary>
	private void OnMeleePressed()
	{
		if (!LaserAvailable)
		{
			TrySwing();
			return;
		}

		float now = Time.GetTicksMsec() / 1000f;
		if (now - _lastPressTime <= DoubleTapWindow)
		{
			_lastPressTime = -10f;
			if (!TryLaser())
				TrySwing();
			return;
		}

		_lastPressTime = now;
		_holding = true;
		_holdTimer = 0f;
	}

	private void OnMeleeReleased()
	{
		if (!_holding)
			return;

		_holding = false;
		bool shortPress = _holdTimer < HoldThreshold;
		_holdTimer = 0f;

		// 长按已在 0.4s 时放出剑气，抬手不再补一次普通挥砍
		if (shortPress)
			TrySwing();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_cooldown = Mathf.Max(0f, _cooldown - dt);
		_laserCooldown = Mathf.Max(0f, _laserCooldown - dt);

		if (_holding)
		{
			_holdTimer += dt;
			if (_holdTimer >= HoldThreshold)
			{
				_holding = false;
				TryLaser();
			}
		}

		if (_swingTimer <= 0f)
			return;

		_swingTimer -= dt;
		float progress = Mathf.Clamp(1f - _swingTimer / Mathf.Max(Data.SwingTime, 0.01f), 0f, 1f);
		if (_weapon != null)
			_weapon.MeleeSwing = SwingPose(progress);

		if (_laserPending && progress >= Data.HitTimeRatio)
		{
			_laserPending = false;
			FireLaser();
		}

		if (!_resolved && progress >= Data.HitTimeRatio)
		{
			_resolved = true;
			ResolveHit();
		}
	}

	/// <summary>A7 剑气：沿用挥砍动作，不吃 A2 的间隔加成，也不走普通近战判定。</summary>
	private bool TryLaser()
	{
		if (!CanAct || Data == null || !LaserAvailable || _laserCooldown > 0f || _swingTimer > 0f)
			return false;

		_laserCooldown = TalentEffects.Config.LaserCooldown;
		_cooldown = Mathf.Max(_cooldown, Data.Cooldown);
		_swingTimer = Data.SwingTime;
		_resolved = true;
		_laserPending = true;

		Swung?.Invoke();
		return true;
	}

	public bool TrySwing()
	{
		if (!CanAct || Data == null || _cooldown > 0f || _swingTimer > 0f)
			return false;

		_cooldown = Data.Cooldown * TalentEffects.SwingIntervalScale;
		_swingTimer = Data.SwingTime;
		Swung?.Invoke();

		// 背后且未警觉的敌人直接暗杀：静默、不结算普通近战
		_resolved = TryTakedown();
		if (!_resolved)
			NoiseEmitter.Emit(_player.GlobalPosition, NoiseKind.FootstepWalk);

		return true;
	}

	/// <summary>找背后最近的未警觉敌人；成功则直接击杀并返回 true。</summary>
	private bool TryTakedown()
	{
		if (_camera == null)
			return false;

		Vector3 origin = _camera.GlobalPosition;
		Vector3 forward = -_camera.GlobalTransform.Basis.Z;

		EnemyHealth target = null;
		float bestDistance = float.MaxValue;

		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyHealth health || health.IsDead)
				continue;
			if (health.GetParent() is not EnemyAgent agent)
				continue;
			if (agent.State is EnemyState.Alert or EnemyState.Combat)
				continue;

			Vector3 toPlayer = Flat(_player.GlobalPosition - agent.GlobalPosition);
			if (toPlayer.LengthSquared() < 0.0001f)
				continue;
			toPlayer = toPlayer.Normalized();

			Vector3 enemyForward = Flat(-agent.GlobalTransform.Basis.Z);
			if (enemyForward.LengthSquared() < 0.0001f)
				continue;
			enemyForward = enemyForward.Normalized();

			// 玩家必须在敌人背后（点积越负越正后方）
			if (enemyForward.Dot(toPlayer) > Data.TakedownRearDot)
				continue;

			Vector3 toEnemy = agent.GlobalPosition + Vector3.Up - origin;
			float distance = toEnemy.Length();
			if (distance > Data.TakedownRange)
				continue;
			if (Mathf.RadToDeg(forward.AngleTo(toEnemy.Normalized())) > Data.ConeDegrees * 0.5f)
				continue;

			if (distance < bestDistance)
			{
				target = health;
				bestDistance = distance;
			}
		}

		if (target == null)
			return false;

		_cooldown = Mathf.Max(_cooldown, Data.TakedownCooldown);
		Vector3 point = (target.GetParent() as Node3D)?.GlobalPosition ?? _player.GlobalPosition;
		target.TakeDamage(new DamageInfo(9999f, point, -forward, true));

		// 暗杀留痕：目标本来就是未警觉状态（TryTakedown 已过滤 Alert/Combat）
		(GetTree().GetFirstNodeInGroup("stealth_director") as StealthDirector)?.NotifyExecution(target, true);
		return true;
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

	/// <summary>挥动姿势：前 35% 抡出去，后面收回。</summary>
	private static float SwingPose(float progress)
	{
		return progress < 0.35f
			? Mathf.SmoothStep(0f, 1f, progress / 0.35f)
			: Mathf.SmoothStep(1f, 0f, (progress - 0.35f) / 0.65f);
	}

	/// <summary>沿视线 25m、宽 1.2m 的直线剑气：穿透所有敌人，被墙体截断（§5.2.1）。</summary>
	private void FireLaser()
	{
		if (_camera == null || _player == null)
			return;

		float length = TalentEffects.Config.LaserRange;
		float width = TalentEffects.Config.LaserWidth;
		Vector3 origin = _camera.GlobalPosition;
		Vector3 forward = -_camera.GlobalTransform.Basis.Z;

		// 墙体截断：剑气不做穿墙击杀
		PhysicsRayQueryParameters3D wallQuery =
			PhysicsRayQueryParameters3D.Create(origin, origin + forward * length, WorldMask);
		wallQuery.Exclude = _exclude;
		Godot.Collections.Dictionary wall = _player.GetWorld3D().DirectSpaceState.IntersectRay(wallQuery);
		float allowed = wall.Count > 0 ? origin.DistanceTo(wall["position"].AsVector3()) : length;

		var box = new BoxShape3D { Size = new Vector3(width, length, width) };
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = box,
			Transform = new Transform3D(
				new Basis(new Quaternion(Vector3.Up, forward)),
				origin + forward * (length * 0.5f)),
			CollisionMask = HitboxMask,
			CollideWithAreas = true,
		};
		query.Exclude = _exclude;

		Godot.Collections.Array<Godot.Collections.Dictionary> hits =
			_player.GetWorld3D().DirectSpaceState.IntersectShape(query, 32);

		float damage = TalentEffects.Config.LaserDamage * TalentEffects.MeleeDamageScale;
		// 枪托释放 ×0.8；刀不衰减（§5.2.1）
		if (_weapon == null || !_weapon.IsKnifeActive)
			damage *= TalentEffects.Config.LaserStockScale;

		// 同一个敌人只吃一次剑气：多个部位盒重叠时取倍率最高的那个
		var targets = new Dictionary<IDamageable, Hitbox>();

		foreach (Godot.Collections.Dictionary hit in hits)
		{
			if (hit["collider"].As<GodotObject>() is not Hitbox hitbox)
				continue;

			Vector3 toPoint = hitbox.GlobalPosition - origin;
			float along = toPoint.Dot(forward);
			if (along < 0f || along > allowed)
				continue;

			float lateral = Mathf.Sqrt(Mathf.Max(0f, toPoint.LengthSquared() - along * along));
			if (lateral > width * 0.5f)
				continue;

			IDamageable target = hitbox.FindDamageable();
			if (target == null)
				continue;

			if (!targets.TryGetValue(target, out Hitbox existing) || hitbox.DamageMultiplier > existing.DamageMultiplier)
				targets[target] = hitbox;
		}

		foreach (KeyValuePair<IDamageable, Hitbox> pair in targets)
		{
			Vector3 point = pair.Value.GlobalPosition;
			pair.Value.ApplyDamage(damage, point, -forward);
			SpawnImpact(point, origin);
		}

		if (LaserScene?.Instantiate() is LaserSlash slash)
		{
			GetTree().CurrentScene?.AddChild(slash);
			slash.Play(origin, forward, length, width);
		}

		(GetTree().GetFirstNodeInGroup("comic_overlay") as ComicOverlay)
			?.ShowWorld("SHHK", origin + forward * 7f, new Color(1f, 0.86f, 0.4f), 1f);
	}

	private void SpawnImpact(Vector3 point, Vector3 viewOrigin)
	{
		if (ImpactScene?.Instantiate() is not ImpactEffect impact)
			return;

		GetTree().CurrentScene?.AddChild(impact);
		Vector3 normal = viewOrigin - point;
		impact.Play(point, normal.LengthSquared() > 0.0001f ? normal.Normalized() : Vector3.Up);
	}

	/// <summary>沿视线做一次胶囊重叠，取朝向上最近的命中盒；墙体会挡住近战。</summary>
	private void ResolveHit()
	{
		if (_camera == null)
			return;

		Vector3 origin = _camera.GlobalPosition;
		Vector3 forward = -_camera.GlobalTransform.Basis.Z;

		float wallDistance = Data.Range;
		PhysicsRayQueryParameters3D wallQuery =
			PhysicsRayQueryParameters3D.Create(origin, origin + forward * Data.Range, 1u);
		wallQuery.Exclude = _exclude;
		Godot.Collections.Dictionary wall = _player.GetWorld3D().DirectSpaceState.IntersectRay(wallQuery);
		if (wall.Count > 0)
			wallDistance = origin.DistanceTo(wall["position"].AsVector3());

		var capsule = new CapsuleShape3D { Radius = Data.HitRadius, Height = Data.Range };
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = capsule,
			Transform = new Transform3D(
				new Basis(new Quaternion(Vector3.Up, forward)),
				origin + forward * (Data.Range * 0.5f)),
			CollisionMask = 1u | 8u,
			CollideWithAreas = true,
		};
		query.Exclude = _exclude;

		Godot.Collections.Array<Godot.Collections.Dictionary> hits =
			_player.GetWorld3D().DirectSpaceState.IntersectShape(query, 16);

		Hitbox best = null;
		float bestLateral = float.MaxValue;
		float bestDistance = float.MaxValue;

		foreach (Godot.Collections.Dictionary hit in hits)
		{
			if (hit["collider"].As<GodotObject>() is not Hitbox hitbox)
				continue;

			Vector3 point = hitbox.GlobalPosition;
			Vector3 toPoint = point - origin;
			float distance = toPoint.Length();
			if (distance > wallDistance + 0.2f || distance > Data.Range)
				continue;

			if (Mathf.RadToDeg(forward.AngleTo(toPoint.Normalized())) > Data.ConeDegrees * 0.5f)
				continue;

			// 先看离准心有多近（瞄准哪打哪），再看远近
			float lateral = (toPoint - forward * toPoint.Dot(forward)).Length();
			if (lateral < bestLateral - 0.05f || (Mathf.Abs(lateral - bestLateral) <= 0.05f && distance < bestDistance))
			{
				best = hitbox;
				bestLateral = lateral;
				bestDistance = distance;
			}
		}

		if (best == null)
		{
			Resolved?.Invoke(Vector3.Zero, false);
			return;
		}

		Vector3 hitPoint = best.GlobalPosition;
		best.ApplyDamage(Data.Damage * TalentEffects.MeleeDamageScale, hitPoint, -forward);
		Resolved?.Invoke(hitPoint, true);
	}
}
