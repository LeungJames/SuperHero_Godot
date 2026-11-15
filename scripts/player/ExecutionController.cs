using Godot;
using System;

/// <summary>
/// C 阶段处决演出：站在任意存活敌人的背后按下处决键，镜头临时接管做一段击杀分镜。
///
/// 只负责判定、输入锁定与镜头节奏，伤害照常走 EnemyHealth / DamageInfo；
/// 不区分敌人变体与警觉状态（巡逻、警戒、交战、搜索中的敌人都可处决）。
/// 机位使用 PlayerCamera 的外部接管接口（A 阶段预留），参数全部在 ExecutionData。
/// </summary>
public partial class ExecutionController : Node3D
{
	[Export] public ExecutionData Data { get; set; }

	/// <summary>处决开始（参数为被处决的敌人）。</summary>
	public event Action<EnemyHealth> Started;

	/// <summary>致命一击结算完成。</summary>
	public event Action<EnemyHealth> Struck;

	/// <summary>演出与镜头回收结束，玩家恢复操作。</summary>
	public event Action Finished;

	/// <summary>当前是否有可处决目标（HUD / 自动化验证可读）。</summary>
	public bool HasTarget => _promptTarget != null;

	/// <summary>是否正在演出。</summary>
	public bool IsPerforming => _phase != Phase.Idle;

	private enum Phase
	{
		Idle,
		Perform,
		Finalize,
	}

	private const uint WorldMask = 1;
	private const float EnemyDamage = 9999f;

	private PlayerController _player;
	private PlayerHealth _health;
	private PlayerCamera _camera;
	private Camera3D _view;
	private PlayerAnimation _animation;
	private HudController _hud;
	private GameManager _game;

	private readonly Godot.Collections.Array<Rid> _exclude = new();

	private Phase _phase = Phase.Idle;
	private EnemyHealth _promptTarget;
	private EnemyHealth _target;
	private float _cooldown;
	private float _timer;
	private bool _struck;
	private bool _inputWasEnabled;
	private bool _promptShown;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		if (_player == null)
		{
			GD.PushWarning("ExecutionController: 父节点不是 PlayerController，处决关闭。");
			SetProcess(false);
			return;
		}

		_health = _player.GetNodeOrNull<PlayerHealth>("Health");
		_camera = _player.GetNodeOrNull<PlayerCamera>("Head");
		_view = _player.GetNodeOrNull<Camera3D>("Head/Camera3D");
		_animation = _player.GetNodeOrNull<PlayerAnimation>("Animation");
		_game = GetNodeOrNull<GameManager>("../../../Managers/GameManager");

		_exclude.Add(_player.GetRid());

		if (_health != null)
			_health.Died += OnPlayerDied;

