using Godot;
using System;
using System.Collections.Generic;

public enum EnemyState
{
	Patrol,
	Alert,
	Combat,
	Search,
}

/// <summary>
/// 敌人行为：感知（含垂直视野）、Patrol / Alert / Combat / Search 状态机、导航移动、
/// 守卫掩体探头、开火与伤害转发。受伤数值在 EnemyHealth（设计文档 §5.2–§5.4、§9.3）。
/// </summary>
public partial class EnemyAgent : CharacterBody3D, IDamageable
{
	[Export] public EnemyData Data { get; set; }
	[Export] public PackedScene TracerScene { get; set; }
	[Export] public PackedScene ImpactScene { get; set; }

	/// <summary>开出一枪（位置参数为枪口）。音效由 AudioDirector 调度。</summary>
	public event Action<Vector3> Fired;

	/// <summary>首次进入警戒。</summary>
	public event Action Alerted;

	public EnemyState State { get; private set; } = EnemyState.Patrol;
	public bool HasSight => _canSee;

	private const float Gravity = 18f;
	private const float PerceptionInterval = 0.1f;
	private const float MuzzleFlashTime = 0.05f;

	private EnemyHealth _health;
	private NavigationAgent3D _nav;
	private Marker3D _eye;
	private Marker3D _gun;
	private OmniLight3D _muzzleLight;
	private PlayerController _player;
	private PlayerHealth _playerHealth;
	private Godot.Collections.Array<Rid> _exclude;

	private readonly List<Vector3> _patrolPoints = new();
	private readonly List<(Vector3 Cover, Vector3 Peek)> _coverPairs = new();

	private Vector3 _velocity;
	private float _perceptionTimer;
	private bool _canSee;
	private Vector3 _lastKnownPosition;

	private float _stateTimer;
	private float _staggerTimer;
	private float _fireTimer;
	private const float CoverArrivalDistance = 1.0f;
	private int _burstShotsLeft;
	private float _patrolWait;
	private float _patrolWaitTarget;
	private int _patrolIndex;
	private float _strafeTimer;
	private float _strafeDirection = 1f;

	private bool _hasCover;
	private bool _coverPeeking;
	private float _coverPhaseTimer;
	private Vector3 _coverPosition;
	private Vector3 _peekPosition;

	private float _flashTimer;

	public override void _Ready()
	{
		if (Data == null)
		{
			GD.PushError("EnemyAgent: 没有配置 EnemyData。");
			return;
		}

		_health = GetNode<EnemyHealth>("Health");
		_nav = GetNode<NavigationAgent3D>("NavAgent");
		_eye = GetNode<Marker3D>("Eye");
		_gun = GetNodeOrNull<Marker3D>("Gun");
		_muzzleLight = GetNodeOrNull<OmniLight3D>("MuzzleLight");

		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player != null)
			_playerHealth = _player.GetNodeOrNull<PlayerHealth>("Health");

		_exclude = new Godot.Collections.Array<Rid> { GetRid() };
		_lastKnownPosition = GlobalPosition;

		_nav.AvoidanceEnabled = false;

		CollectAnchors();

		_health.Died += OnDied;
		_health.Staggered += OnStaggered;
		NoiseEmitter.Emitted += OnNoise;

