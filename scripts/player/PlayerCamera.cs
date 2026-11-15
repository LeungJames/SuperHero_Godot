using Godot;

/// <summary>玩家视角模式。第一人称沿用视角手臂，第三人称显示全身并跟随。</summary>
public enum CameraMode
{
	FirstPerson,
	ThirdPerson,
}

/// <summary>
/// 视角控制：鼠标偏航写进玩家身体，俯仰与落地下沉写进 Head。
/// A 阶段新增第一 / 第三人称切换、第三人称跟随相机（带遮挡回拉）与外部临时接管接口。
/// 两种模式共用同一套 Head 朝向，因此准星与射击判定的视线方向完全一致；不做命中判定（设计文档 §9.3）。
/// </summary>
public partial class PlayerCamera : Node3D
{
	[Export] public float PitchLimitDegrees { get; set; } = 89f;

	/// <summary>第三人称时显示的全身模型根节点；第一人称隐藏，避免看到身体内部。</summary>
	[Export] public Node3D BodyModel { get; set; }

	/// <summary>第一人称视角手臂根节点；第三人称隐藏，避免手臂悬空穿帮。</summary>
	[Export] public Node3D ViewModelNode { get; set; }

	/// <summary>后坐回正速度（度/秒），由武器在启动时按 WeaponData 写入。</summary>
	public float RecoilRecovery { get; set; } = 14f;

	/// <summary>侧倾跟随速度（度/秒），蹬墙跑时由移动系统写入目标角。</summary>
	public float RollSpeed { get; set; } = 180f;

	/// <summary>死亡或演出期间由外部关闭。</summary>
	public bool LookEnabled { get; set; } = true;

	public CameraMode Mode => _mode;

	/// <summary>相机是否正被外部（如处决演出）临时接管。</summary>
	public bool IsTakenOver => _takeoverActive;

	/// <summary>当前是否能看到玩家全身（第三人称，或被演出临时打开）。</summary>
	public bool IsBodyVisible => _mode == CameraMode.ThirdPerson || _performanceBody;

	private const float LandingDipDepth = 0.05f;
	private const float LandingDipRecoverTime = 0.15f;
	private const float DeathSinkDepth = 0.5f;
	private const float DeathRollDegrees = 8f;
	private const float DeathTransitionTime = 0.5f;
	private const uint WorldMask = 1;

	private PlayerController _player;
	private PlayerData _data;
	private Camera3D _camera;
	private float _pitch;
	private float _landingDip;
	private float _recoilPitch;
	private float _recoilYaw;
	private float _aimBlend;
	private float _deathBlend;
	private bool _deathActive;
	private float _roll;
	private float _rollTarget;
	private float _slideBlend;
	private float _shakeAmplitude;
	private float _shakeTime;
	private float _shakeDuration;
	private float _adsFovOverride = -1f;

	private CameraMode _mode = CameraMode.FirstPerson;
	private float _modeBlend;
	private bool _performanceBody;

	private Node3D _takeoverAnchor;
	private Transform3D _takeoverWorld;
	private float _takeoverWeight;
	private float _takeoverBlendTime = 0.3f;
	private bool _takeoverActive;

	private readonly Godot.Collections.Array<Rid> _rayExclude = new();

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_data = _player.Data;
		_camera = GetNode<Camera3D>("Camera3D");

		if (ViewModelNode == null)
			ViewModelNode = GetNodeOrNull<Node3D>("Camera3D/ViewModel");
		if (BodyModel == null)
			BodyModel = _player.GetNodeOrNull<Node3D>("BodyModel");

		if (_data == null)
			GD.PushError("PlayerCamera: Player 节点没有配置 PlayerData。");
		else
			_camera.Fov = _data.DefaultFov;