		if (Data == null)
			GD.PushWarning("ExecutionController: 没有配置 ExecutionData，处决关闭。");
	}

	public override void _ExitTree()
	{
		if (_health != null)
			_health.Died -= OnPlayerDied;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("execution"))
			TryExecute();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (_cooldown > 0f)
			_cooldown = Mathf.Max(0f, _cooldown - dt);

		switch (_phase)
		{
			case Phase.Perform:
				UpdatePerform(dt);
				break;
			case Phase.Finalize:
				UpdateFinalize(dt);
				break;
			default:
				UpdatePrompt();
				break;
		}
	}

	// ---------------------------------------------------------------- 触发

	/// <summary>尝试处决面前背后最近的敌人；返回是否成功开始演出。</summary>
	public bool TryExecute()
	{
		if (!CanAct())
			return false;

		EnemyHealth target = FindTarget();
		if (target == null)
			return false;

		Begin(target);
		return true;
	}

	private bool CanAct()
	{
		return Data != null && _player != null && _health != null && !_health.IsDead
			&& _player.InputEnabled && _camera != null && _view != null
			&& _phase == Phase.Idle && _cooldown <= 0f
			&& _player.State == MoveState.Normal && _player.IsOnFloor();
	}

	/// <summary>任意存活敌人：背后角度、水平距离、高低差、视线四项都满足时取最近的一个。</summary>
	private EnemyHealth FindTarget()
	{
		if (Data == null || _view == null)
			return null;

		Vector3 feet = _player.GlobalPosition;
		Vector3 eye = _view.GlobalPosition;
		Vector3 forward = -_view.GlobalTransform.Basis.Z;

		EnemyHealth best = null;
		float bestDistance = float.MaxValue;

		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyHealth health || health.IsDead)
				continue;
			if (health.GetParent() is not EnemyAgent agent)
				continue;

			Vector3 enemyFeet = agent.GlobalPosition;
			Vector3 toPlayer = Flat(feet - enemyFeet);
			float distance = toPlayer.Length();

			if (distance > Data.Range || distance < 0.05f)
				continue;
			if (Mathf.Abs(feet.Y - enemyFeet.Y) > Data.MaxVerticalGap)
				continue;

			Vector3 enemyForward = Flat(-agent.GlobalTransform.Basis.Z);
			if (enemyForward.LengthSquared() < 0.0001f)
				continue;
			if (enemyForward.Normalized().Dot(toPlayer / distance) > Data.RearDot)
				continue;

			Vector3 aimPoint = enemyFeet + Vector3.Up * Data.AimHeight;
			Vector3 toEnemy = aimPoint - eye;
			if (toEnemy.LengthSquared() < 0.0001f)
				continue;
			if (forward.Dot(toEnemy.Normalized()) < Data.FacingDot)
				continue;

			// 只查关卡几何：格构与集装箱挡住时不能隔墙处决
			var query = PhysicsRayQueryParameters3D.Create(eye, aimPoint, WorldMask);
			query.Exclude = _exclude;
			if (_player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0)
				continue;

			if (distance < bestDistance)
			{
				best = health;
				bestDistance = distance;
			}
		}

		return best;
	}

	private void UpdatePrompt()
	{
		_promptTarget = CanAct() ? FindTarget() : null;
		bool show = _promptTarget != null;
		if (show == _promptShown)
			return;

		_promptShown = show;
		Hud?.SetPrompt(show ? Data.PromptText : string.Empty);
	}

	// ---------------------------------------------------------------- 演出

	private void Begin(EnemyHealth target)
	{
		_target = target;
		_promptTarget = null;
		_phase = Phase.Perform;
		_timer = 0f;
		_struck = false;
		_inputWasEnabled = _player.InputEnabled;

		// 锁住移动、射击与视角；身体换成全身可见，镜头交给演出机位
		_player.InputEnabled = false;
		_player.Velocity = Vector3.Zero;
		_camera.LookEnabled = false;
		_camera.SetPerformanceBody(true);
		FaceTarget(target);
		GetAgent(target)?.Seize();

		_camera.TakeOverTransform(BuildCameraTransform(target), Data.BlendInTime);

		if (_promptShown)
		{
			_promptShown = false;
			Hud?.SetPrompt(string.Empty);
		}

		Started?.Invoke(target);
	}

	private void UpdatePerform(float dt)
	{
		_timer += dt;

		if (!_struck && _timer >= Data.StrikeTime)
		{
			_struck = true;
			Strike();
		}

		if (_timer >= Data.StrikeTime + Data.RecoverTime)
		{
			_camera?.ReleaseTakeOver(Data.BlendOutTime);
			_phase = Phase.Finalize;
			_timer = 0f;
		}
	}

	private void Strike()
	{
		if (_target == null || _target.IsDead)
			return;

		EnemyAgent agent = GetAgent(_target);
		Vector3 enemyFeet = agent?.GlobalPosition ?? _player.GlobalPosition;
		Vector3 point = enemyFeet + Vector3.Up * Data.AimHeight;
		Vector3 direction = Flat(point - _player.GlobalPosition);
		if (direction.LengthSquared() < 0.0001f)
			direction = -_player.GlobalTransform.Basis.Z;
		else
			direction = direction.Normalized();

		_animation?.PlayExecutionStrike();
		_target.TakeDamage(new DamageInfo(EnemyDamage, point, direction, false));

		// 暗杀 / 处决留痕：不发声，但会留下尸体供敌人发现（2.0 §3.5）
		bool unaware = agent == null ||
			agent.State is EnemyState.Patrol or EnemyState.Alert or EnemyState.Search;
		(GetTree().GetFirstNodeInGroup("stealth_director") as StealthDirector)?.NotifyExecution(_target, unaware);

		Struck?.Invoke(_target);
	}

	private void UpdateFinalize(float dt)
	{
		_timer += dt;

		// 镜头快回到玩家眼睛时收起全身，避免穿进角色模型
		if (_view != null && _view.GlobalPosition.DistanceTo(
				_player.GlobalPosition + Vector3.Up * 1.62f) < Data.BodyHideDistance)
			_camera?.SetPerformanceBody(false);

		if (_timer < Data.BlendOutTime)
			return;

		End();
	}

	private void End()
	{
		_camera?.SetPerformanceBody(false);

		bool playing = _game == null || _game.State == GameState.Playing;
		if (_health != null && !_health.IsDead && playing)
		{
			_player.InputEnabled = _inputWasEnabled;
			if (_camera != null)
				_camera.LookEnabled = true;
		}

		_cooldown = Data != null ? Data.Cooldown : 1.4f;
		_phase = Phase.Idle;
		_target = null;
		_promptTarget = null;
		_promptShown = false;

		Finished?.Invoke();
	}

	/// <summary>演出中玩家死亡或进入结算：立刻交还镜头，不再恢复操作。</summary>
	private void Abort()
	{
		if (_phase == Phase.Idle)
			return;

		_camera?.ReleaseTakeOver(0.1f);
		_camera?.SetPerformanceBody(false);
		_phase = Phase.Idle;
		_target = null;
		_promptTarget = null;
		_promptShown = false;
	}

	private void OnPlayerDied(DeathCause cause)
	{
		Abort();
	}

	// ---------------------------------------------------------------- 工具

	private void FaceTarget(EnemyHealth target)
	{
		EnemyAgent agent = GetAgent(target);
		if (agent == null)
			return;

		Vector3 delta = agent.GlobalPosition - _player.GlobalPosition;
		delta.Y = 0f;
		if (delta.LengthSquared() < 0.0001f)
			return;

		// 与 EnemyAgent.FaceTowards 同一套朝向约定，演出时身体正对目标
		_player.Rotation = new Vector3(0f, Mathf.Atan2(-delta.X, -delta.Z), 0f);
	}

	/// <summary>
	/// 机位相对「玩家—敌人」连线做侧向构图，能同时看到处决者与目标；
	/// 两侧被墙挡住时退到玩家肩后，仍被挡就沿视线拉近，避免镜头穿进几何体。
	/// 不依赖敌人的朝向：目标可能正对墙或刚从巡逻转身。
	/// </summary>
	private Transform3D BuildCameraTransform(EnemyHealth target)
	{
		EnemyAgent agent = GetAgent(target);
		Vector3 enemyFeet = agent?.GlobalPosition ?? _player.GlobalPosition;

		Vector3 axis = Flat(enemyFeet - _player.GlobalPosition);
		if (axis.LengthSquared() < 0.0001f)
			axis = Flat(_player.GlobalTransform.Basis.Z);
		if (axis.LengthSquared() < 0.0001f)
			axis = Vector3.Forward;
		axis = axis.Normalized();

		Vector3 side = axis.Cross(Vector3.Up).Normalized();
		Vector3 anchor = enemyFeet + Vector3.Up * Data.CameraLookHeight - axis * Data.CameraLookAhead;
		Vector3 rise = Vector3.Up * Data.CameraRise;

		Vector3 best = anchor + side * Data.CameraSideOffset + rise;
		float bestClearance = Clearance(anchor, best);
		Consider(anchor, anchor - side * Data.CameraSideOffset + rise, ref best, ref bestClearance);

		// 两侧都贴墙时退到玩家肩后：玩家来路通常没有遮挡
		Vector3 shoulder = _player.GlobalPosition - axis * 0.55f
			+ Vector3.Up * (Data.CameraLookHeight + 0.25f) + side * 0.5f;
		Consider(anchor, shoulder, ref best, ref bestClearance);

		Vector3 direction = best - anchor;
		float length = direction.Length();
		if (length > 0.01f && bestClearance < length)
		{
			float safe = Mathf.Max(bestClearance, 0.6f);
			best = anchor + direction / length * safe;
		}

		// 不要钻到地面以下
		best.Y = Mathf.Max(best.Y, enemyFeet.Y + 0.3f);

		return new Transform3D(Basis.Identity, best).LookingAt(anchor, Vector3.Up);
	}

	private void Consider(Vector3 anchor, Vector3 candidate, ref Vector3 best, ref float bestClearance)
	{
		float clearance = Clearance(anchor, candidate);
		if (clearance <= bestClearance + 0.05f)
			return;

		bestClearance = clearance;
		best = candidate;
	}

	/// <summary>注视点到机位之间还剩多少可用距离（被墙挡住时减去安全半径）。</summary>
	private float Clearance(Vector3 from, Vector3 to)
	{
		float length = from.DistanceTo(to);
		var query = PhysicsRayQueryParameters3D.Create(from, to, WorldMask);
		Godot.Collections.Dictionary hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (hit.Count == 0)
			return length;
		return Mathf.Max(from.DistanceTo(hit["position"].AsVector3()) - Data.CameraCollisionRadius, 0f);
	}

	private static EnemyAgent GetAgent(EnemyHealth health)
	{
		return health?.GetParent() as EnemyAgent;
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

	/// <summary>HUD 是按需查找的：Hud 的 _Ready 可能晚于玩家。</summary>
	private HudController Hud
	{
		get
		{
			_hud ??= GetTree().GetFirstNodeInGroup("hud") as HudController;
			return _hud;
		}
	}
}
