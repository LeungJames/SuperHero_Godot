using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Boss 霍克：三阶段、六招式、背部核心弱点与死亡演出（2.0 设计 §8.2–§8.5）。
/// 不做击杀/胜利判定：死亡只广播 <see cref="Defeated"/>，全歼与直升机流程仍由
/// EnemyRegistry / GameManager 负责。
/// </summary>
public partial class BossController : CharacterBody3D, IDamageable
{
	private enum BossState
	{
		Dormant,
		Chase,
		Windup,
		Charge,
		Recover,
		Turn,
		PhaseShift,
		Dead,
	}

	private const float Gravity = 18f;

	[Export] public BossConfig Config { get; set; }
	[Export] public BossMoveSet Moves { get; set; }
	[Export] public PackedScene DeathBurstScene { get; set; }

	public event Action<float, float> HealthChanged;
	public event Action<int> PhaseChanged;
	public event Action<bool> WeakPointChanged;
	public event Action Engaged;
	public event Action Defeated;

	public float Health { get; private set; }
	public float MaxHealth => Config != null ? Config.MaxHealth : 0f;
	public int CurrentPhase { get; private set; } = 1;
	public bool IsEngaged { get; private set; }
	public bool IsDefeated { get; private set; }
	public bool WeakPointExposed { get; private set; }
	public bool HasSummoned { get; private set; }
	public string StateName => _state.ToString();

	private PlayerController _player;
	private PlayerHealth _playerHealth;
	private Node3D _visual;
	private Hitbox _coreHitbox;
	private Node _hitboxes;
	private OmniLight3D _hitFlash;
	private BossArena _arena;
	private BossPhaseBanner _banner;
	private NarrativeDirector _narrative;

	private BossState _state = BossState.Dormant;
	private BossMoveData _currentMove;
	private BossMoveData _lastMove;
	private int _moveStage;
	private float _stateTimer;
	private float _attackCooldown;
	private float _weakTimer;
	private float _hitFlashTimer;
	private bool _activationPending;
	private bool _deathStarted;

	private readonly Dictionary<BossMoveData, float> _cooldowns = new();
	private readonly List<Vector3> _rainCenters = new();
	private Vector3 _chargeStart;
	private Vector3 _chargeEnd;
	private Vector3 _chargeDirection;
	private bool _chargeHit;

	public override void _Ready()
	{
		AddToGroup("boss");

		_visual = GetNodeOrNull<Node3D>("Visual");
		_hitboxes = GetNodeOrNull<Node>("Hitboxes");
		_coreHitbox = GetNodeOrNull<Hitbox>("Hitboxes/Core");
		_hitFlash = GetNodeOrNull<OmniLight3D>("HitFlash");

		Health = MaxHealth;
		SetCoreActive(false);
		CallDeferred(nameof(BindSceneRefs));

		if (Config == null || Moves == null)
			GD.PushError("BossController: 缺少 BossConfig 或 BossMoveSet。");
	}

	private void BindSceneRefs()
	{
		_arena = GetTree().GetFirstNodeInGroup("boss_arena") as BossArena;
		_narrative = GetTree().GetFirstNodeInGroup("narrative_director") as NarrativeDirector;
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_playerHealth = _player?.GetNodeOrNull<PlayerHealth>("Health");

		_banner = new BossPhaseBanner { Name = "BossPhaseBanner" };
		GetTree().CurrentScene?.AddChild(_banner);
	}

	// ------------------------------------------------------------ 战斗入口

	/// <summary>玩家踏入场地：先播登场过场（看过的会跳过），再正式激活。</summary>
	public void BeginEncounter()
	{
		if (IsEngaged || IsDefeated)
			return;

		// 检查点重试直接进战斗，不重复播放已看过的登场过场
		if (RunCheckpoint.ConsumedOnLoad)
		{
			Activate();
			return;
		}

		_narrative ??= GetTree().GetFirstNodeInGroup("narrative_director") as NarrativeDirector;

		if (_narrative != null && _narrative.PlayBossIntro())
		{
			_activationPending = true;
			_narrative.Cutscenes.Finished += OnIntroFinished;
			return;
		}

		Activate();
	}

