using Godot;

/// <summary>
/// 第一人称移动：自定义速度积分 + 土狼时间 + 跳跃缓冲。
/// 只负责移动与地面检测，不碰相机与武器（设计文档 §9.3）。
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	[Export] public PlayerData Data { get; set; }

	/// <summary>死亡或演出期间由外部关闭。</summary>
	public bool InputEnabled { get; set; } = true;

	/// <summary>ADS / 换弹时的移速倍率，由武器写入（设计文档 §3.1）。</summary>
	public float MoveSpeedMultiplier { get; set; } = 1f;

	private PlayerCamera _camera;
	private float _coyoteTimer;
	private float _jumpBufferTimer;

	public override void _Ready()
	{
		_camera = GetNode<PlayerCamera>("Head");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// 在输入回调里记录跳跃，避免高帧率下物理帧轮询漏掉按键
		if (InputEnabled && @event.IsActionPressed("jump"))
			_jumpBufferTimer = Data.JumpBuffer;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		bool grounded = IsOnFloor();
		UpdateJumpTimers(grounded, dt);

		Vector3 velocity = Velocity;

		Vector3 wishDirection = Vector3.Zero;
		if (InputEnabled)
		{
			Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
			wishDirection = Basis * new Vector3(input.X, 0f, input.Y);
			if (wishDirection.LengthSquared() > 1f)
				wishDirection = wishDirection.Normalized();
		}

		float targetSpeed = InputEnabled ? GetTargetSpeed() * MoveSpeedMultiplier : 0f;

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

		if (TryConsumeJump())
		{
			velocity.Y = Data.JumpVelocity;
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

	private void UpdateJumpTimers(bool grounded, float dt)
	{
		_coyoteTimer = grounded ? Data.CoyoteTime : Mathf.Max(0f, _coyoteTimer - dt);
		_jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - dt);
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
		if (Input.IsActionPressed("walk_slow"))
			return Data.SlowWalkSpeed;

		if (Input.IsActionPressed("sprint"))
			return Data.SprintSpeed;

		return Data.WalkSpeed;
	}

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
}
