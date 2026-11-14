using Godot;

/// <summary>
/// 全歼后的胜利演出：横幅 → 无线电 → 直升机进场降落 → 侧向机位自动登机 → 淡出。
/// 不做胜负判定、不做 UI 绘制（设计文档 §2.3、§9.3）。
/// </summary>
public partial class VictorySequenceController : Node
{
	[Export] public VictorySequenceConfig Config { get; set; }

	public bool IsPlaying { get; private set; }

	private Node3D _helicopter;
	private Node3D _rotor;
	private Node3D _tailRotor;
	private Camera3D _cinematicCamera;
	private PlayerController _player;
	private PlayerCamera _playerCamera;
	private HudController _hud;
	private GameManager _gameManager;

	private float _time;
	private float _boardBlend;
	private bool _boardStarted;
	private bool _landed;
	private bool _finished;

	public override void _Ready()
	{
		_helicopter = GetNode<Node3D>("../../Actors/Helicopter");
		_rotor = _helicopter.GetNodeOrNull<Node3D>("Rotor");
		_tailRotor = _helicopter.GetNodeOrNull<Node3D>("TailRotor");
		_cinematicCamera = GetNodeOrNull<Camera3D>("../../Actors/CinematicCamera");
		_player = GetNode<PlayerController>("../../Actors/Player");
		_playerCamera = _player.GetNode<PlayerCamera>("Head");
		_hud = GetNode<HudController>("../../UI/Hud");
		_gameManager = GetNode<GameManager>("../GameManager");

		if (Config == null)
			GD.PushError("VictorySequenceController: 没有配置 VictorySequenceConfig。");

		_helicopter.Visible = false;
	}

	public void Play()
	{
		if (IsPlaying || _finished || Config == null)
			return;

		IsPlaying = true;
		_time = 0f;
		_boardBlend = 0f;
		_boardStarted = false;
		_landed = false;

		_helicopter.Visible = true;
		_helicopter.GlobalPosition = Config.HelicopterApproachFrom;

		_player.InputEnabled = false;
		_playerCamera.LookEnabled = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		_hud.SetCombatVisible(false);
		_hud.ShowBanner(Config.BannerText);
	}

	public override void _Process(double delta)
	{
		if (!IsPlaying || Config == null)
			return;

		float dt = (float)delta;
		_time += dt;

		SpinRotors(dt);
		UpdateRadio();
		UpdateApproach();
		UpdateBoarding(dt);
		UpdateFade();

		if (_time >= Config.ResultsTime && !_finished)
		{
			_finished = true;
			IsPlaying = false;
			_hud.HideBanner();
			_gameManager.ShowVictoryResults();
		}
	}

	private void SpinRotors(float dt)
	{
		float deg = Config.RotorSpinDegreesPerSecond * dt;
		_rotor?.RotateY(Mathf.DegToRad(deg));
		_tailRotor?.RotateX(Mathf.DegToRad(deg * 1.5f));
	}

	private void UpdateRadio()
	{
		if (_time < Config.RadioStart || _time > Config.RadioEnd)
			return;

		if (!_radioShown)
		{
			_radioShown = true;
			_hud.ShowMessage(Config.RadioText, new Color(0.96f, 0.92f, 0.86f));
		}
	}

	private bool _radioShown;

	private void UpdateApproach()
	{
		if (_time < Config.ApproachStart)
			return;

		if (_time >= Config.LandTime)
		{
			if (!_landed)
			{
				_landed = true;
				_helicopter.GlobalPosition = Config.HelicopterLandingPosition;
				_helicopter.Rotation = new Vector3(0f, Mathf.Pi, 0f);
			}

			return;
		}

		float span = Mathf.Max(Config.LandTime - Config.ApproachStart, 0.01f);
		float t = Mathf.Clamp((_time - Config.ApproachStart) / span, 0f, 1f);
		float eased = t * t * (3f - 2f * t);

		Vector3 from = Config.HelicopterApproachFrom;
		Vector3 control = Config.HelicopterApproachControl;
		Vector3 to = Config.HelicopterLandingPosition;

		_helicopter.GlobalPosition = from.Lerp(control, eased).Lerp(control.Lerp(to, eased), eased);
		_helicopter.Rotation = new Vector3(0f, Mathf.Pi + (1f - eased) * 0.6f, 0f);
	}

	/// <summary>9.4~11.4s：切侧向机位，玩家自动走向舱门并隐藏。</summary>
	private void UpdateBoarding(float dt)
	{
		if (_time < Config.BoardStart)
			return;

		if (!_boardStarted)
		{
			_boardStarted = true;
			if (_cinematicCamera != null)
			{
				_cinematicCamera.GlobalPosition = Config.CinematicCameraPosition;
				_cinematicCamera.LookAt(_helicopter.GlobalPosition + new Vector3(0f, 1.6f, 0f));
				_cinematicCamera.Current = true;
			}
		}

		float span = Mathf.Max(Config.BoardEnd - Config.BoardStart, 0.01f);
		_boardBlend = Mathf.Clamp((_time - Config.BoardStart) / span, 0f, 1f);

		Vector3 target = _helicopter.GlobalPosition + Config.BoardTargetOffset;
		_player.GlobalPosition = _player.GlobalPosition.Lerp(target, Mathf.Min(dt * 3f, 1f));
		_player.Rotation = new Vector3(0f, Mathf.Atan2(
			_helicopter.GlobalPosition.X - _player.GlobalPosition.X,
			_helicopter.GlobalPosition.Z - _player.GlobalPosition.Z), 0f);

		if (_boardBlend >= 1f)
			_player.Visible = false;
	}

	private void UpdateFade()
	{
		if (_time < Config.FadeStart)
			return;

		float span = Mathf.Max(Config.FadeEnd - Config.FadeStart, 0.01f);
		_hud.SetFade(Mathf.Clamp((_time - Config.FadeStart) / span, 0f, 1f));
	}
}
