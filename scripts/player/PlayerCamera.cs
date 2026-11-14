using Godot;

/// <summary>
/// 第一人称视角：鼠标偏航写进玩家身体，俯仰与落地下沉写进 Head。
/// 不做命中判定（设计文档 §9.3）。
/// </summary>
public partial class PlayerCamera : Node3D
{
	[Export] public float PitchLimitDegrees { get; set; } = 89f;

	/// <summary>死亡或演出期间由外部关闭。</summary>
	public bool LookEnabled { get; set; } = true;

	private const float LandingDipDepth = 0.05f;
	private const float LandingDipRecoverTime = 0.15f;

	private PlayerController _player;
	private PlayerData _data;
	private Camera3D _camera;
	private float _pitch;
	private float _landingDip;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_data = _player.Data;
		_camera = GetNode<Camera3D>("Camera3D");

		if (_data == null)
			GD.PushError("PlayerCamera: Player 节点没有配置 PlayerData。");

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion)
		{
			if (!LookEnabled || _data == null || Input.MouseMode != Input.MouseModeEnum.Captured)
				return;

			float sensitivity = Mathf.DegToRad(_data.MouseSensitivity);
			_player.RotateY(-motion.Relative.X * sensitivity);

			float limit = Mathf.DegToRad(PitchLimitDegrees);
			_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sensitivity, -limit, limit);
			Rotation = new Vector3(_pitch, 0f, 0f);
			return;
		}

		// M1 阶段只用于释放/重新捕获鼠标；暂停由 M5 的 GameManager 接管
		if (@event.IsActionPressed("pause"))
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}
	}

	public override void _Process(double delta)
	{
		if (_landingDip <= 0f)
			return;

		_landingDip = Mathf.MoveToward(_landingDip, 0f, LandingDipDepth / LandingDipRecoverTime * (float)delta);
		_camera.Position = new Vector3(0f, -_landingDip, 0f);
	}

	/// <summary>重着地时相机轻微下沉，落地更干脆（设计文档 §3.3）。</summary>
	public void LandingBump(float verticalSpeed)
	{
		if (_data != null && verticalSpeed <= -_data.LandingImpactSpeed)
			_landingDip = LandingDipDepth;
	}
}