		_rayExclude.Add(_player.GetRid());
		ApplyModeVisibility();

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("camera_mode"))
		{
			if (LookEnabled && _player.InputEnabled)
				ToggleMode();
			return;
		}

		if (@event is not InputEventMouseMotion motion)
			return;

		if (!LookEnabled || _data == null || Input.MouseMode != Input.MouseModeEnum.Captured)
			return;

		float sensitivity = Mathf.DegToRad(_data.MouseSensitivity) *
			Mathf.Lerp(1f, _data.AdsSensitivityScale, _aimBlend);
		_player.RotateY(-motion.Relative.X * sensitivity);

		float limit = Mathf.DegToRad(PitchLimitDegrees);
		_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sensitivity, -limit, limit);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		UpdateRecoil(dt);
		ApplyRotation();
		UpdateAim();
		UpdateCameraOffset(dt);
	}

	// ---------------------------------------------------------------- 视角模式

	public void ToggleMode()
	{
		SetMode(_mode == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson);
	}

	/// <summary>切换第一 / 第三人称；身体与视角手臂互斥显示，相机位置做短过渡。</summary>
	public void SetMode(CameraMode mode)
	{
		if (_mode == mode)
			return;

		_mode = mode;
		ApplyModeVisibility();
	}

	private void ApplyModeVisibility()
	{
		if (BodyModel != null)
			BodyModel.Visible = IsBodyVisible;
		if (ViewModelNode != null)
			ViewModelNode.Visible = !_performanceBody && _mode == CameraMode.FirstPerson;
	}

	/// <summary>
	/// 演出期间临时显示全身、隐藏视角手臂（C 阶段处决用）。
	/// 不改变当前视角模式，关闭后按模式自动恢复。
	/// </summary>
	public void SetPerformanceBody(bool visible)
	{
		if (_performanceBody == visible)
			return;

		_performanceBody = visible;
		ApplyModeVisibility();
	}

	// ---------------------------------------------------------------- 外部接管（处决演出用）

	/// <summary>把相机临时接管到世界空间的一个锚点（例如处决机位），过渡结束后稳定跟随该锚点。</summary>
	public void TakeOver(Node3D anchor, float blendTime = 0.3f)
	{
		_takeoverAnchor = anchor;
		_takeoverActive = true;
		_takeoverBlendTime = Mathf.Max(blendTime, 0.001f);
	}

	/// <summary>把相机临时接管到世界空间的一个固定变换（例如处决机位）。</summary>
	public void TakeOverTransform(Transform3D worldTransform, float blendTime = 0.3f)
	{
		_takeoverAnchor = null;
		_takeoverWorld = worldTransform;
		_takeoverActive = true;
		_takeoverBlendTime = Mathf.Max(blendTime, 0.001f);
	}

	/// <summary>交还相机，平滑过渡回当前视角模式。</summary>
	public void ReleaseTakeOver(float blendTime = 0.3f)
	{
		_takeoverAnchor = null;
		_takeoverActive = false;
		_takeoverBlendTime = Mathf.Max(blendTime, 0.001f);
	}

	// ---------------------------------------------------------------- 既有第一人称行为

	/// <summary>后坐只写相机偏移，不改玩家实际朝向（设计文档 §4.5）。</summary>
	public void AddRecoil(float pitchDegrees, float yawDegrees)
	{
		_recoilPitch += pitchDegrees;
		_recoilYaw += yawDegrees;
	}

	/// <summary>0 = 腰射，1 = 完全开镜，由武器按 0.15s 过渡驱动。</summary>
	public void SetAimBlend(float blend)
	{
		_aimBlend = Mathf.Clamp(blend, 0f, 1f);
	}

	/// <summary>重着地时相机轻微下沉，落地更干脆（设计文档 §3.3）。</summary>
	public void LandingBump(float verticalSpeed)
	{
		if (_data != null && verticalSpeed <= -_data.LandingImpactSpeed)
			_landingDip = LandingDipDepth;
	}

	/// <summary>屏幕侧倾（度），蹬墙跑时由移动系统设置，普通状态下传 0 回正。</summary>
	public void SetRoll(float degrees)
	{
		_rollTarget = degrees;
	}

	/// <summary>滑铲姿态权重 0~1：叠加前倾、侧倾与轻微广角，配合低视点做出压低感。</summary>
	public void SetSlideBlend(float blend)
	{
		_slideBlend = Mathf.Clamp(blend, 0f, 1f);
	}

	/// <summary>短促镜头抖动（度），用于换弹到位、重落地等冲击帧。</summary>
	public void AddShake(float degrees, float duration)
	{
		if (degrees <= 0f)
			return;

		_shakeAmplitude = degrees;
		_shakeDuration = Mathf.Max(duration, 0.01f);
		_shakeTime = _shakeDuration;
	}

	/// <summary>开镜 FOV 由武器覆盖（狙击枪做瞄准镜效果）；传 0 或负数恢复 PlayerData 的默认值。</summary>
	public void SetAdsFov(float fov)
	{
		_adsFovOverride = fov > 0f ? fov : -1f;
	}

	/// <summary>死亡沉镜：0.5m 下沉 + 侧倾 8°（设计文档 §3.6）。</summary>
	public void BeginDeath()
	{
		LookEnabled = false;
		_deathActive = true;
	}

	private void UpdateRecoil(float dt)
	{
		_recoilPitch = Mathf.MoveToward(_recoilPitch, 0f, RecoilRecovery * dt);
		_recoilYaw = Mathf.MoveToward(_recoilYaw, 0f, RecoilRecovery * dt);
	}

	private void ApplyRotation()
	{
		float limit = Mathf.DegToRad(PitchLimitDegrees);
		float slidePitch = _data != null ? Mathf.DegToRad(_data.SlideCameraPitch * _slideBlend) : 0f;
		float pitch = Mathf.Clamp(_pitch + Mathf.DegToRad(_recoilPitch) + slidePitch, -limit, limit);
		Rotation = new Vector3(pitch, Mathf.DegToRad(_recoilYaw), 0f);
	}

	private void UpdateAim()
	{
		if (_data == null)
			return;

		float aimFov = _adsFovOverride > 0f ? _adsFovOverride : _data.AdsFov;
		// 滑铲时略放宽 FOV 强化速度感；开镜时退回瞄准视野，不干扰精度判断
		float slideFov = _data.SlideFovBoost * _slideBlend * (1f - _aimBlend);
		_camera.Fov = Mathf.Lerp(_data.DefaultFov, aimFov, _aimBlend) + slideFov;
	}

	private void UpdateCameraOffset(float dt)
	{
		if (_landingDip > 0f)
			_landingDip = Mathf.MoveToward(_landingDip, 0f,
				LandingDipDepth / LandingDipRecoverTime * dt);

		if (_deathActive && _deathBlend < 1f)
			_deathBlend = Mathf.MoveToward(_deathBlend, 1f, dt / DeathTransitionTime);

		_roll = Mathf.MoveToward(_roll, _rollTarget, RollSpeed * dt);

		float blendTime = _data != null ? Mathf.Max(_data.CameraModeBlendTime, 0.001f) : 0.2f;
		_modeBlend = Mathf.MoveToward(_modeBlend, _mode == CameraMode.ThirdPerson ? 1f : 0f, dt / blendTime);

		Vector3 firstPerson = new(0f, -(_landingDip + DeathSinkDepth * _deathBlend), 0f);
		Vector3 thirdPerson = new(0f, 0f, 0f);
		if (_data != null)
		{
			// 角色视觉缩放后机位跟着收，保持原来的人体比例构图
			float scale = Mathf.Max(_data.ModelScale, 0.01f);
			thirdPerson = new Vector3(
				_data.ThirdPersonShoulderOffset * scale,
				_data.ThirdPersonHeightOffset * scale,
				_data.ThirdPersonDistance * scale);
		}

		Vector3 local = firstPerson.Lerp(thirdPerson, _modeBlend);
		if (_modeBlend > 0.001f && _data != null)
			local = PullInFromObstacles(local);

		_camera.Position = local;
		float slideRoll = _data != null ? _data.SlideCameraRoll * _slideBlend : 0f;

		float shake = 0f;
		if (_shakeTime > 0f)
		{
			_shakeTime = Mathf.Max(0f, _shakeTime - dt);
			float decay = _shakeTime / _shakeDuration;
			shake = _shakeAmplitude * decay * decay * Mathf.Sin(_shakeTime * 80f);
		}

		_camera.Rotation = new Vector3(
			Mathf.DegToRad(shake * 0.6f),
			Mathf.DegToRad(shake),
			Mathf.DegToRad(-DeathRollDegrees * _deathBlend + _roll + slideRoll + shake * 0.3f));

		UpdateTakeOver(dt);
	}

	/// <summary>第三人称相机被墙挡住时沿视线回拉，避免镜头穿进几何体。</summary>
	private Vector3 PullInFromObstacles(Vector3 local)
	{
		Vector3 desired = GlobalTransform * local;
		Vector3 pivot = GlobalPosition;
		Vector3 delta = desired - pivot;
		float distance = delta.Length();
		if (distance < 0.05f)
			return local;

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(pivot, desired, WorldMask);
		query.Exclude = _rayExclude;
		Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (hit.Count == 0)
			return local;

		Vector3 point = (Vector3)hit["position"];
		Vector3 direction = (point - pivot) / distance;
		float safeDistance = Mathf.Max(distance - _data.CameraCollisionRadius, 0.1f);
		Vector3 safe = pivot + direction * safeDistance;
		return GlobalTransform.AffineInverse() * safe;
	}

	/// <summary>临时接管：把相机混合到外部机位；释放后按 setter 的过渡时间回到正常视角。</summary>
	private void UpdateTakeOver(float dt)
	{
		float target = _takeoverActive ? 1f : 0f;
		_takeoverWeight = Mathf.MoveToward(_takeoverWeight, target, dt / _takeoverBlendTime);
		if (_takeoverWeight <= 0.0001f)
			return;

		Transform3D world = _takeoverAnchor != null ? _takeoverAnchor.GlobalTransform : _takeoverWorld;
		Transform3D localTarget = GlobalTransform.AffineInverse() * world;
		float weight = Mathf.SmoothStep(0f, 1f, _takeoverWeight);

		// InterpolateWith 要求基正交归一，父节点带缩放时会断言；这里显式做位置线性插值与旋转球面插值。
		Transform3D current = _camera.Transform;
		Quaternion from = current.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
		Quaternion to = localTarget.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
		Basis basis = new Basis(from.Slerp(to, weight));
		_camera.Transform = new Transform3D(basis, current.Origin.Lerp(localTarget.Origin, weight));
	}
}
