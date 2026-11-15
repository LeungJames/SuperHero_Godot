using Godot;
using System;

/// <summary>移动状态。Normal 以外的状态由机动系统接管速度积分（设计文档 §5.1）。</summary>
public enum MoveState
{
	Normal,
	Slide,
	WallRun,
	Vault,
	Hang,
}

/// <summary>评级统计的机动动作类型（§6.2）。</summary>
public enum MobilityAction
{
	Slide,
	WallRun,
	Vault,
	Climb,
	Dash,
	Hook,
}

/// <summary>
/// 第一人称移动：自定义速度积分 + 土狼时间 + 跳跃缓冲；
/// 机动状态机：滑铲 / 蹬墙跑 / 翻越 / 边缘攀爬（设计文档 §5.1）。
/// 只负责移动与地面检测，不碰相机与武器（设计文档 §9.3）。
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	[Export] public PlayerData Data { get; set; }

	/// <summary>落下一步（脚步声的播放时机由 AudioDirector 决定）。</summary>
	public event Action Footstepped;

	/// <summary>进入一次机动动作（评级统计用；同一动作由订阅方去重）。</summary>
	public event Action<MobilityAction> MobilityPerformed;

	/// <summary>死亡或演出期间由外部关闭。</summary>
	public bool InputEnabled { get; set; } = true;

	/// <summary>ADS / 换弹时的移速倍率，由武器写入（设计文档 §3.1）。</summary>
	public float MoveSpeedMultiplier { get; set; } = 1f;

	/// <summary>
	/// 外部限速（m/s），用于藏尸等需要压低移动速度的交互（2.0 §3.5）。
	/// 默认不限制；与武器写入的 MoveSpeedMultiplier 叠加后再取上限。
	/// </summary>
	public float ExternalSpeedCap { get; set; } = float.MaxValue;

	public MoveState State => _state;
	public bool IsSliding => _state == MoveState.Slide;
	public bool IsWallRunning => _state == MoveState.WallRun;
	public bool IsHanging => _state == MoveState.Hang;

	/// <summary>仅探测关卡几何（World 层），不把敌人和可交互物算成墙。</summary>
	private const uint WorldMask = 1;

	/// <summary>蹬墙跑中断后仍算"蹬墙落地"的宽限时间（秒）。</summary>
	private const float WallRunLandingGrace = 1f;

	private PlayerCamera _camera;
	private CollisionShape3D _bodyShape;
	private CapsuleShape3D _capsule;
	private Node3D _head;
	private readonly Godot.Collections.Array<Rid> _rayExclude = new();

	private float _coyoteTimer;
	private float _jumpBufferTimer;
	private float _footstepTimer;

	private MoveState _state = MoveState.Normal;

	// 滑铲
	private Vector3 _slideDirection;
	private float _slideSpeed;
	private float _slideTimer;
	private bool _slideFromLanding;
	private float _crouchBlend;

	// 落地滑铲窗口 / 摔伤判定
	private float _slideWindow;
	private bool _slideWindowBoosted;
	private float _fallPeakY;
	private bool _wasGrounded = true;
	private float _wallRunGrace;

	/// <summary>本帧是否在蹬墙跑或滑铲状态下落地（供 PlayerHealth 减免摔伤）。</summary>
	public bool LandedWithMobility { get; private set; }

	// 蹬墙跑
	private Vector3 _wallNormal;
	private Vector3 _wallTangent;
	private float _wallRoll;
	private float _wallTimer;
	private float _mobilityCooldown;

	// 翻越 / 上翻共用
	private Vector3 _actionTarget;
	private Vector3 _actionMomentum;
	private float _actionTimer;
	private float _actionDuration;

	// 边缘悬挂
	private Vector3 _hangNormal;
	private float _hangLedgeY;

	private float _standCapsuleHeight;
	private float _standEyeHeight;

	// 天赋机动（M2 / M3 / M4 / M5）
	private const float DashDuration = 0.25f;
	private bool _airJumpUsed;
	private float _dashBuffer;
	private float _dashTimer;
	private float _dashCooldown;
	private Vector3 _dashDirection;
	private float _grappleBuffer;
	private float _grappleTimer;
	private float _grappleCooldown;
	private Vector3 _grappleTarget;
	private float _edgeSpeedTimer;

	/// <summary>M5 边缘加速的移速倍率。</summary>
	private float EdgeSpeedScale => _edgeSpeedTimer > 0f ? 1f + TalentEffects.EdgeSpeedBonus : 1f;
	private HudController _hud;

	public override void _Ready()
	{
		_camera = GetNode<PlayerCamera>("Head");
		_head = GetNode<Node3D>("Head");
		_bodyShape = GetNodeOrNull<CollisionShape3D>("Body");
		if (_bodyShape?.Shape is CapsuleShape3D capsule)
		{
			_capsule = capsule;
			_standCapsuleHeight = capsule.Height;
		}
		_standEyeHeight = _head.Position.Y;
		_rayExclude.Add(GetRid());
		AddToGroup("player");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// 在输入回调里记录跳跃，避免高帧率下物理帧轮询漏掉按键
		if (InputEnabled && @event.IsActionPressed("jump"))
			_jumpBufferTimer = Data.JumpBuffer;
		if (InputEnabled && @event.IsActionPressed("dash"))
			_dashBuffer = 0.15f;
		if (InputEnabled && @event.IsActionPressed("grapple"))
			_grappleBuffer = 0.15f;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		_jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - dt);
		_mobilityCooldown = Mathf.Max(0f, _mobilityCooldown - dt);
		_slideWindow = Mathf.Max(0f, _slideWindow - dt);
		_wallRunGrace = Mathf.Max(0f, _wallRunGrace - dt);
		_dashBuffer = Mathf.Max(0f, _dashBuffer - dt);
		_dashCooldown = Mathf.Max(0f, _dashCooldown - dt);
		_grappleBuffer = Mathf.Max(0f, _grappleBuffer - dt);
		_grappleCooldown = Mathf.Max(0f, _grappleCooldown - dt);
		_edgeSpeedTimer = Mathf.Max(0f, _edgeSpeedTimer - dt);
		if (IsOnFloor())
			_airJumpUsed = false;
		LandedWithMobility = false;

		if (!InputEnabled && _state != MoveState.Normal)
			ExitToNormal(Velocity);

		MoveState stateAtFrameStart = _state;

		switch (_state)
		{
			case MoveState.Slide:
				UpdateSlide(dt);
				break;
			case MoveState.WallRun:
				UpdateWallRun(dt);
				break;
			case MoveState.Vault:
				UpdateVault(dt);
				break;
			case MoveState.Hang:
				UpdateHang(dt);
				break;
			default:
				UpdateNormal(dt);
				break;
		}

		UpdateLandingState(stateAtFrameStart);

		UpdateCrouchPose(dt);
		_camera?.SetRoll(_state == MoveState.WallRun ? _wallRoll : 0f);
		UpdateFootstepNoise(_state == MoveState.Normal && IsOnFloor(), dt);
	}

	/// <summary>
	/// 记录落地时机：蹬墙跑 / 滑铲落地算身法落地（摔伤减半），
	/// 并从高处落地或身法落地后开出一小段可接滑铲的窗口。
	/// </summary>
	private void UpdateLandingState(MoveState stateAtFrameStart)
	{
		bool grounded = IsOnFloor();
		float y = GlobalPosition.Y;

		if (!grounded)
		{
			_fallPeakY = Mathf.Max(_fallPeakY, y);
			if (stateAtFrameStart == MoveState.WallRun)
				_wallRunGrace = WallRunLandingGrace;
		}
		else if (!_wasGrounded)
		{
			float fall = Mathf.Max(0f, _fallPeakY - y);
			// 身法落地：蹬墙跑 / 滑铲中落地，或落地瞬间按住蹲伏直接接滑铲
			bool mobility = stateAtFrameStart == MoveState.WallRun
				|| stateAtFrameStart == MoveState.Slide
				|| Input.IsActionPressed("crouch");
			LandedWithMobility = mobility;

			if (mobility || fall >= Data.SlideLandingMinFallHeight)
			{
				_slideWindow = Data.SlideLandingWindow;
				_slideWindowBoosted = _wallRunGrace > 0f;
			}

			_wallRunGrace = 0f;
		}

		if (grounded)
			_fallPeakY = y;

		_wasGrounded = grounded;
	}

	// ---------------------------------------------------------------- 普通移动

	private void UpdateNormal(float dt)
	{
		if (TryUpdateTalentMotion(dt))
			return;

		bool grounded = IsOnFloor();
		_coyoteTimer = grounded ? Data.CoyoteTime : Mathf.Max(0f, _coyoteTimer - dt);

		Vector3 wishDirection = GetWishDirection();
		float targetSpeed = InputEnabled ? GetTargetSpeed() * MoveSpeedMultiplier * EdgeSpeedScale : 0f;

		Vector3 velocity = Velocity;

		if (grounded)
		{
			velocity = wishDirection.LengthSquared() > 0.0001f
				? Accelerate(velocity, wishDirection * targetSpeed, Data.GroundAcceleration, dt)
				: ApplyFriction(velocity, dt);
		}
		else
		{
			velocity = ApplyAirControl(velocity, wishDirection, targetSpeed, dt);
		}

		if (InputEnabled && TryStartMobility(velocity, grounded))
		{
			MoveAndSlide();
			return;
		}

		if (TryConsumeJump())
		{
			velocity.Y = Data.JumpVelocity;
			_airJumpUsed = false;
		}
		else if (TalentEffects.DoubleJump && !grounded && !_airJumpUsed && _jumpBufferTimer > 0f)
		{
			// M3 二段跳：空中再消耗一次跳跃，高度按首次跳跃的配置比例换算
			_jumpBufferTimer = 0f;
			_airJumpUsed = true;
			velocity.Y = Data.JumpVelocity * TalentEffects.DoubleJumpVelocityScale;
		}
		else
		{
			velocity.Y -= Data.Gravity * dt;
		}

		float impactSpeed = velocity.Y;
		Velocity = velocity;

		MoveAndSlide();

		if (!grounded && IsOnFloor() && _camera != null)
			_camera.LandingBump(impactSpeed);
	}

	/// <summary>
	/// 天赋机动：钩锁拉近、空中冲刺（M2 / M4）。返回 true 表示本帧已接管位移，
	/// 普通移动与重力不再参与。
	/// </summary>
	private bool TryUpdateTalentMotion(float dt)
	{
		if (_grappleTimer > 0f)
		{
			Vector3 delta = _grappleTarget - GlobalPosition;
			float distance = delta.Length();
			if (distance < 1.5f)
			{
				EndGrapple();
				return false;
			}

			Velocity = delta / distance * 20f;
			MoveAndSlide();
			_grappleTimer -= dt;
			if (_grappleTimer <= 0f)
				EndGrapple();
			return true;
		}

		if (_dashTimer > 0f)
		{
			float speed = TalentEffects.AirDashDistance / DashDuration;
			Velocity = new Vector3(_dashDirection.X * speed, 0f, _dashDirection.Z * speed);
			MoveAndSlide();
			_dashTimer -= dt;
			return true;
		}

		if (_grappleBuffer > 0f && !IsOnFloor() && TalentEffects.Grapple &&
			_grappleCooldown <= 0f && TryStartGrapple())
			return true;

		if (_dashBuffer > 0f && !IsOnFloor() && TalentEffects.AirDash && _dashCooldown <= 0f)
		{
			_dashBuffer = 0f;
			_coyoteTimer = 0f;
			Vector3 direction = Flat(GetWishDirection());
			_dashDirection = direction.LengthSquared() > 0.01f
				? direction.Normalized()
				: Flat(-GlobalTransform.Basis.Z).Normalized();
			_dashTimer = DashDuration;
			_dashCooldown = TalentEffects.AirDashCooldown;
			MobilityPerformed?.Invoke(MobilityAction.Dash);
			return true;
		}

		return false;
	}

	/// <summary>钩锁只抓带 <c>hook_anchor</c> 组的标记：视线前方、40m 内且中间无墙体遮挡。</summary>
	private bool TryStartGrapple()
	{
		Camera3D camera = GetViewport()?.GetCamera3D();
		if (camera == null)
			return false;

		Vector3 origin = camera.GlobalPosition;
		Vector3 forward = -camera.GlobalTransform.Basis.Z;
		Node3D best = null;
		float bestScore = float.MinValue;

		foreach (Node node in GetTree().GetNodesInGroup("hook_anchor"))
		{
			if (node is not Node3D anchor || !anchor.IsVisibleInTree())
				continue;

			Vector3 to = anchor.GlobalPosition - origin;
			float distance = to.Length();
			if (distance < 2f || distance > TalentEffects.GrappleRange)
				continue;

			float facing = forward.Dot(to / distance);
			if (facing < 0.55f)
				continue;

			// 锚点与玩家之间不能有遮挡；细格栅没有独立层，按普通墙体处理
			PhysicsRayQueryParameters3D sightQuery =
				PhysicsRayQueryParameters3D.Create(origin, anchor.GlobalPosition, WorldMask);
			sightQuery.Exclude = _rayExclude;
			if (GetWorld3D().DirectSpaceState.IntersectRay(sightQuery).Count > 0)
				continue;

			float score = facing * 10f - distance * 0.05f;
			if (score <= bestScore)
				continue;

			bestScore = score;
			best = anchor;
		}

		if (best == null)
			return false;

		_grappleBuffer = 0f;
		_grappleTarget = best.GlobalPosition + Vector3.Up * 0.4f;
		_grappleTimer = 1.4f;
		_grappleCooldown = TalentEffects.GrappleCooldown;
		MobilityPerformed?.Invoke(MobilityAction.Hook);
		return true;
	}

	private void EndGrapple()
	{
		_grappleTimer = 0f;
		Velocity = Flat(Velocity) * 0.35f + Vector3.Up * 1f;
	}

	/// <summary>静步不发声；走路 / 冲刺按速度间隔广播脚步声（设计文档 §3.1、§5.4）。</summary>
	private void UpdateFootstepNoise(bool grounded, float dt)
	{
		Vector3 horizontal = new(Velocity.X, 0f, Velocity.Z);
		float speed = horizontal.Length();

		if (!grounded || speed < 1f || Input.IsActionPressed("walk_slow"))
		{
			_footstepTimer = 0f;
			return;
		}

		_footstepTimer -= dt;
		if (_footstepTimer > 0f)
			return;

		_footstepTimer = Mathf.Lerp(0.5f, 0.32f, Mathf.Clamp(speed / Data.SprintSpeed, 0f, 1f));

		// 静步已在上面直接返回（不发声）；走路与冲刺用不同听觉半径（2.0 §3.4）
		NoiseKind kind = speed >= Data.SprintSpeed * 0.9f
			? NoiseKind.FootstepSprint
			: NoiseKind.FootstepWalk;
		NoiseEmitter.Emit(GlobalPosition, kind, TalentEffects.FootstepRangeScale);
		Footstepped?.Invoke();
	}

	private bool TryConsumeJump()
	{
		if (_jumpBufferTimer <= 0f || _coyoteTimer <= 0f)
			return false;

		_jumpBufferTimer = 0f;
		_coyoteTimer = 0f;
		return true;
	}

	private float GetTargetSpeed()
	{
		float speed = Data.WalkSpeed;

		if (Input.IsActionPressed("walk_slow"))
			speed = TalentEffects.SlowWalkSpeed > 0f ? TalentEffects.SlowWalkSpeed : Data.SlowWalkSpeed;
		else if (Input.IsActionPressed("sprint"))
			speed = Data.SprintSpeed;

		return Mathf.Min(speed, ExternalSpeedCap);
	}

	private Vector3 GetWishDirection()
	{
		if (!InputEnabled)
			return Vector3.Zero;

		Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		Vector3 wish = Basis * new Vector3(input.X, 0f, input.Y);
		if (wish.LengthSquared() > 1f)
			wish = wish.Normalized();
		return wish;
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

	private static float HorizontalSpeed(Vector3 v) => new Vector2(v.X, v.Z).Length();

	private Vector3 Accelerate(Vector3 velocity, Vector3 targetVelocity, float acceleration, float dt)
	{
		Vector3 horizontal = new(velocity.X, 0f, velocity.Z);
		horizontal = horizontal.MoveToward(new Vector3(targetVelocity.X, 0f, targetVelocity.Z), acceleration * dt);
		return new Vector3(horizontal.X, velocity.Y, horizontal.Z);
	}

	private Vector3 ApplyFriction(Vector3 velocity, float dt)
	{
		Vector3 horizontal = new(velocity.X, 0f, velocity.Z);
		horizontal = horizontal.MoveToward(Vector3.Zero, Data.GroundFriction * dt);
		return new Vector3(horizontal.X, velocity.Y, horizontal.Z);
	}

	/// <summary>空中只沿移动意图加速、不做减速，保留起跳惯性（设计文档 §3.3）。</summary>
	private Vector3 ApplyAirControl(Vector3 velocity, Vector3 wishDirection, float targetSpeed, float dt)
	{
		if (wishDirection.LengthSquared() <= 0.0001f)
			return velocity;

		Vector3 horizontal = new(velocity.X, 0f, velocity.Z);
		float speedAlongWish = horizontal.Dot(wishDirection);
		float addSpeed = targetSpeed - speedAlongWish;
		if (addSpeed <= 0f)
			return velocity;

		horizontal += wishDirection * Mathf.Min(Data.AirAcceleration * dt, addSpeed);
		return new Vector3(horizontal.X, velocity.Y, horizontal.Z);
	}

	// ---------------------------------------------------------------- 状态入口

	/// <summary>在普通状态下判断是否切进某个机动状态；返回 true 表示本帧已切走。</summary>
	private bool TryStartMobility(Vector3 velocity, bool grounded)
	{
		if (grounded)
		{
			bool sprintSlide = Input.IsActionPressed("sprint") && Input.IsActionPressed("crouch")
				&& HorizontalSpeed(velocity) >= Data.SlideMinSpeed;

			// 落地滑铲窗口：从高处 / 蹬墙跑落地后短时间内按 C 直接接滑铲
			bool landingSlide = _slideWindow > 0f && Input.IsActionPressed("crouch");

			if (sprintSlide || landingSlide)
			{
				EnterSlide(velocity, landingSlide && _slideWindowBoosted, landingSlide);
				return true;
			}

			if (TryEnterVault(velocity))
				return true;

			return false;
		}

		if (_mobilityCooldown > 0f)
			return false;

		if (velocity.Y < 0f && TryEnterLedgeGrab())
			return true;

		return HorizontalSpeed(velocity) >= Data.WallRunMinSpeed && TryEnterWallRun(velocity);
	}

	// ---------------------------------------------------------------- 滑铲

	/// <summary>
	/// 进入滑铲。落地滑铲会用冲刺速度兜底，保证从静止落地也能滑出去；
	/// <paramref name="wallRunBoost"/> 为真时再叠加蹬墙落地的额外初速。
	/// </summary>
	private void EnterSlide(Vector3 velocity, bool wallRunBoost = false, bool landingSlide = false)
	{
		Vector3 horizontal = Flat(velocity);
		_slideDirection = horizontal.LengthSquared() > 0.01f
			? horizontal.Normalized()
			: Flat(-GlobalTransform.Basis.Z).Normalized();

		float baseSpeed = Mathf.Max(horizontal.Length(), Data.SprintSpeed);
		float boost = Data.SlideBoost * (wallRunBoost ? Data.SlideLandingBoost : 1f);
		_slideSpeed = baseSpeed * boost;

		_slideTimer = 0f;
		_slideWindow = 0f;
		_slideFromLanding = landingSlide;
		_state = MoveState.Slide;
		Velocity = new Vector3(_slideDirection.X * _slideSpeed, velocity.Y, _slideDirection.Z * _slideSpeed);
		MobilityPerformed?.Invoke(MobilityAction.Slide);
	}

	private void UpdateSlide(float dt)
	{
		// 滑铲可以被跳跃取消：立刻起身起跳，保留水平动量（不超过正常起跳）
		if (TrySlideJump())
		{
			MoveAndSlide();
			return;
		}

		_slideTimer += dt;
		_slideSpeed = Mathf.MoveToward(_slideSpeed, 0f, Data.SlideFriction * dt);

		// 允许在滑行中缓慢转向，保持"冲刺中下蹲"的手感
		Vector3 wish = Flat(GetWishDirection());
		if (wish.LengthSquared() > 0.01f)
		{
			wish = wish.Normalized();
			float angle = _slideDirection.AngleTo(wish);
			float maxAngle = Mathf.DegToRad(Data.SlideSteerDegrees) * dt;
			float blend = angle > 0.0001f ? Mathf.Min(1f, maxAngle / angle) : 1f;
			_slideDirection = _slideDirection.Lerp(wish, blend).Normalized();
		}

		float y = Mathf.Min(Velocity.Y, 0f) - Data.Gravity * dt;
		Velocity = new Vector3(_slideDirection.X * _slideSpeed, y, _slideDirection.Z * _slideSpeed);
		MoveAndSlide();

		// 落地滑铲是一按到底的"救场动作"，不因松开蹲伏而提前断掉
		bool released = Input.IsActionJustReleased("crouch") && !_slideFromLanding && _slideTimer >= 0.2f;
		bool tooSlow = _slideSpeed <= Data.SlideEndSpeed;
		if (released || tooSlow || _slideTimer >= Data.SlideMaxTime * TalentEffects.SlideDistanceScale || !IsOnFloor())
			ExitToNormal(Velocity);
	}

	// ---------------------------------------------------------------- 蹬墙跑

	/// <summary>
	/// 滑铲中按下跳跃：立即恢复站立胶囊并起跳，水平动量按滑铲当前速度保留，
	/// 但不超过正常起跳的水平速度；头顶净空不足时取消这次起跳并提示，滑铲继续。
	/// </summary>
	private bool TrySlideJump()
	{
		if (_jumpBufferTimer <= 0f || _coyoteTimer <= 0f)
			return false;

		if (HasCeilingAbove())
		{
			_jumpBufferTimer = 0f;
			_hud ??= GetTree().GetFirstNodeInGroup("hud") as HudController;
			_hud?.ShowMessage("头顶空间不足，无法起跳", new Color(0.96f, 0.76f, 0.28f));
			return false;
		}

		Vector3 horizontal = Flat(Velocity);
		Vector3 direction = horizontal.LengthSquared() > 0.0001f
			? horizontal.Normalized()
			: _slideDirection;
		float speed = Mathf.Min(
			HorizontalSpeed(Velocity),
			Data.SprintSpeed * MoveSpeedMultiplier);

		// 消耗跳跃：起跳后只有一次空中跳跃机会（本项目没有二段跳，coyote 归零即锁死）
		_jumpBufferTimer = 0f;
		_coyoteTimer = 0f;
		_crouchBlend = 0f;
		ExitToNormal(new Vector3(direction.X * speed, Data.JumpVelocity, direction.Z * speed));
		_camera?.SetSlideBlend(0f);
		return true;
	}

	private bool TryEnterWallRun(Vector3 velocity)
	{
		Vector3 dir = Flat(velocity);
		if (dir.LengthSquared() < 0.0001f)
			return false;
		dir = dir.Normalized();

		if (!ProbeWall(dir, out Vector3 normal))
			return false;

		Vector3 tangent = Vector3.Up.Cross(normal).Normalized();
		if (tangent.Dot(dir) < 0f)
			tangent = -tangent;

		_wallNormal = normal;
		_wallTangent = tangent;
		_wallTimer = 0f;

		// 墙在右手边时相机顺势朝右倒，反之亦然（右向按跑动方向算，不看身体朝向）
		Vector3 right = tangent.Cross(Vector3.Up);
		float side = Mathf.Sign(normal.Dot(right));
		_wallRoll = -side * Data.WallRunCameraRoll;

		Vector3 along = tangent * Mathf.Max(HorizontalSpeed(velocity), Data.WallRunMinSpeed);
		Velocity = new Vector3(along.X, Mathf.Max(velocity.Y, 0f), along.Z);
		_state = MoveState.WallRun;
		MobilityPerformed?.Invoke(MobilityAction.WallRun);
		return true;
	}

	private void UpdateWallRun(float dt)
	{
		_wallTimer += dt;

		Vector3 probe = GlobalPosition + Vector3.Up * Data.LedgeProbeHeight;
		bool stillOnWall = CastRay(probe, probe - _wallNormal * (Data.WallRunProbeDistance + 0.15f), out _, out Vector3 normal);
		if (!stillOnWall || Mathf.Abs(normal.Y) > 0.35f)
		{
			ExitToNormal(Velocity, true);
			return;
		}

		if (_jumpBufferTimer > 0f)
		{
			// 蹬墙跑不受土狼时间限制：贴墙期间随时可以蹬墙起跳
			_jumpBufferTimer = 0f;
			_coyoteTimer = 0f;

			float speed = Mathf.Max(HorizontalSpeed(Velocity), Data.WallRunMinSpeed);
			Velocity = _wallTangent * speed
				+ _wallNormal * Data.WallRunJumpOut
				+ Vector3.Up * Data.WallRunJumpUp;
			ExitToNormal(Velocity, true);
			MoveAndSlide();
			return;
		}

		float down = Mathf.Max(Velocity.Y - Data.WallRunGravity * TalentEffects.WallRunFallScale * dt, -Data.WallRunMaxFall);
		float alongSpeed = Mathf.Max(HorizontalSpeed(Velocity), Data.WallRunMinSpeed);
		Vector3 run = _wallTangent * alongSpeed - _wallNormal * Data.WallRunStickSpeed;
		Velocity = new Vector3(run.X, down, run.Z);
		MoveAndSlide();

		if (_wallTimer >= Data.WallRunMaxTime + TalentEffects.WallRunMaxTimeAdd || IsOnFloor())
			ExitToNormal(Velocity, true);
	}

	// ---------------------------------------------------------------- 翻越

	private bool TryEnterVault(Vector3 velocity)
	{
		// 按键翻越：跳跃落在矮障碍前；自动翻越：冲刺撞上矮障碍
		bool wanted = _jumpBufferTimer > 0f
			|| (Input.IsActionPressed("sprint") && HorizontalSpeed(velocity) > Data.WalkSpeed * 0.9f);
		if (!wanted)
			return false;

		Vector3 forward = ProbeForward(velocity);
		Vector3 chest = GlobalPosition + Vector3.Up * 0.75f;
		if (!CastRay(chest, chest + forward * Data.VaultReach, out Vector3 wallPoint, out Vector3 wallNormal))
			return false;
		if (Mathf.Abs(wallNormal.Y) > 0.3f)
			return false;

		Vector3 topProbe = wallPoint + forward * 0.3f + Vector3.Up * (Data.VaultMaxHeight + 0.3f);
		if (!CastRay(topProbe, topProbe + Vector3.Down * (Data.VaultMaxHeight + 0.6f), out Vector3 topPoint, out Vector3 topNormal))
			return false;
		if (topNormal.Y < 0.7f)
			return false;

		float height = topPoint.Y - GlobalPosition.Y;
		if (height < Data.VaultMinHeight || height > Data.VaultMaxHeight)
			return false;

		Vector3 target = topPoint + forward * Data.VaultLandingOffset;
		target.Y = topPoint.Y + 0.05f;
		BeginAction(target, Data.VaultDuration, Flat(velocity));
		MobilityPerformed?.Invoke(MobilityAction.Vault);
		return true;
	}

	// ---------------------------------------------------------------- 边缘攀爬

	private bool TryEnterLedgeGrab()
	{
		Vector3 forward = ProbeForward(Velocity);
		Vector3 chest = GlobalPosition + Vector3.Up * Data.LedgeProbeHeight;
		if (!CastRay(chest, chest + forward * Data.LedgeGrabReach, out Vector3 wallPoint, out Vector3 wallNormal))
			return false;
		if (Mathf.Abs(wallNormal.Y) > 0.3f)
			return false;

		// 从墙面往墙内挪一点再往下打，才能落在平台顶面上（不挪就会擦着边缘落空）
		Vector3 from = wallPoint + forward * 0.2f + Vector3.Up * 1.1f;
		if (!CastRay(from, from + Vector3.Down * 1.6f, out Vector3 topPoint, out Vector3 topNormal))
			return false;
		if (topNormal.Y < 0.7f)
			return false;

		float height = topPoint.Y - GlobalPosition.Y;
		if (height < Data.LedgeMinHeight || height > Data.LedgeMaxHeight)
			return false;

		_hangNormal = wallNormal;
		_hangLedgeY = topPoint.Y;
		_state = MoveState.Hang;
		Velocity = Vector3.Zero;
		MobilityPerformed?.Invoke(MobilityAction.Climb);
		return true;
	}

	private void UpdateHang(float dt)
	{
		// 平滑对齐到悬挂高度并贴住墙面（用速度而不是直接改坐标，避免穿墙）
		float targetY = _hangLedgeY - Data.LedgeHangOffset;
		float y = (targetY - GlobalPosition.Y) * Data.LedgeHangStiffness;
		Velocity = new Vector3(-_hangNormal.X * 0.6f, y, -_hangNormal.Z * 0.6f);
		MoveAndSlide();

		if (_jumpBufferTimer > 0f)
		{
			// 悬挂中按跳跃直接上翻，不受土狼时间限制
			_jumpBufferTimer = 0f;
			Vector3 target = GlobalPosition - _hangNormal * Data.VaultLandingOffset;
			target.Y = _hangLedgeY + 0.06f;
			Vector3 momentum = Flat(-_hangNormal) * 2f;
			BeginAction(target, Data.LedgeClimbTime, momentum);
			return;
		}

		if (Input.IsActionJustPressed("crouch") || Input.IsActionJustPressed("move_back"))
		{
			Velocity = new Vector3(_hangNormal.X * 1.5f, 0f, _hangNormal.Z * 1.5f);
			ExitToNormal(Velocity, true);
		}
	}

	// ---------------------------------------------------------------- 位移动作（翻越 / 上翻）

	private void BeginAction(Vector3 target, float duration, Vector3 momentum)
	{
		_jumpBufferTimer = 0f;
		_actionTarget = target;
		_actionMomentum = momentum;
		_actionDuration = Mathf.Max(duration, 0.05f);
		_actionTimer = 0f;
		_state = MoveState.Vault;
		Velocity = Vector3.Zero;
	}

	private void UpdateVault(float dt)
	{
		_actionTimer += dt;

		Vector3 delta = _actionTarget - GlobalPosition;
		float remaining = Mathf.Max(_actionDuration - _actionTimer, 0.02f);
		Velocity = delta / remaining;
		MoveAndSlide();

		if (_actionTimer >= _actionDuration || delta.LengthSquared() < 0.0025f)
		{
			Vector3 dir = Flat(_actionMomentum);
			dir = dir.LengthSquared() > 0.01f
				? dir.Normalized()
				: Flat(-GlobalTransform.Basis.Z).Normalized();
			float speed = Mathf.Max(_actionMomentum.Length(), Data.VaultExitSpeed);
			Velocity = new Vector3(dir.X * speed, 0f, dir.Z * speed);
			ExitToNormal(Velocity);
			_coyoteTimer = Data.CoyoteTime;

			// M5 边缘加速：翻越 / 攀爬上沿后短时间提速
			if (TalentEffects.EdgeSpeed)
				_edgeSpeedTimer = TalentEffects.EdgeSpeedDuration;
		}
	}

	private void ExitToNormal(Vector3 velocity, bool lockMobility = false)
	{
		_state = MoveState.Normal;
		Velocity = velocity;
		if (lockMobility)
			_mobilityCooldown = Mathf.Max(_mobilityCooldown, Data.WallRunCooldown);
		_actionTimer = _actionDuration;
	}

	// ---------------------------------------------------------------- 姿态与探测

	/// <summary>滑铲时压低胶囊与视点；起身前检查头顶净空，避免卡进低矮空间。</summary>
	private void UpdateCrouchPose(float dt)
	{
		float target = _state == MoveState.Slide ? 1f : 0f;
		float step = dt / Mathf.Max(Data.CrouchBlendTime, 0.001f);

		if (target < _crouchBlend && HasCeilingAbove())
			target = _crouchBlend;

		_crouchBlend = Mathf.MoveToward(_crouchBlend, target, step);
		_camera?.SetSlideBlend(_crouchBlend);

		if (_capsule == null)
			return;

		float height = Mathf.Lerp(_standCapsuleHeight, Data.SlideCapsuleHeight, _crouchBlend);
		_capsule.Height = height;
		_bodyShape.Position = new Vector3(0f, height * 0.5f, 0f);
		_head.Position = new Vector3(0f, Mathf.Lerp(_standEyeHeight, Data.SlideEyeHeight, _crouchBlend), 0f);
	}

	private bool HasCeilingAbove()
	{
		if (_crouchBlend <= 0.02f)
			return false;

		Vector3 from = GlobalPosition + Vector3.Up * (Data.SlideCapsuleHeight + 0.05f);
		float length = _standCapsuleHeight - Data.SlideCapsuleHeight + 0.05f;
		return CastRay(from, from + Vector3.Up * length, out _, out _);
	}

	/// <summary>
	/// 找可跑墙面：沿速度方向以及左右各 60°、90° 各打一条射线。
	/// 跑墙时速度基本平行于墙面，只往前探是探不到的。
	/// </summary>
	private bool ProbeWall(Vector3 dir, out Vector3 normal)
	{
		Vector3 origin = GlobalPosition + Vector3.Up * Data.LedgeProbeHeight;
		float reach = Data.WallRunProbeDistance + 0.05f;
		float[] angles = { 0f, Mathf.Pi / 3f, -Mathf.Pi / 3f, Mathf.Pi / 2f, -Mathf.Pi / 2f };

		foreach (float angle in angles)
		{
			Vector3 probe = dir.Rotated(Vector3.Up, angle);
			if (!CastRay(origin, origin + probe * reach, out _, out Vector3 hit))
				continue;
			if (Mathf.Abs(hit.Y) > 0.25f)
				continue;

			normal = hit;
			return true;
		}

		normal = Vector3.Up;
		return false;
	}

	private Vector3 ProbeForward(Vector3 velocity)	{
		Vector3 dir = Flat(velocity);
		if (dir.LengthSquared() > 0.25f)
			return dir.Normalized();

		Vector3 forward = Flat(-GlobalTransform.Basis.Z);
		return forward.LengthSquared() > 0.0001f ? forward.Normalized() : Vector3.Forward;
	}

	private bool CastRay(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal)
	{
		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to, WorldMask);
		query.Exclude = _rayExclude;
		Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);

		if (result.Count == 0)
		{
			point = Vector3.Zero;
			normal = Vector3.Up;
			return false;
		}

		point = (Vector3)result["position"];
		normal = (Vector3)result["normal"];
		return true;
	}
}
