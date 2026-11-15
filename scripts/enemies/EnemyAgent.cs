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

	// ---------------------------------------------------------------- 小队协同（2.0 §2.3–§2.6）

	/// <summary>所属小队；为空表示独立敌人（如增援），行为退回 1.0 逻辑。</summary>
	public SquadRuntime Squad { get; set; }
	/// <summary>Combat 下的槽位任务，不是新的顶层状态。</summary>
	public SquadSlot Slot { get; private set; } = SquadSlot.Suppress;
	/// <summary>槽位给的目标点（压制距离点 / 侧翼点 / 后撤点）。</summary>
	public Vector3 SlotTarget { get; set; }
	/// <summary>队长阵亡后降级为原地防守。</summary>
	public bool HoldPost { get; set; }
	/// <summary>被玩家直视的累计时长，用于放弃包抄（§2.3 规则 4）。</summary>
	public float LookedAtTime { get; set; }
	/// <summary>当前不适合包抄（被盯太久且没有掩体）。</summary>
	public bool FlankBlocked { get; set; }
	public bool IsRetreating { get; private set; }
	public float RetreatTimeLeft { get; set; }
	public float RetreatCooldownLeft { get; set; }

	public bool IsDead => _health == null || _health.IsDead;
	public float HealthRatio => _health != null && _health.MaxHealth > 0f ? _health.Health / _health.MaxHealth : 1f;
	public Vector3 PlayerPosition => _player != null ? _player.GlobalPosition : GlobalPosition;
	public float DistanceToPlayer => _player != null ? FlatDistance(GlobalPosition, _player.GlobalPosition) : 999f;
	/// <summary>最后已知位置（目视或队友共享），供潜行警戒与探针读取。</summary>
	public Vector3 LastKnownPosition => _lastKnownPosition;

	private Vector3 _retreatPoint;
	private CombatDirector _combat;

	public void SetSlot(SquadSlot slot) => Slot = slot;

	public void BeginRetreat(Vector3 point, float duration, float cooldown)
	{
		IsRetreating = true;
		_retreatPoint = point;
		RetreatTimeLeft = duration;
		RetreatCooldownLeft = cooldown;
		_burstShotsLeft = 0;
		ReleaseToken();
	}

	public void EndRetreat()
	{
		IsRetreating = false;
		RetreatTimeLeft = 0f;
		Slot = SquadSlot.Support;
	}

	/// <summary>小队令牌；独立敌人也走全局令牌，保证"同屏开火人数"这条线对增援同样生效。</summary>
	private CombatDirector Combat
	{
		get
		{
			if (Squad?.Director?.Combat != null)
				return Squad.Director.Combat;

			if (_combat != null && IsInstanceValid(_combat))
				return _combat;

			foreach (Node node in GetTree().GetNodesInGroup("squad_director"))
			{
				if (node is SquadDirector director && director.Combat != null)
				{
					_combat = director.Combat;
					return _combat;
				}
			}

			return null;
		}
	}

	private void ReleaseToken() => Combat?.Release(this);

	private const float Gravity = 18f;
	private const float PerceptionInterval = 0.1f;
	private const float MuzzleFlashTime = 0.05f;

	private EnemyHealth _health;
	private NavigationAgent3D _nav;
	private EnemyAnimation _animation;
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
	private bool _coverValid;
	private bool _coverPeeking;
	private float _coverPhaseTimer;
	private Vector3 _coverPosition;
	private Vector3 _peekPosition;
	private Vector3 _post;

	private float _flashTimer;
	private bool _seized;

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
		_post = GlobalPosition;

		_nav.AvoidanceEnabled = false;

		CollectAnchors();

		_animation = GetNodeOrNull<EnemyAnimation>("Animation");
		if (_animation != null)
			_animation.Data = Data;

		_health.Died += OnDied;
		_health.Staggered += OnStaggered;
		_health.Hit += OnHit;
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
			_health.Hit -= OnHit;
		}
	}

	public void TakeDamage(in DamageInfo info)
	{
		// 受害数值全部交给 EnemyHealth，这里只是命中盒的转发出口
		_health?.TakeDamage(info);
	}

	/// <summary>
	/// 处决演出期间被制服：停火、停移动、停在原地，直到结算结束。
	/// 不改生命值与状态机，只冻结本帧行为（处决对任意敌人有效）。
	/// </summary>
	public void Seize()
	{
		if (_health == null || _health.IsDead)
			return;

		_seized = true;
		_burstShotsLeft = 0;
		_fireTimer = 0f;
		ReleaseToken();
		_velocity = Vector3.Zero;
		Velocity = Vector3.Zero;
		_nav.TargetPosition = GlobalPosition;
		_animation?.SetSpeed(0f);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (Data == null || _health == null || _health.IsDead)
			return;

		// 处决演出期间被制服：停止移动、开火与转向，等待致命一击
		if (_seized)
		{
			_velocity = Vector3.Zero;
			Velocity = Vector3.Zero;
			_animation?.SetSpeed(0f);
			return;
		}

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
			UpdateAnimator();
			return;
		}

		// 共享情报（§2.5）：同队有人看见就等于本队进入警戒；跨队不共享，保持 1.0 的独立性
		if (Squad != null)
		{
			if (State == EnemyState.Patrol && Squad.AlertLevel > 0)
				EnterAlert();

			// 自己看不见时改用队友情报：置信度低时只借方向，不隔墙锁人
			if (!_canSee && State != EnemyState.Combat &&
				Squad.TryGetIntelTarget(this, out Vector3 intel, out _))
				_lastKnownPosition = intel;
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
		UpdateAnimator();

		// 离开交火就交还令牌，避免"隔着掩体占着开火权"
		if (State != EnemyState.Combat)
			ReleaseToken();
	}

	/// <summary>把真实水平位移速度交给骨架动画：播放速率按速度 / 剪辑原速缩放，脚不滑。</summary>
	private void UpdateAnimator()
	{
		if (_animation == null)
			return;

		Vector3 horizontal = new(Velocity.X, 0f, Velocity.Z);
		_animation.SetSpeed(horizontal.Length());
		_animation.SetAiming(State == EnemyState.Combat || State == EnemyState.Alert);
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

		// 发现即喊话：延迟后同步给同队，跨队不共享（§2.5）
		Squad?.ReportSighting(_lastKnownPosition);

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

	/// <summary>增援落地时由 AlertDirector 告知玩家大致方向：直接进入警戒并赶过去。</summary>
	/// <summary>
	/// 增援是在 AddChild 之后才被摆位的，_Ready 里记下的岗位会变成世界原点；
	/// 摆好位置后调用本方法重建岗位与感知基准。
	/// </summary>
	public void SyncPost()
	{
		_post = GlobalPosition;
		if (_lastKnownPosition.LengthSquared() < 0.0001f)
			_lastKnownPosition = GlobalPosition;
	}

	public void JoinAlert(Vector3 lastKnownPosition)
	{
		if (Data == null || _health == null || _health.IsDead || _playerHealth == null || _playerHealth.IsDead)
			return;

		_lastKnownPosition = lastKnownPosition;

		if (State == EnemyState.Patrol || State == EnemyState.Search)
			EnterAlert();
	}

	private void OnNoise(Vector3 position, NoiseKind kind)
	{
		if (Data == null || _health == null || _health.IsDead || _playerHealth == null || _playerHealth.IsDead)
			return;

		// 听觉半径按动作分级：静步 0 / 走路 8 / 冲刺 14 / 枪声 30（2.0 §3.4）
		float radius = StealthSystem.NoiseRadius(kind);
		if (radius <= 0f || GlobalPosition.DistanceTo(position) > radius)
			return;

		if (State == EnemyState.Combat)
			return;

		_lastKnownPosition = position;
		// 听觉只提升本小队警戒，不跨队（§2.5）
		Squad?.ReportNoise(position);

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

		if (_stateTimer < DifficultySystem.ReactionTime(Data.ReactionTime))
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

		// 看不到人时朝声源 / 最后位置压过去，超过 20m 也停在这一状态；
		// 小队成员在共享情报有效期内优先走向自己的槽位点（包抄从警戒阶段就开始绕侧翼）。
		Vector3 seek = _lastKnownPosition;
		if (Squad != null && Squad.HasSharedContact && SlotTarget != Vector3.Zero)
			seek = SlotTarget;
		Vector3 target = ClampToPost(seek);
		if (FlatDistance(GlobalPosition, target) > 1f)
		{
			MoveTo(target, Data.PatrolSpeed);
			FaceTowards(target, dt);
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

		Vector3 target = ClampToPost(_lastKnownPosition);
		if (FlatDistance(GlobalPosition, target) > 0.8f)
		{
			MoveTo(target, Data.PatrolSpeed);
			FaceTowards(target, dt);
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

		// 小队成员的位置由槽位决定，不再走 1.0 的掩体探头节奏
		bool holdingCover = Squad == null && UsesCover && _coverValid;

		// 守卫 / 铁锚帮躲在掩体后本来就看不见玩家，不能因此判丢视线
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

		// 小队成员走槽位分工；独立敌人（增援等）保持 1.0 的行为
		if (Squad != null)
		{
			UpdateSquadCombat(dt);
			UpdateFiring(dt);
			return;
		}

		switch (Data.Behavior)
		{
			case EnemyBehavior.Runner:
				UpdateRunnerCombat(dt);
				break;
			case EnemyBehavior.Ancla:
				UpdateAnclaCombat(dt);
				break;
			default:
				UpdateHolderCombat(dt);
				break;
		}

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
		if (!_coverValid)
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

	// 铁锚帮沿用守卫的掩体—探头节奏，区别在 EnterCombat 的选点被限制在岗位半径内；
	// 没有可用掩体时原地开火，不会像赤蛇会那样压上。
	private void UpdateAnclaCombat(float dt) => UpdateHolderCombat(dt);

	/// <summary>
	/// 小队槽位行为（2.0 §2.3）：压制守距离、包抄走侧翼点、支援跟队长、后撤进掩体。
	/// 开火权不在这里，统一由 <see cref="CombatDirector"/> 的令牌决定。
	/// </summary>
	private void UpdateSquadCombat(float dt)
	{
		Vector3 playerPosition = _player.GlobalPosition;

		if (IsRetreating)
		{
			if (FlatDistance(GlobalPosition, _retreatPoint) > 0.8f)
			{
				MoveTo(_retreatPoint, Data.CombatSpeed);
				FaceTowards(playerPosition, dt);
			}
			else
			{
				StopHorizontal(dt);
				FaceTowards(playerPosition, dt);
			}
			return;
		}

		switch (Slot)
		{
			case SquadSlot.Flank:
				// 走侧翼导航点；到点后原地压制
				if (FlatDistance(GlobalPosition, SlotTarget) > 1.0f)
				{
					MoveTo(SlotTarget, Data.CombatSpeed);
					FaceTowards(SlotTarget, dt);
				}
				else
				{
					StopHorizontal(dt);
					FaceTowards(playerPosition, dt);
				}
				return;

			case SquadSlot.Support:
			{
				EnemyAgent leader = Squad?.Leader;
				bool leaderAlive = leader != null && IsInstanceValid(leader) && !leader.IsDead;
				Vector3 anchor = leaderAlive ? leader.GlobalPosition : SlotTarget;
				float distance = FlatDistance(GlobalPosition, anchor);

				if (distance > Squad.Config.SupportMaxRange)
				{
					MoveTo(anchor, Data.CombatSpeed);
					FaceTowards(anchor, dt);
				}
				else if (distance < Squad.Config.SupportMinRange * 0.7f)
				{
					Vector3 away = GlobalPosition + (GlobalPosition - anchor).Normalized() * 2.5f;
					MoveTo(away, Data.CombatSpeed * 0.8f);
					FaceTowards(playerPosition, dt);
				}
				else
				{
					StopHorizontal(dt);
					FaceTowards(playerPosition, dt);
				}

				return;
			}
		}

		// Suppress：看得见就守住压制距离；看不见就朝共享情报摸过去
		if (!_canSee && Squad != null && Squad.TryGetIntelTarget(this, out Vector3 shared, out _))
		{
			// 置信度低时情报点已经换算成"沿最后方向的 6m 前出点"，不再隔墙锁人
			if (FlatDistance(GlobalPosition, shared) > 1.5f)
			{
				MoveTo(shared, Data.CombatSpeed);
				FaceTowards(shared, dt);
			}
			else
			{
				StopHorizontal(dt);
				FaceTowards(shared, dt);
			}

			return;
		}

		if (HoldPost)
		{
			if (FlatDistance(GlobalPosition, _post) > 2f)
				MoveTo(_post, Data.CombatSpeed);
			else
				StopHorizontal(dt);

			FaceTowards(playerPosition, dt);
			return;
		}

		if (FlatDistance(GlobalPosition, SlotTarget) > 1.5f)
		{
			MoveTo(SlotTarget, Data.CombatSpeed);
			FaceTowards(playerPosition, dt);
		}
		else
		{
			StopHorizontal(dt);
			FaceTowards(playerPosition, dt);
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
		_fireTimer = DifficultySystem.ReactionTime(Data.ReactionTime);
		_burstShotsLeft = 0;

		if (UsesCover)
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
		{
			// 失去视线立即交还令牌，让别人接替（§2.6）
			ReleaseToken();
			return false;
		}

		if (IsRetreating)
		{
			ReleaseToken();
			return false;
		}

		// 小队成员不用探头状态机（槽位移动自带掩护），掩体闸门只对独立敌人生效
		if (Squad == null && UsesCover && _coverValid && !_coverPeeking)
			return false;

		// 同屏开火令牌（2.0 §2.6）：拿不到就只做移动 / 掩护，不扣扳机
		CombatDirector combat = Combat;
		if (combat != null && !combat.Request(this))
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

		_animation?.PlayFire();
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

		// 难度只改敌人侧散布（§10.5），不做任何玩家减伤 / 回血
		spread = DifficultySystem.ApplySpread(spread);

		// 开场试探期：敌人还在校准射界，散布放大（§2.6 压力曲线）
		if (Combat is { EngageRampActive: true } director)
			spread *= director.Config.EngageRampSpreadScale;

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
		_coverValid = false;

		if (!_hasCover)
			return;

		float best = float.MaxValue;
		foreach ((Vector3 cover, Vector3 peek) in _coverPairs)
		{
			// 铁锚帮只使用岗位附近的掩体，避免追着玩家离开高点 / 要道
			if (Data.Behavior == EnemyBehavior.Ancla && Data.HoldRadius > 0f &&
				FlatDistance(cover, _post) > Data.HoldRadius)
				continue;

			float distance = FlatDistance(GlobalPosition, cover);
			if (distance >= best)
				continue;

			best = distance;
			_coverPosition = cover;
			_peekPosition = peek;
			_coverValid = true;
		}
	}

	/// <summary>守卫与铁锚帮共用掩体探头逻辑。</summary>
	private bool UsesCover => Data.Behavior == EnemyBehavior.Holder || Data.Behavior == EnemyBehavior.Ancla;

	/// <summary>把移动目标限制在铁锚帮的岗位半径内；其他行为原样返回。</summary>
	private Vector3 ClampToPost(Vector3 target)
	{
		if (Data.Behavior != EnemyBehavior.Ancla || Data.HoldRadius <= 0f)
			return target;

		Vector3 offset = target - _post;
		offset.Y = 0f;
		float distance = offset.Length();
		if (distance <= Data.HoldRadius)
			return target;

		return _post + offset / distance * Data.HoldRadius;
	}

	private void OnStaggered(float duration)
	{
		_staggerTimer = duration;
		_burstShotsLeft = 0;
		_animation?.PlayHit();
	}

	/// <summary>每次掉血都抽一下：轻武器（未达硬直阈值）也有可见的受击姿势。</summary>
	private void OnHit()
	{
		_animation?.PlayHit();
	}

	private void OnDied()
	{
		_burstShotsLeft = 0;
		ReleaseToken();
		_nav.TargetPosition = GlobalPosition;
		SetPhysicsProcess(false);
		_animation?.SetDead();
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