	private void OnIntroFinished()
	{
		if (!_activationPending)
			return;

		_activationPending = false;
		if (_narrative?.Cutscenes != null)
			_narrative.Cutscenes.Finished -= OnIntroFinished;

		Activate();
	}

	public void Activate()
	{
		if (IsEngaged || IsDefeated)
			return;

		IsEngaged = true;
		_state = BossState.Chase;
		_attackCooldown = 1.0f;
		HealthChanged?.Invoke(Health, MaxHealth);
		Engaged?.Invoke();
	}

	// ------------------------------------------------------------ 主循环

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (IsDefeated)
			return;

		_hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - dt);
		if (_hitFlash != null && _hitFlashTimer <= 0f && _hitFlash.Visible)
			_hitFlash.Visible = false;

		if (!IsEngaged)
		{
			ApplyGravity(dt);
			Velocity = new Vector3(0f, Velocity.Y, 0f);
			MoveAndSlide();
			return;
		}

		if (_player == null || _playerHealth == null)
			EnsurePlayer();
		if (_player == null)
			return;

		_attackCooldown = Mathf.Max(0f, _attackCooldown - dt);
		foreach (BossMoveData move in _cooldowns.Keys)
		{
			if (_cooldowns[move] > 0f)
				_cooldowns[move] = Mathf.Max(0f, _cooldowns[move] - dt);
		}

		UpdateWeakPoint(dt);

		switch (_state)
		{
			case BossState.Chase:
				UpdateChase(dt);
				break;
			case BossState.Windup:
				UpdateWindup(dt);
				break;
			case BossState.Charge:
				UpdateCharge(dt);
				break;
			case BossState.Recover:
				UpdateRecover(dt);
				break;
			case BossState.Turn:
				UpdateTurn(dt);
				break;
			case BossState.PhaseShift:
				UpdatePhaseShift(dt);
				break;
		}

		ApplyGravity(dt);
		MoveAndSlide();
		ClampToArena();
	}

	private void UpdateChase(float dt)
	{
		if (_playerHealth.IsDead)
		{
			StopHorizontal(dt);
			return;
		}

		FaceTowards(_player.GlobalPosition, dt);

		float distance = FlatDistance(GlobalPosition, _player.GlobalPosition);
		float speed = CurrentPhase >= 3 ? (Config?.EnrageSpeed ?? 4f) : (Config?.ChaseSpeed ?? 3.2f);

		if (distance > 3.4f)
		{
			Vector3 direction = Flat(_player.GlobalPosition - GlobalPosition).Normalized();
			Velocity = new Vector3(direction.X * speed, Velocity.Y, direction.Z * speed);
		}
		else
		{
			StopHorizontal(dt);
		}

		if (_attackCooldown > 0f)
			return;

		BossMoveData move = PickMove(distance);
		if (move != null)
			StartMove(move);
	}

	private void StartMove(BossMoveData move)
	{
		_currentMove = move;
		_moveStage = 0;
		_state = BossState.Windup;
		_stateTimer = Mathf.Max(move.Windup, 0.01f);
		_attackCooldown = Mathf.Max(move.Cooldown, 0.1f);
		_cooldowns[move] = move.Cooldown;
		_lastMove = move;

		if (move.Kind == BossMoveKind.Charge)
		{
			_chargeDirection = Flat(_player.GlobalPosition - GlobalPosition).Normalized();
			_chargeStart = GlobalPosition;
			_chargeEnd = ClampPoint(_chargeStart + _chargeDirection * move.ChargeDistance);
			_chargeHit = false;
		}

		if (move.Kind == BossMoveKind.AnchorRain)
			PrepareRain(move);
		else
			SpawnTelegraph(move);

		PlaySfx(move.Sfx, GlobalPosition);
	}

	private void UpdateWindup(float dt)
	{
		StopHorizontal(dt);
		_stateTimer -= dt;
		if (_stateTimer > 0f)
			return;

		ResolveMove();
	}

	private void UpdateCharge(float dt)
	{
		_stateTimer -= dt;

		Vector3 delta = _chargeEnd - GlobalPosition;
		Vector3 flat = Flat(delta);
		if (flat.Length() > 0.3f)
			Velocity = new Vector3(flat.Normalized().X * (Config?.ChaseSpeed ?? 3.2f) * 3.2f,
				Velocity.Y, flat.Normalized().Z * (Config?.ChaseSpeed ?? 3.2f) * 3.2f);
		else
			StopHorizontal(dt);

		if (!_chargeHit && _currentMove != null
			&& Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y) <= 2.4f
			&& DistanceToSegment(_player.GlobalPosition, _chargeStart, _chargeEnd) <= 1.7f)
		{
			_chargeHit = true;
			DamagePlayer(_currentMove.Damage);
		}

		if (_stateTimer <= 0f)
			EnterRecover();
	}

	private void UpdateRecover(float dt)
	{
		StopHorizontal(dt);
		_stateTimer -= dt;

		if (_stateTimer <= 0f)
		{
			_state = BossState.Turn;
			_stateTimer = Mathf.Max(Config?.TurnTime ?? 0.45f, 0.01f);
		}
	}

	private void UpdateTurn(float dt)
	{
		StopHorizontal(dt);
		FaceTowards(_player.GlobalPosition, dt);
		_stateTimer -= dt;

		if (_stateTimer <= 0f)
			_state = BossState.Chase;
	}

	private void UpdatePhaseShift(float dt)
	{
		StopHorizontal(dt);
		_stateTimer -= dt;
		if (_stateTimer > 0f)
			return;

		// 一次大招可能跨两个阈值：先进入下一阶段，再在同一处判断是否继续
		if (RefreshPhase())
			return;

		_state = BossState.Chase;
		_attackCooldown = Mathf.Max(_attackCooldown, 0.4f);
	}

	private void ResolveMove()
	{
		BossMoveData move = _currentMove;
		if (move == null)
		{
			EnterRecover();
			return;
		}

		switch (move.Kind)
		{
			case BossMoveKind.Sweep:
				TryHitCone(move.Damage, move.Range, move.AngleDegrees);
				break;

			case BossMoveKind.Charge:
				_state = BossState.Charge;
				_stateTimer = 0.4f;
				return;

			case BossMoveKind.ChainPull:
				if (PlayerInCone(move.Range, move.AngleDegrees) && HasLineOfSight())
				{
					DamagePlayer(move.Damage);
					PullPlayer(move.PullDistance);
				}
				break;

			case BossMoveKind.Summon:
				SummonAdds();
				break;

			case BossMoveKind.AnchorRain:
				ResolveRain(move);
				break;

			case BossMoveKind.DoubleHook:
				if (_moveStage == 0)
				{
					TryHitCone(move.Damage, move.Range, move.AngleDegrees);
					_moveStage = 1;
					_stateTimer = Mathf.Max(move.SecondWindup, 0.01f);
					SpawnTelegraph(move);
					PlaySfx(move.Sfx, GlobalPosition);
					return;
				}

				TryHitCone(move.SecondDamage, move.Range, move.AngleDegrees);
				break;
		}

		EnterRecover();
	}

	private void EnterRecover()
	{
		_state = BossState.Recover;
		_stateTimer = Mathf.Max(Config?.RecoverTime ?? 0.65f, 0.01f);
		_weakTimer = Mathf.Max(Config?.WeakPointExposure ?? 2.5f, 0.1f);
		SetCoreActive(true);
	}

	// ------------------------------------------------------------ 阶段与弱点

	private bool RefreshPhase()
	{
		if (Config == null || IsDefeated || !IsEngaged || _state == BossState.PhaseShift)
			return false;

		float ratio = MaxHealth > 0f ? Health / MaxHealth : 0f;

		if (CurrentPhase == 1 && ratio <= Config.Phase3Threshold)
		{
			AdvancePhase(2);
			return true;
		}

		if (CurrentPhase == 1 && ratio <= Config.Phase2Threshold)
		{
			AdvancePhase(2);
			return true;
		}

		if (CurrentPhase == 2 && ratio <= Config.Phase3Threshold)
		{
			AdvancePhase(3);
			return true;
		}

		return false;
	}

	private void AdvancePhase(int next)
	{
		CurrentPhase = next;
		_state = BossState.PhaseShift;
		_stateTimer = Mathf.Max(Config?.PhaseShiftTime ?? 0.8f, 0.01f);
		SetCoreActive(false);
		_weakTimer = 0f;
		PhaseChanged?.Invoke(next);

		string title = next switch
		{
			2 => "阶段 II · 断链",
			_ => "阶段 III · 沉锚",
		};
		string sfx = next == 2 ? "CLANG!" : "THOOM!";

		_banner ??= new BossPhaseBanner { Name = "BossPhaseBanner" };
		if (_banner.GetParent() == null)
			GetTree().CurrentScene?.AddChild(_banner);
		_banner.ShowPhase(next, title, sfx, Config?.PhaseShiftTime ?? 0.8f);

		PlaySfx("alert", GlobalPosition, -2f);
	}

	private void UpdateWeakPoint(float dt)
	{
		if (_weakTimer <= 0f)
			return;

		_weakTimer = Mathf.Max(0f, _weakTimer - dt);
		if (_weakTimer <= 0f)
			SetCoreActive(false);
	}

	private void SetCoreActive(bool active)
	{
		if (_coreHitbox == null || WeakPointExposed == active)
			return;

		WeakPointExposed = active;
		_coreHitbox.CollisionLayer = active ? 8u : 0u;
		WeakPointChanged?.Invoke(active);
	}

	/// <summary>命中盒已经把部位倍率乘进来；这里只扣血、判阶段、判死亡。</summary>
	public void TakeDamage(in DamageInfo info)
	{
		if (IsDefeated || !IsEngaged || info.Damage <= 0f)
			return;

		Health = Mathf.Max(0f, Health - info.Damage);
		_hitFlashTimer = 0.08f;
		if (_hitFlash != null)
			_hitFlash.Visible = true;

		HealthChanged?.Invoke(Health, MaxHealth);

		if (Health <= 0f)
		{
			BeginDeath();
			return;
		}

		if (_state != BossState.PhaseShift)
			RefreshPhase();
	}

	// ------------------------------------------------------------ 招式

	private BossMoveData PickMove(float distance)
	{
		if (Moves?.Moves == null)
			return null;

		BossMoveData best = null;
		float bestWeight = 0f;
		int phaseBit = 1 << (CurrentPhase - 1);

		foreach (BossMoveData move in Moves.Moves)
		{
			if (move == null || (move.PhaseMask & phaseBit) == 0)
				continue;
			if (move.Kind == BossMoveKind.Summon && (HasSummoned || CurrentPhase != 2))
				continue;
			if (_cooldowns.TryGetValue(move, out float cooldown) && cooldown > 0f)
				continue;
			if (move == _lastMove)
				continue;
			if (move.Kind is BossMoveKind.Sweep or BossMoveKind.DoubleHook or BossMoveKind.AnchorRain &&
				distance > move.Range + 1.5f)
				continue;
			if (move.Kind == BossMoveKind.ChainPull && distance > move.Range)
				continue;

			float weight = move.Kind == BossMoveKind.Summon ? 2.2f : (float)GD.RandRange(0.75, 1.25);
			if (weight <= bestWeight)
				continue;

			bestWeight = weight;
			best = move;
		}

		return best;
	}

	private void TryHitCone(float damage, float range, float angleDegrees)
	{
		if (PlayerInCone(range, angleDegrees))
			DamagePlayer(damage);
	}

	private bool PlayerInCone(float range, float angleDegrees)
	{
		Vector3 to = _player.GlobalPosition - GlobalPosition;
		to.Y = 0f;
		float distance = to.Length();
		if (distance > range)
			return false;
		if (Mathf.Abs(_player.GlobalPosition.Y - GlobalPosition.Y) > 2.4f)
			return false;
		if (distance < 0.05f)
			return true;

		Vector3 forward = Flat(-GlobalTransform.Basis.Z);
		if (forward.LengthSquared() < 0.001f)
			return true;

		float angle = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
			forward.Normalized().Dot(to / distance), -1f, 1f)));
		return angle <= angleDegrees * 0.5f;
	}

	private bool HasLineOfSight()
	{
		Vector3 from = GlobalPosition + Vector3.Up * 1.8f;
		Vector3 to = _player.GlobalPosition + Vector3.Up * 1.0f;
		var query = PhysicsRayQueryParameters3D.Create(from, to, 1u);
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
		return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
	}

	private void PullPlayer(float distance)
	{
		Vector3 direction = Flat(GlobalPosition - _player.GlobalPosition);
		if (direction.LengthSquared() < 0.001f)
			return;

		direction = direction.Normalized();
		Vector3 target = _player.GlobalPosition + direction * distance;
		target = ClampPoint(target);
		target.Y = _player.GlobalPosition.Y;
		_player.GlobalPosition = target;
		_player.Velocity = new Vector3(-direction.X * 2f, _player.Velocity.Y, -direction.Z * 2f);
	}

	private void SummonAdds()
	{
		if (HasSummoned || Config?.SummonScene == null)
			return;

		HasSummoned = true;
		SpawnAdd(Config.SummonLeft);
		SpawnAdd(Config.SummonRight);
	}

	private void SpawnAdd(Vector3 position)
	{
		if (Config?.SummonScene?.Instantiate() is not EnemyAgent agent)
			return;

		GetTree().CurrentScene?.AddChild(agent);
		agent.GlobalPosition = position;
		agent.SyncPost();
		agent.JoinAlert(_player.GlobalPosition);
	}

	private void PrepareRain(BossMoveData move)
	{
		_rainCenters.Clear();
		Vector3 origin = _player.GlobalPosition;

		for (int i = 0; i < 3; i++)
		{
			Vector2 offset = i == 0
				? Vector2.Zero
				: new Vector2((float)GD.RandRange(-5.0, 5.0), (float)GD.RandRange(-5.0, 5.0));
			Vector3 center = ClampPoint(new Vector3(origin.X + offset.X, 0.05f, origin.Z + offset.Y));
			_rainCenters.Add(center);
			SpawnDisc(center, move.Radius, new Color(1f, 0.35f, 0.12f, 0.34f), move.Windup);
		}

		PlaySfx(move.Sfx, origin, 0f);
	}

	private void ResolveRain(BossMoveData move)
	{
		bool hit = false;
		foreach (Vector3 center in _rainCenters)
		{
			if (Mathf.Abs(_player.GlobalPosition.Y - center.Y) > 2.6f)
				continue;
			if (FlatDistance(center, _player.GlobalPosition) > move.Radius)
				continue;
			hit = true;
			break;
		}

		if (hit)
			DamagePlayer(move.Damage);

		_arena?.BreakColumn();
		PlaySfx("land", GlobalPosition, 2f, 0.75f);
	}

	// ------------------------------------------------------------ 表现

	private void SpawnTelegraph(BossMoveData move)
	{
		float length = move.Kind switch
		{
			BossMoveKind.Charge => move.ChargeDistance,
			BossMoveKind.ChainPull => move.Range,
			_ => move.Range,
		};

		float width = move.Kind switch
		{
			BossMoveKind.Sweep => Mathf.Max(2f, length * 1.1f),
			BossMoveKind.DoubleHook => Mathf.Max(2f, length * 1.1f),
			_ => 1.6f,
		};

		Vector3 forward = Flat(-GlobalTransform.Basis.Z);
		if (forward.LengthSquared() < 0.001f)
			forward = Vector3.Forward;

		Vector3 center = GlobalPosition + forward.Normalized() * (length * 0.5f);
		center.Y = 0.06f;

		var mesh = new BoxMesh { Size = new Vector3(width, 0.03f, length) };
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = new Color(1f, 0.32f, 0.1f, 0.3f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.28f, 0.08f),
			EmissionEnergyMultiplier = 1.6f,
		};
		mesh.Material = material;

		var node = new MeshInstance3D { Name = "BossTelegraph", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		var scene = GetTree().CurrentScene;
		if (scene == null)
			return;

		scene.AddChild(node);
		node.GlobalPosition = center;
		node.Rotation = new Vector3(0f, GlobalRotation.Y, 0f);
		FreeAfter(node, Mathf.Max(move.Windup, 0.2f));
	}

	private void SpawnDisc(Vector3 center, float radius, Color color, float duration)
	{
		var mesh = new CylinderMesh
		{
			TopRadius = radius,
			BottomRadius = radius,
			Height = 0.03f,
			RadialSegments = 28,
		};
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = new Color(1f, 0.3f, 0.1f),
			EmissionEnergyMultiplier = 1.2f,
		};
		mesh.Material = material;

		var node = new MeshInstance3D { Name = "RainTelegraph", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		var scene = GetTree().CurrentScene;
		if (scene == null)
			return;

		scene.AddChild(node);
		node.GlobalPosition = center;
		FreeAfter(node, Mathf.Max(duration, 0.2f));
	}

	private async void FreeAfter(Node3D node, float seconds)
	{
		await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
		if (IsInstanceValid(node))
			node.QueueFree();
	}

	private void PlaySfx(string id, Vector3 position, float volumeDb = 0f, float pitch = 1f)
	{
		SfxKind kind = id switch
		{
			"melee" => SfxKind.Melee,
			"alert" => SfxKind.Alert,
			"land" => SfxKind.Land,
			"kill" => SfxKind.Kill,
			_ => SfxKind.Hit,
		};
		AudioDirector.Instance?.PlayWorld(kind, position, volumeDb, pitch);
	}

	// ------------------------------------------------------------ 死亡

	private void BeginDeath()
	{
		if (_deathStarted)
			return;

		_deathStarted = true;
		IsDefeated = true;
		_state = BossState.Dead;
		Health = 0f;
		HealthChanged?.Invoke(Health, MaxHealth);
		SetCoreActive(false);
		CollisionLayer = 0;

		if (_hitboxes != null)
		{
			foreach (Node child in _hitboxes.GetChildren())
			{
				if (child is Area3D area)
					area.CollisionLayer = 0;
			}
		}

		if (_visual != null)
		{
			_visual.Rotation = new Vector3(-1.15f, 0f, 0.35f);
			_visual.Position = new Vector3(0f, -0.55f, 0f);
		}

		PlaySfx("kill", GlobalPosition, 3f, 0.7f);
		GetTree().GetFirstNodeInGroup("comic_overlay")?.Call("ShowWorld", "K.O.!",
			GlobalPosition + Vector3.Up * 1.4f, new Color(0.95f, 0.36f, 0.16f), 1.5f);

		_narrative ??= GetTree().GetFirstNodeInGroup("narrative_director") as NarrativeDirector;
		if (_narrative != null && _narrative.PlayBossDeath())
			_narrative.Cutscenes.Finished += OnDeathCutsceneFinished;
		else
			BeginDeathSequence();
	}

	private void OnDeathCutsceneFinished()
	{
		if (_narrative?.Cutscenes != null)
			_narrative.Cutscenes.Finished -= OnDeathCutsceneFinished;

		BeginDeathSequence();
	}

	private async void BeginDeathSequence()
	{
		ProcessMode = ProcessModeEnum.Always;

		float slowScale = Config?.DeathSlowMotionScale ?? 0.35f;
		float slowTime = Config?.DeathSlowMotionRealTime ?? 1.2f;

		CanvasLayer layer = new() { Name = "BossDeathBlackout", Layer = 18, ProcessMode = ProcessModeEnum.Always };
		ColorRect black = new()
		{
			Color = new Color(0f, 0f, 0f, 0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(black);
		GetTree().CurrentScene?.AddChild(layer);

		Engine.TimeScale = slowScale;
		black.Color = new Color(0f, 0f, 0f, 0.9f);

		await ToSignal(GetTree().CreateTimer(Mathf.Max(slowTime, 0.1f), true, false, true),
			SceneTreeTimer.SignalName.Timeout);

		Engine.TimeScale = 1.0;
		black.Color = new Color(0f, 0f, 0f, 0.45f);

		await ToSignal(GetTree().CreateTimer(0.25f, true, false, true),
			SceneTreeTimer.SignalName.Timeout);

		layer.QueueFree();
		if (_visual != null)
			_visual.Visible = false;

		Defeated?.Invoke();
	}

	// ------------------------------------------------------------ 工具

	private void EnsurePlayer()
	{
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_playerHealth = _player?.GetNodeOrNull<PlayerHealth>("Health");
	}

	private void DamagePlayer(float damage)
	{
		if (damage <= 0f || _playerHealth == null || _playerHealth.IsDead)
			return;

		_playerHealth.TakeDamage(damage, DeathCause.Gunshot, GlobalPosition);
	}

	private void ApplyGravity(float dt)
	{
		if (!IsOnFloor())
			Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * dt, Velocity.Z);
		else if (Velocity.Y < 0f)
			Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
	}

	private void StopHorizontal(float dt)
	{
		float step = 20f * Mathf.Max(dt, 0.016f);
		Velocity = new Vector3(Mathf.MoveToward(Velocity.X, 0f, step), Velocity.Y,
			Mathf.MoveToward(Velocity.Z, 0f, step));
	}

	private void FaceTowards(Vector3 point, float dt)
	{
		Vector3 delta = point - GlobalPosition;
		delta.Y = 0f;
		if (delta.LengthSquared() < 0.0001f)
			return;

		float targetYaw = Mathf.Atan2(-delta.X, -delta.Z);
		float step = Mathf.DegToRad(Config?.TurnSpeedDegrees ?? 220f) * Mathf.Max(dt, 0.016f);
		float yaw = Rotation.Y;
		float diff = Mathf.Wrap(targetYaw - yaw, -Mathf.Pi, Mathf.Pi);
		Rotation = new Vector3(0f, yaw + Mathf.Clamp(diff, -step, step), 0f);
	}

	private void ClampToArena()
	{
		if (Config == null)
			return;

		GlobalPosition = ClampPoint(GlobalPosition);
	}

	private Vector3 ClampPoint(Vector3 point)
	{
		if (Config == null)
			return point;

		Vector3 center = Config.ArenaCenter;
		float halfX = Mathf.Max(1f, Config.ArenaSize.X * 0.5f - 1.4f);
		float halfZ = Mathf.Max(1f, Config.ArenaSize.Y * 0.5f - 1.4f);

		return new Vector3(
			Mathf.Clamp(point.X, center.X - halfX, center.X + halfX),
			point.Y,
			Mathf.Clamp(point.Z, center.Z - halfZ, center.Z + halfZ));
	}

	private static Vector3 Flat(Vector3 value) => new(value.X, 0f, value.Z);

	private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

	private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
	{
		Vector3 ab = Flat(b - a);
		Vector3 ap = Flat(point - a);
		float lengthSq = ab.LengthSquared();
		if (lengthSq < 0.0001f)
			return FlatDistance(point, a);

		float t = Mathf.Clamp(ap.Dot(ab) / lengthSq, 0f, 1f);
		return FlatDistance(point, a + ab * t);
	}
}