		_patrolWaitTarget = RandomWait();
	}

	public override void _ExitTree()
	{
		NoiseEmitter.Emitted -= OnNoise;

		if (_health != null)
		{
			_health.Died -= OnDied;
			_health.Staggered -= OnStaggered;
		}
	}

	public void TakeDamage(in DamageInfo info)
	{
		// 受害数值全部交给 EnemyHealth，这里只是命中盒的转发出口
		_health?.TakeDamage(info);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (Data == null || _health == null || _health.IsDead)
			return;

		// 敌人节点可能比玩家先 _Ready，玩家进树后再补抓一次
		if (!EnsurePlayer())
			return;

		_flashTimer = Mathf.Max(0f, _flashTimer - dt);
		if (_flashTimer <= 0f && _muzzleLight != null && _muzzleLight.Visible)
			_muzzleLight.Visible = false;

		_perceptionTimer -= dt;
		if (_perceptionTimer <= 0f)
		{
			_perceptionTimer = PerceptionInterval;
			UpdatePerception();
		}

		_staggerTimer = Mathf.Max(0f, _staggerTimer - dt);
		if (_staggerTimer > 0f)
		{
			StopHorizontal(dt);
			ApplyGravity(dt);
			Velocity = _velocity;
			MoveAndSlide();
			return;
		}

		switch (State)
		{
			case EnemyState.Patrol:
				UpdatePatrol(dt);
				break;
			case EnemyState.Alert:
				UpdateAlert(dt);
				break;
			case EnemyState.Combat:
				UpdateCombat(dt);
				break;
			case EnemyState.Search:
				UpdateSearch(dt);
				break;
		}

		ApplyGravity(dt);
		Velocity = _velocity;
		MoveAndSlide();
	}

	// ---------------------------------------------------------------- 感知

	private bool EnsurePlayer()
	{
		if (_player != null && _playerHealth != null)
			return true;

		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null)
			return false;

		_playerHealth = _player.GetNodeOrNull<PlayerHealth>("Health");
		return _playerHealth != null;
	}

	private void UpdatePerception()
	{
		if (Data == null || _playerHealth.IsDead)
		{
			_canSee = false;
			return;
		}

		Vector3 eye = _eye.GlobalPosition;
		Vector3 target = _player.GlobalPosition + new Vector3(0f, 1.1f, 0f);
		Vector3 delta = target - eye;
		float distance = delta.Length();

		if (distance > GetEffectiveViewDistance() || distance < 0.01f)
		{
			_canSee = false;
			return;
		}

		Vector3 flat = new(delta.X, 0f, delta.Z);
		if (flat.LengthSquared() > 0.0001f)
		{
			Vector3 forward = -GlobalTransform.Basis.Z;
			forward.Y = 0f;
			forward = forward.Normalized();

			float horizontal = Mathf.RadToDeg(Mathf.Acos(
				Mathf.Clamp(flat.Normalized().Dot(forward), -1f, 1f)));
			if (horizontal > Data.ViewAngleHorizontal * 0.5f)
			{
				_canSee = false;
				return;
			}
		}

		float vertical = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(delta.Y / distance, -1f, 1f)));
		if (Mathf.Abs(vertical) > Data.ViewAngleVertical)
		{
			_canSee = false;
			return;
		}

		// 只查世界层：格构 / 集装箱遮挡视线，玩家与敌人自身不挡
		var query = PhysicsRayQueryParameters3D.Create(eye, target, 1u);
		query.Exclude = _exclude;
		_canSee = GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;

		if (!_canSee)
			return;

		_lastKnownPosition = _player.GlobalPosition;

		if (State == EnemyState.Patrol || State == EnemyState.Search)
			EnterAlert();
	}

	/// <summary>§5.2：高处 +8m，低处 ×0.6。</summary>
	private float GetEffectiveViewDistance()
	{
		float gap = VerticalGapToPlayer();
		if (gap > 0.5f)
			return Data.ViewDistance + Data.HighGroundViewBonus;
		if (gap < -0.5f)
			return Data.ViewDistance * Data.LowGroundViewScale;
		return Data.ViewDistance;
	}

	private float VerticalGapToPlayer()
	{
		return GlobalPosition.Y - _player.GlobalPosition.Y;
	}

	private void OnNoise(Vector3 position, NoiseKind kind)
	{
		if (Data == null || _health == null || _health.IsDead || _playerHealth == null || _playerHealth.IsDead)
			return;

		float radius = kind == NoiseKind.Gunshot ? NoiseEmitter.GunshotRadius : Data.HearingRadius;
		if (GlobalPosition.DistanceTo(position) > radius)
			return;

		if (State == EnemyState.Combat)
			return;

		_lastKnownPosition = position;

		if (State == EnemyState.Patrol || State == EnemyState.Search)
			EnterAlert();
	}

	// ---------------------------------------------------------------- 状态

	private void UpdatePatrol(float dt)
	{
		if (_patrolPoints.Count == 0)
		{
			// 没有巡逻点就是驻守：保持面朝方向，不原地打转
			StopHorizontal(dt);
			return;
		}

		Vector3 target = _patrolPoints[_patrolIndex];
		if (FlatDistance(GlobalPosition, target) < 0.7f)
		{
			StopHorizontal(dt);
			_patrolWait += dt;
			if (_patrolWait >= _patrolWaitTarget)
			{
				_patrolWait = 0f;
				_patrolWaitTarget = RandomWait();
				_patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
			}
			return;
		}

		MoveTo(target, Data.PatrolSpeed);
		FaceTowards(target, dt);
	}

	private void UpdateAlert(float dt)
	{
		_stateTimer += dt;

		if (_stateTimer < Data.ReactionTime)
		{
			StopHorizontal(dt);
			FaceTowards(_lastKnownPosition, dt);
			return;
		}

		if (_canSee && VerticalGapToPlayer() <= Data.VerticalCombatLimit)
		{
			EnterCombat();
			return;
		}

		// 看不到人时朝声源 / 最后位置压过去，超过 20m 也停在这一状态
		if (FlatDistance(GlobalPosition, _lastKnownPosition) > 1f)
		{
			MoveTo(_lastKnownPosition, Data.PatrolSpeed);
			FaceTowards(_lastKnownPosition, dt);
		}
		else
		{
			StopHorizontal(dt);
			RotateY(dt * 1f);
		}

		if (_stateTimer >= Data.AlertTimeout)
			EnterSearch();
	}

	private void UpdateSearch(float dt)
	{
		_stateTimer += dt;

		if (_canSee && VerticalGapToPlayer() <= Data.VerticalCombatLimit)
		{
			EnterCombat();
			return;
		}

		if (FlatDistance(GlobalPosition, _lastKnownPosition) > 0.8f)
		{
			MoveTo(_lastKnownPosition, Data.PatrolSpeed);
			FaceTowards(_lastKnownPosition, dt);
		}
		else
		{
			StopHorizontal(dt);
			RotateY(dt * 1.2f);
		}

		if (_stateTimer >= Data.SearchTimeout)
			EnterPatrol();
	}

	private void UpdateCombat(float dt)
	{
		if (VerticalGapToPlayer() > Data.VerticalCombatLimit)
		{
			EnterAlert();
			return;
		}

		bool holdingCover = Data.Behavior == EnemyBehavior.Holder && _hasCover;

		// 守卫躲在掩体后本来就看不见玩家，不能因此判丢视线
		if (!_canSee && !(holdingCover && !_coverPeeking))
		{
			_stateTimer += dt;
			if (_stateTimer >= Data.LoseSightTime)
				EnterSearch();
		}
		else
		{
			_stateTimer = 0f;
		}

		if (State != EnemyState.Combat)
			return;

		if (Data.Behavior == EnemyBehavior.Runner)
			UpdateRunnerCombat(dt);
		else
			UpdateHolderCombat(dt);

		UpdateFiring(dt);
	}

	private void UpdateRunnerCombat(float dt)
	{
		Vector3 playerPosition = _player.GlobalPosition;
		float distance = FlatDistance(GlobalPosition, playerPosition);

		if (distance > Data.EngageMaxRange)
		{
			MoveTo(playerPosition, Data.CombatSpeed);
			FaceTowards(playerPosition, dt);
			return;
		}

		if (distance < Data.EngageMinRange)
		{
			Vector3 away = GlobalPosition + (GlobalPosition - playerPosition).Normalized() * 3f;
			MoveTo(away, Data.CombatSpeed);
			FaceTowards(playerPosition, dt);
			return;
		}

		// 斜向压上：交战时持续侧移，逼玩家跟枪
		_strafeTimer -= dt;
		if (_strafeTimer <= 0f)
		{
			_strafeTimer = (float)GD.RandRange(1.0, 2.0);
			_strafeDirection = (float)GD.RandRange(0.0, 1.0) > 0.5f ? 1f : -1f;
		}

		Vector3 toPlayer = playerPosition - GlobalPosition;
		toPlayer.Y = 0f;
		if (toPlayer.LengthSquared() > 0.0001f)
		{
			Vector3 right = new(-toPlayer.Normalized().Z, 0f, toPlayer.Normalized().X);
			MoveTo(GlobalPosition + right * (2f * _strafeDirection), Data.CombatSpeed * 0.55f);
		}

		FaceTowards(playerPosition, dt);
	}

	private void UpdateHolderCombat(float dt)
	{
		if (!_hasCover)
		{
			StopHorizontal(dt);
			FaceTowards(_player.GlobalPosition, dt);
			return;
		}

		_coverPhaseTimer -= dt;

		if (!_coverPeeking)
		{
			// 导航的 target_desired_distance 是 0.8m，判断"到点"必须比它宽，
			// 否则守卫会停在掩体前 0.8m 永远不进探头阶段（也就永远不开火）。
			if (FlatDistance(GlobalPosition, _coverPosition) > CoverArrivalDistance)
			{
				MoveTo(_coverPosition, Data.CombatSpeed);
				FaceTowards(_player.GlobalPosition, dt);
			}
			else
			{
				StopHorizontal(dt);
				FaceTowards(_player.GlobalPosition, dt);
				if (_coverPhaseTimer <= 0f)
				{
					_coverPeeking = true;
					_coverPhaseTimer = Data.PeekDuration;
				}
			}
		}
		else
		{
			if (FlatDistance(GlobalPosition, _peekPosition) > CoverArrivalDistance)
			{
				MoveTo(_peekPosition, Data.CombatSpeed);
				FaceTowards(_player.GlobalPosition, dt);
			}
			else
			{
				StopHorizontal(dt);
				FaceTowards(_player.GlobalPosition, dt);
			}

			if (_coverPhaseTimer <= 0f)
			{
				_coverPeeking = false;
				_coverPhaseTimer = Data.HideDuration;
			}
		}
	}

	private void EnterPatrol()
	{
		State = EnemyState.Patrol;
		_stateTimer = 0f;
		_patrolWait = 0f;
		_burstShotsLeft = 0;
	}

	private void EnterAlert()
	{
		if (State == EnemyState.Alert)
			return;

		State = EnemyState.Alert;
		_stateTimer = 0f;
		_burstShotsLeft = 0;
		Alerted?.Invoke();
	}

	private void EnterCombat()
	{
		State = EnemyState.Combat;
		_stateTimer = 0f;
		_fireTimer = Data.ReactionTime;
		_burstShotsLeft = 0;

		if (Data.Behavior == EnemyBehavior.Holder)
		{
			_coverPeeking = false;
			_coverPhaseTimer = Data.HideDuration;
			PickCover();
		}
	}

	private void EnterSearch()
	{
		State = EnemyState.Search;
		_stateTimer = 0f;
		_burstShotsLeft = 0;
	}

	// ---------------------------------------------------------------- 开火

	private void UpdateFiring(float dt)
	{
		if (!CanShoot())
		{
			_burstShotsLeft = 0;
			return;
		}

		_fireTimer -= dt;

		if (_burstShotsLeft > 0)
		{
			if (_fireTimer > 0f)
				return;

			Shoot();
			_burstShotsLeft--;
			_fireTimer = _burstShotsLeft > 0 ? Data.BurstShotInterval : Data.BurstCooldown;
			return;
		}

		if (_fireTimer <= 0f)
		{
			_burstShotsLeft = Data.BurstShots;
			_fireTimer = 0f;
		}
	}

	private bool CanShoot()
	{
		if (_staggerTimer > 0f || !_canSee)
			return false;

		if (Data.Behavior == EnemyBehavior.Holder && _hasCover && !_coverPeeking)
			return false;

		return true;
	}

	private void Shoot()
	{
		Vector3 origin = _eye.GlobalPosition;
		Vector3 target = _player.GlobalPosition + new Vector3(0f, 1.1f, 0f);
		Vector3 direction = (target - origin).Normalized();
		direction = HitscanResolver.ApplySpread(direction, GetSpreadDegrees());

		var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * 120f, 1u | 2u);
		query.Exclude = _exclude;
		var result = GetWorld3D().DirectSpaceState.IntersectRay(query);

		Vector3 muzzle = _gun != null ? _gun.GlobalPosition : origin;
		Vector3 endPoint = origin + direction * 120f;

		if (result.Count > 0)
		{
			endPoint = result["position"].AsVector3();
			SpawnImpact(endPoint, result["normal"].AsVector3());

			if (result["collider"].As<Node>() is PlayerController)
				_playerHealth.TakeDamage(Data.Damage, DeathCause.Gunshot);
		}

		SpawnTracer(muzzle, endPoint);
		_flashTimer = MuzzleFlashTime;
		if (_muzzleLight != null)
			_muzzleLight.Visible = true;

		Fired?.Invoke(muzzle);
	}

	private float GetSpreadDegrees()
	{
		float distance = FlatDistance(GlobalPosition, _player.GlobalPosition);
		float spread = distance < Data.SpreadMidDistance
			? Data.SpreadClose
			: distance < Data.SpreadFarDistance ? Data.SpreadMid : Data.SpreadFar;

		Vector3 horizontal = new(_player.Velocity.X, 0f, _player.Velocity.Z);
		if (horizontal.LengthSquared() > 1f)
			spread += Data.PlayerMovingSpreadBonus;
		if (!_player.IsOnFloor())
			spread += Data.PlayerMovingSpreadBonus;

		return spread;
	}

	// ---------------------------------------------------------------- 移动与工具

	private void MoveTo(Vector3 target, float speed)
	{
		_nav.TargetPosition = target;

		if (_nav.IsNavigationFinished())
		{
			StopHorizontal(0f);
			return;
		}

		Vector3 next = _nav.GetNextPathPosition();
		Vector3 direction = next - GlobalPosition;
		direction.Y = 0f;

		if (direction.LengthSquared() < 0.0001f)
		{
			StopHorizontal(0f);
			return;
		}

		direction = direction.Normalized();
		_velocity.X = direction.X * speed;
		_velocity.Z = direction.Z * speed;
	}

	private void StopHorizontal(float dt)
	{
		_velocity.X = Mathf.MoveToward(_velocity.X, 0f, 24f * Mathf.Max(dt, 0.016f));
		_velocity.Z = Mathf.MoveToward(_velocity.Z, 0f, 24f * Mathf.Max(dt, 0.016f));
	}

	private void ApplyGravity(float dt)
	{
		if (!IsOnFloor())
			_velocity.Y -= Gravity * dt;
		else if (_velocity.Y < 0f)
			_velocity.Y = 0f;

		_velocity.Y = Mathf.Max(_velocity.Y, -40f);
	}

	private void FaceTowards(Vector3 point, float dt)
	{
		Vector3 delta = point - GlobalPosition;
		delta.Y = 0f;
		if (delta.LengthSquared() < 0.0001f)
			return;

		float targetYaw = Mathf.Atan2(-delta.X, -delta.Z);
		Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, targetYaw, 1f - Mathf.Exp(-8f * Mathf.Max(dt, 0.016f))), 0f);
	}

	private float RandomWait()
	{
		return (float)GD.RandRange(Data.PatrolWaitMin, Data.PatrolWaitMax);
	}

	private static float FlatDistance(Vector3 a, Vector3 b)
	{
		return new Vector2(a.X - b.X, a.Z - b.Z).Length();
	}

	private void CollectAnchors()
	{
		_patrolPoints.Clear();
		_coverPairs.Clear();

		if (GetNodeOrNull<Node3D>("PatrolPoints") is Node3D patrol)
		{
			foreach (Node child in patrol.GetChildren())
			{
				if (child is Node3D marker)
					_patrolPoints.Add(marker.GlobalPosition);
			}
		}

		if (GetNodeOrNull<Node3D>("CoverPoints") is Node3D covers)
		{
			var markers = new List<Vector3>();
			foreach (Node child in covers.GetChildren())
			{
				if (child is Node3D marker)
					markers.Add(marker.GlobalPosition);
			}

			// 子节点成对出现：掩体点、探头点
			for (int i = 0; i + 1 < markers.Count; i += 2)
				_coverPairs.Add((markers[i], markers[i + 1]));
		}

		_hasCover = _coverPairs.Count > 0;
	}

	private void PickCover()
	{
		if (!_hasCover)
			return;

		float best = float.MaxValue;
		foreach ((Vector3 cover, Vector3 peek) in _coverPairs)
		{
			float distance = FlatDistance(GlobalPosition, cover);
			if (distance >= best)
				continue;

			best = distance;
			_coverPosition = cover;
			_peekPosition = peek;
		}
	}

	private void OnStaggered(float duration)
	{
		_staggerTimer = duration;
		_burstShotsLeft = 0;
	}

	private void OnDied()
	{
		_burstShotsLeft = 0;
		_nav.TargetPosition = GlobalPosition;
		SetPhysicsProcess(false);
	}

	// ---------------------------------------------------------------- 表现

	private void SpawnTracer(Vector3 from, Vector3 to)
	{
		if (TracerScene == null || TracerScene.Instantiate() is not Tracer tracer)
			return;

		GetTree().CurrentScene?.AddChild(tracer);
		tracer.Fire(from, to);
	}

	private void SpawnImpact(Vector3 point, Vector3 normal)
	{
		if (ImpactScene == null || ImpactScene.Instantiate() is not ImpactEffect impact)
			return;

		GetTree().CurrentScene?.AddChild(impact);
		impact.Play(point, normal);
	}
}
