using Godot;
using System;

public enum GameState
{
	Playing,
	Victory,
	Defeat,
}

/// <summary>
/// 一局流程：三状态机、计时与统计、暂停、重开、死因。
/// 不直接操作敌人与玩家控制器以外的系统（设计文档 §2.2、§9.3）。
/// </summary>
public partial class GameManager : Node
{
	[Export] public float DefeatDelay { get; set; } = 1.0f;

	public GameState State { get; private set; } = GameState.Playing;
	public float ElapsedTime { get; private set; }
	public float MaxHeight { get; private set; }
	public int ShotsFired { get; private set; }
	public int ShotsHit { get; private set; }
	public int Headshots { get; private set; }
	public DeathCause DeathCause { get; private set; }
	public float DeathHeight { get; private set; }

	public int Kills => _registry != null ? _registry.Kills : 0;
	public int TotalEnemies => _registry != null ? _registry.TotalCount : 0;

	private PlayerController _player;
	private PlayerHealth _health;
	private PlayerCamera _camera;
	private PistolController _pistol;
	private EnemyRegistry _registry;
	private VictorySequenceController _victory;
	private HudController _hud;
	private ResultsPanel _results;
	private Control _pauseMenu;

	private float _defeatTimer;
	private bool _defeatPending;
	private string _objective = string.Empty;

	public override void _Ready()
	{
		_player = GetNode<PlayerController>("../../Actors/Player");
		_health = _player.GetNode<PlayerHealth>("Health");
		_camera = _player.GetNode<PlayerCamera>("Head");
		_registry = GetNode<EnemyRegistry>("../EnemyRegistry");
		_victory = GetNode<VictorySequenceController>("../VictorySequenceController");
		_hud = GetNode<HudController>("../../UI/Hud");
		_results = GetNode<ResultsPanel>("../../UI/ResultsPanel");
		_pauseMenu = GetNode<Control>("../../UI/PauseMenu");

		_pistol = GetTree().GetFirstNodeInGroup("pistol") as PistolController;

		_health.Died += OnPlayerDied;
		_registry.CountChanged += OnEnemyCountChanged;
		_registry.AllCleared += EnterVictory;

		if (_pistol != null)
			_pistol.ShotResolved += OnShotResolved;

		_results.RestartPressed += Restart;
		_results.QuitPressed += Quit;

		WirePauseMenu();

		MaxHeight = _player.GlobalPosition.Y;
		_hud.SetEnemyCount(_registry.AliveCount, _registry.TotalCount);
		_hud.SetObjective("下到港区地面");
		_pauseMenu.Visible = false;
	}

	public override void _Process(double delta)
	{
		if (GetTree().Paused)
			return;

		float dt = (float)delta;

		if (State == GameState.Playing)
		{
			ElapsedTime += dt;
			MaxHeight = Mathf.Max(MaxHeight, _player.GlobalPosition.Y);
			_hud.SetHeight(_player.GlobalPosition.Y);
			UpdateObjective();
		}

		// 失败演出计时在 Playing 之外推进，否则进入 Defeat 后永远不会弹结算
		if (!_defeatPending)
			return;

		_defeatTimer -= dt;
		if (_defeatTimer > 0f)
			return;

		_defeatPending = false;
		ShowDefeatResults();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!@event.IsActionPressed("pause") || State != GameState.Playing)
			return;

		TogglePause();
		GetViewport().SetInputAsHandled();
	}

	// ------------------------------------------------------------ 状态迁移

	private void EnterVictory()
	{
		if (State != GameState.Playing)
			return;

		State = GameState.Victory;
		_player.InputEnabled = false;
		_hud.SetObjective("区域清空 · 撤离直升机抵达中");
		_victory.Play();
	}

	private void OnPlayerDied(DeathCause cause)
	{
		if (State != GameState.Playing)
			return;

		State = GameState.Defeat;
		DeathCause = cause;
		DeathHeight = _player.GlobalPosition.Y;
		_defeatPending = true;
		_defeatTimer = DefeatDelay;

		_camera.BeginDeath();
		_hud.SetCombatVisible(false);
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public void ShowVictoryResults()
	{
		string subtitle = "直升机离开黄昏港口";
		string stats = BuildStats(victory: true);
		_results.Present(true, subtitle, stats);
	}

	private void ShowDefeatResults()
	{
		_results.Present(false, DescribeDeathCause(), BuildStats(victory: false));
	}

	private string DescribeDeathCause()
	{
		return DeathCause switch
		{
			DeathCause.Gunshot => "死于枪伤",
			DeathCause.Fall => $"从 {Mathf.RoundToInt(MaxHeight - DeathHeight)}m 的高处坠落",
			DeathCause.Drown => "落水身亡",
			DeathCause.Explosion => "被爆炸波及",
			_ => "阵亡",
		};
	}

	private string BuildStats(bool victory)
	{
		float accuracy = ShotsFired > 0 ? (float)ShotsHit / ShotsFired * 100f : 0f;
		int seconds = Mathf.RoundToInt(ElapsedTime);

		if (victory)
		{
			return $"用时 {seconds} 秒\n击杀 {Kills}/{TotalEnemies}\n命中率 {accuracy:F0}%\n" +
				$"爆头 {Headshots}\n最高高度 {Mathf.RoundToInt(MaxHeight)}m";
		}

		return $"击杀 {Kills}/{TotalEnemies}\n存活 {seconds} 秒\n命中率 {accuracy:F0}%\n" +
			$"死亡高度 {Mathf.RoundToInt(DeathHeight)}m";
	}

	private void UpdateObjective()
	{
		string target;
		if (_registry.AliveCount <= 0)
			target = "区域清空";
		else if (_player.GlobalPosition.Y <= 1.0f)
			target = $"清空港口 · 剩余 {_registry.AliveCount}";
		else
			target = "下到港区地面";

		if (target == _objective)
			return;

		_objective = target;
		_hud.SetObjective(target);
	}

	// ------------------------------------------------------------ 统计

	private void OnShotResolved(HitscanHit hit)
	{
		ShotsFired++;

		if (!hit.HasHit || hit.Collider is not Hitbox hitbox)
			return;

		ShotsHit++;
		if (hitbox.IsHead)
			Headshots++;
	}

	private void OnEnemyCountChanged(int alive, int total)
	{
		_hud.SetEnemyCount(alive, total);
	}

	// ------------------------------------------------------------ 暂停与重开

	private void WirePauseMenu()
	{
		var resume = _pauseMenu.GetNodeOrNull<Button>("Panel/VBox/ResumeButton");
		var quit = _pauseMenu.GetNodeOrNull<Button>("Panel/VBox/QuitButton");

		if (resume != null)
			resume.Pressed += TogglePause;
		if (quit != null)
			quit.Pressed += Quit;
	}

	private void TogglePause()
	{
		if (State != GameState.Playing)
			return;

		bool paused = !GetTree().Paused;
		GetTree().Paused = paused;
		_pauseMenu.Visible = paused;
		Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;

		if (paused)
			_pauseMenu.GetNodeOrNull<Button>("Panel/VBox/ResumeButton")?.GrabFocus();
	}

	public void Restart()
	{
		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();
	}

	public void Quit()
	{
		GetTree().Quit();
	}
}
