using Godot;

/// <summary>
/// 第一人称视角：鼠标偏航写进玩家身体，俯仰与落地下沉写进 Head。
/// 不做命中判定（设计文档 §9.3）。
/// </summary>
public partial class PlayerCamera : Node3D
{
	[Export] public float PitchLimitDegrees { get; set; } = 89f;

	/// <summary>后坐回正速度（度/秒），由武器在启动时按 PistolData 写入。</summary>
	public float RecoilRecovery { get; set; } = 14f;

	/// <summary>死亡或演出期间由外部关闭。</summary>
	public bool LookEnabled { get; set; } = true;

	private const float LandingDipDepth = 0.05f;
	private const float LandingDipRecoverTime = 0.15f;
	private const float DeathSinkDepth = 0.5f;
	private const float DeathRollDegrees = 8f;
	private const float DeathTransitionTime = 0.5f;

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

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_data = _player.Data;
		_camera = GetNode<Camera3D>("Camera3D");

		if (_data == null)
			GD.PushError("PlayerCamera: Player 节点没有配置 PlayerData。");
		else
			_camera.Fov = _data.DefaultFov;

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
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
		float pitch = Mathf.Clamp(_pitch + Mathf.DegToRad(_recoilPitch), -limit, limit);
		Rotation = new Vector3(pitch, Mathf.DegToRad(_recoilYaw), 0f);
	}

	private void UpdateAim()
	{
		if (_data == null)
			return;

		_camera.Fov = Mathf.Lerp(_data.DefaultFov, _data.AdsFov, _aimBlend);
	}

	private void UpdateCameraOffset(float dt)
	{
		if (_landingDip > 0f)
			_landingDip = Mathf.MoveToward(_landingDip, 0f,
				LandingDipDepth / LandingDipRecoverTime * dt);

		if (_deathActive && _deathBlend < 1f)
			_deathBlend = Mathf.MoveToward(_deathBlend, 1f, dt / DeathTransitionTime);

		_camera.Position = new Vector3(0f, -(_landingDip + DeathSinkDepth * _deathBlend), 0f);
		_camera.Rotation = new Vector3(0f, 0f, Mathf.DegToRad(-DeathRollDegrees) * _deathBlend);
	}
}
