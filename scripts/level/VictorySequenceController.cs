using Godot;
using System;

/// <summary>
/// 全歼后的撤离演出（A 方案）：交还控制权 → 直升机按时间轴进场降落 → 玩家自行走进机舱 → 淡出结算。
/// 演出本身不可跳过，但降落阶段不锁镜头、不锁移动与射击；只有真正入舱的瞬间才接演出机位。
/// 不做胜负判定、不做 UI 绘制（设计文档 §2.3、§9.3）。
/// </summary>
public partial class VictorySequenceController : Node
{
	[Export] public VictorySequenceConfig Config { get; set; }

	public event Action Started;
	public event Action RadioCue;

	public bool IsPlaying { get; private set; }

	/// <summary>直升机是否已经降落在停机坪（HUD 指引与外部验证用）。</summary>
	public bool HasLanded => _landed;

	/// <summary>玩家是否已经进入登机演出。</summary>
	public bool IsBoarding => _boarding;

	/// <summary>机舱口的世界坐标（登机判定中心）。</summary>
	public Vector3 CabinPoint => _helicopter != null && Config != null
		? _helicopter.GlobalPosition + Config.BoardTargetOffset
		: Vector3.Zero;

	private Node3D _helicopter;
	private Node3D _rotor;
	private Node3D _tailRotor;
	private Camera3D _cinematicCamera;
	private PlayerController _player;
	private PlayerCamera _playerCamera;
	private HudController _hud;
	private GameManager _gameManager;

	private float _time;
	private float _boardTimer;
	private bool _landed;
	private bool _boarding;
	private bool _finished;
	private bool _radioShown;

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

	/// <summary>全歼当帧调用：不接管玩家，只让直升机开始进场并给出撤离指引。</summary>
	public void Play()
	{
		if (IsPlaying || _finished || Config == null)
			return;

		IsPlaying = true;
		_time = 0f;
		_boardTimer = 0f;
		_landed = false;
		_boarding = false;
		_radioShown = false;

		_helicopter.Visible = true;
		_helicopter.GlobalPosition = Config.HelicopterApproachFrom;

		_hud.ShowBanner(Config.BannerText);
		_hud.SetPrompt(Config.WaitText);

		Started?.Invoke();
	}

	/// <summary>结算面板出现前玩家阵亡：立刻中止撤离流程，交由 GameManager 按失败处理。</summary>
	public void Cancel()
	{
		if (_finished)
			return;

		IsPlaying = false;
		_boarding = false;
		_hud.HideBanner();
		_hud.SetPrompt(string.Empty);
		_hud.SetFade(0f);
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
	}

	private void SpinRotors(float dt)
	{
		float deg = Config.RotorSpinDegreesPerSecond * dt;
		_rotor?.RotateY(Mathf.DegToRad(deg));
		_tailRotor?.RotateX(Mathf.DegToRad(deg * 1.5f));
	}

	private void UpdateRadio()
	{
		if (_time < Config.RadioStart || _time > Config.RadioEnd || _radioShown)
			return;

		_radioShown = true;
		_hud.ShowMessage(Config.RadioText, new Color(0.96f, 0.92f, 0.86f));
		RadioCue?.Invoke();
	}

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
				_hud.ShowBanner(Config.BannerText);
				_hud.SetPrompt(Config.BoardHint);
				_hud.ShowMessage(Config.BoardHint, new Color(0.96f, 0.92f, 0.86f));
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

	/// <summary>
	/// 降落后等待玩家自行走进机舱：距离舱口 2m 内才开始登机演出。
	/// 没走过去就一直等待，不结算。
	/// </summary>
	private void UpdateBoarding(float dt)
	{
		if (!_boarding)
		{
			if (!_landed)
				return;

			Vector3 cabin = CabinPoint;
			Vector3 player = _player.GlobalPosition;
			float distance = new Vector2(player.X - cabin.X, player.Z - cabin.Z).Length();
			if (distance > Config.BoardDistance)
				return;

			_boarding = true;
			_boardTimer = 0f;
			_player.InputEnabled = false;
			_playerCamera.LookEnabled = false;
			_hud.HideBanner();
			_hud.SetPrompt(string.Empty);

			if (_cinematicCamera != null)
			{
				_cinematicCamera.GlobalPosition = Config.CinematicCameraPosition;
				_cinematicCamera.LookAt(_helicopter.GlobalPosition + new Vector3(0f, 1.6f, 0f));
				_cinematicCamera.Current = true;
			}
		}

		_boardTimer += dt;

		float boardSpan = Mathf.Max(Config.BoardEnd - Config.BoardStart, 0.01f);
		float boardBlend = Mathf.Clamp(_boardTimer / boardSpan, 0f, 1f);

		Vector3 target = CabinPoint;
		_player.GlobalPosition = _player.GlobalPosition.Lerp(target, Mathf.Min(dt * 4f, 1f));
		_player.Rotation = new Vector3(0f, Mathf.Atan2(
			_helicopter.GlobalPosition.X - _player.GlobalPosition.X,
			_helicopter.GlobalPosition.Z - _player.GlobalPosition.Z), 0f);

		if (boardBlend >= 1f)
			_player.Visible = false;

		float fadeStart = Mathf.Max(Config.FadeStart - Config.BoardStart, boardSpan);
		float fadeEnd = Mathf.Max(Config.FadeEnd - Config.BoardStart, fadeStart + 0.01f);
		_hud.SetFade(Mathf.Clamp((_boardTimer - fadeStart) / (fadeEnd - fadeStart), 0f, 1f));

		if (_boardTimer >= fadeEnd && !_finished)
		{
			_finished = true;
			IsPlaying = false;
			_hud.SetFade(1f);
			_gameManager.ShowVictoryResults();
		}
	}
}
