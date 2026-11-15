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

	// 关卡目标文案与高度阈值；默认值保持港口关原有表现，新关卡在场景里覆盖。
	[Export] public string ObjectiveHigh { get; set; } = "下到港区地面";
	[Export] public string ObjectiveLow { get; set; } = "清空港口";
	[Export] public string ObjectiveClear { get; set; } = "区域清空";
	[Export] public float ObjectiveHeightThreshold { get; set; } = 1.0f;

	// 章节串联：留空表示本关是终局，没有下一关。
	[Export] public string NextScenePath { get; set; } = string.Empty;
	[Export] public string NextChapterLabel { get; set; } = string.Empty;
	[Export] public bool IsFinalChapter { get; set; }
	[Export] public string VictorySubtitle { get; set; } = "直升机离开黄昏港口";

	/// <summary>存档里的章节 ID（port / factory / anchor），通关时写入。</summary>
	[Export] public string ChapterId { get; set; } = "port";

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
	private WeaponController _weapon;
	private EnemyRegistry _registry;
	private VictorySequenceController _victory;
	private HudController _hud;
	private ResultsPanel _results;
	private Control _pauseMenu;
	private SettingsMenu _settings;
	private ChapterSelect _chapterSelect;
	private CollectiblesMenu _collectiblesMenu;
	private Button _difficultyButton;

	private float _defeatTimer;
	private bool _defeatPending;
	private bool _switching;
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
		_settings = _pauseMenu.GetNodeOrNull<SettingsMenu>("SettingsMenu");
		_chapterSelect = _pauseMenu.GetNodeOrNull<ChapterSelect>("ChapterSelect");
		_collectiblesMenu = _pauseMenu.GetNodeOrNull<CollectiblesMenu>("CollectiblesMenu");

		_weapon = GetTree().GetFirstNodeInGroup("weapon") as WeaponController;

		_health.Died += OnPlayerDied;
		_registry.CountChanged += OnEnemyCountChanged;
		_registry.AllCleared += EnterVictory;

		if (_weapon != null)
			_weapon.ShotResolved += OnShotResolved;

		_results.NextPressed += GoToNextChapter;
		_results.RestartPressed += Restart;
		_results.QuitPressed += Quit;

		WirePauseMenu();

		if (_settings != null)
			_settings.Closed += OnSubmenuClosed;
		if (_chapterSelect != null)
			_chapterSelect.Closed += OnSubmenuClosed;
		if (_collectiblesMenu != null)
			_collectiblesMenu.Closed += OnSubmenuClosed;

		MaxHeight = _player.GlobalPosition.Y;
		_hud.SetEnemyCount(_registry.AliveCount, _registry.TotalCount);
		_hud.SetObjective(ObjectiveHigh);
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
		// 过场播放时 Esc 属于「跳过」输入，交给 CutsceneComicPlayer
		if (CutsceneComicPlayer.AnyPlaying)
			return;
		if (!@event.IsActionPressed("pause") || State != GameState.Playing)
			return;

		HandlePausePressed();
		GetViewport().SetInputAsHandled();
	}

	/// <summary>设置 / 章节选择打开时，Esc 先退子页，而不是直接继续游戏。</summary>
	private void HandlePausePressed()
	{
		if (_collectiblesMenu is { Visible: true })
		{
			_collectiblesMenu.Close();
			return;
		}

		if (_settings is { Visible: true })
		{
			_settings.Close();
			return;
		}

		if (_chapterSelect is { Visible: true })
		{
			_chapterSelect.Close();
			return;
		}

		TogglePause();
	}

	// ------------------------------------------------------------ 状态迁移

	private void EnterVictory()
	{
		if (State != GameState.Playing)
			return;

		// A 方案：全歼当帧交还控制权，只给撤离指引，玩家自己走到停机坪登机才算胜利
		State = GameState.Victory;
		SaveSystem.Instance?.MarkChapterCleared(ChapterId, ElapsedTime);
		_hud.SetObjective("区域清空 · 前往停机坪登机");
		_victory.Play();
	}

	private void OnPlayerDied(DeathCause cause)
	{
		if (State == GameState.Defeat)
			return;

		// 结算面板出现前仍可能被打死或摔死：中止撤离演出，按失败处理
		if (State == GameState.Victory)
		{
			_victory.Cancel();
			_hud.HideBanner();
			_hud.SetPrompt(string.Empty);
		}

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
		SaveSystem.Instance?.AppendRunRecord(ChapterId, true, ElapsedTime, Kills, TotalEnemies,
			ShotsFired, ShotsHit, Headshots, string.Empty);

		string nextLabel = string.IsNullOrEmpty(NextScenePath)
			? null
			: $"进入{(string.IsNullOrEmpty(NextChapterLabel) ? "下一章" : NextChapterLabel)}（Enter）";
		_results.Present(true, VictorySubtitle, BuildStats(victory: true), nextLabel, IsFinalChapter);
	}

	private void ShowDefeatResults()
	{
		SaveSystem.Instance?.AppendRunRecord(ChapterId, false, ElapsedTime, Kills, TotalEnemies,
			ShotsFired, ShotsHit, Headshots, DeathCause.ToString());
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
			target = ObjectiveClear;
		else if (_player.GlobalPosition.Y <= ObjectiveHeightThreshold)
			target = $"{ObjectiveLow} · 剩余 {_registry.AliveCount}";
		else
			target = ObjectiveHigh;

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
		var settings = _pauseMenu.GetNodeOrNull<Button>("Panel/VBox/SettingsButton");
		var chapters = _pauseMenu.GetNodeOrNull<Button>("Panel/VBox/ChaptersButton");
		var collectibles = _pauseMenu.GetNodeOrNull<Button>("Panel/VBox/CollectiblesButton");
		_difficultyButton = _pauseMenu.GetNodeOrNull<Button>("Panel/VBox/DifficultyButton");
		var quit = _pauseMenu.GetNodeOrNull<Button>("Panel/VBox/QuitButton");

		if (resume != null)
			resume.Pressed += TogglePause;
		if (settings != null)
			settings.Pressed += OpenSettings;
		if (chapters != null)
			chapters.Pressed += OpenChapterSelect;
		if (collectibles != null)
			collectibles.Pressed += OpenCollectibles;
		if (_difficultyButton != null)
			_difficultyButton.Pressed += CycleDifficulty;
		if (quit != null)
			quit.Pressed += Quit;

		RefreshDifficultyLabel();
	}

	/// <summary>暂停面板直接循环切换难度；只影响敌人侧参数，立即生效并落盘。</summary>
	private void CycleDifficulty()
	{
		string next = SaveSystem.Instance?.DifficultyId switch
		{
			"easy" => "normal",
			"normal" => "hard",
			_ => "easy",
		};

		SaveSystem.Instance?.SetDifficulty(next);
		RefreshDifficultyLabel();
	}

	private void RefreshDifficultyLabel()
	{
		if (_difficultyButton == null)
			return;

		string name = SaveSystem.Instance?.DifficultyId switch
		{
			"easy" => "简单",
			"hard" => "困难",
			_ => "普通",
		};
		_difficultyButton.Text = $"难度：{name}";
	}

	private void OpenSettings()
	{
		SetPauseMainPanelVisible(false);
		_settings?.Open();
	}

	private void OpenChapterSelect()
	{
		SetPauseMainPanelVisible(false);
		_chapterSelect?.Open();
	}

	private void OpenCollectibles()
	{
		SetPauseMainPanelVisible(false);
		_collectiblesMenu?.Open();
	}

	private void OnSubmenuClosed()
	{
		SetPauseMainPanelVisible(true);
		_pauseMenu.GetNodeOrNull<Button>("Panel/VBox/ResumeButton")?.GrabFocus();
	}

	private void SetPauseMainPanelVisible(bool visible)
	{
		_pauseMenu.GetNodeOrNull<Control>("Panel")?.SetVisible(visible);
	}

	private void TogglePause()
	{
		if (State != GameState.Playing)
			return;

		bool paused = !GetTree().Paused;

		if (!paused)
		{
			_settings?.Close();
			_chapterSelect?.Close();
			_collectiblesMenu?.Close();
		}

		GetTree().Paused = paused;
		_pauseMenu.Visible = paused;
		if (paused)
		{
			SetPauseMainPanelVisible(true);
			RefreshDifficultyLabel();
		}
		Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;

		if (paused)
			_pauseMenu.GetNodeOrNull<Button>("Panel/VBox/ResumeButton")?.GrabFocus();
	}

	/// <summary>结算面板的“进入下一章”：换场景后新关的 GameManager 会重新计时、重新统计。</summary>
	public void GoToNextChapter()
	{
		if (_switching || string.IsNullOrEmpty(NextScenePath))
			return;

		_switching = true;
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(NextScenePath);
	}

	public void Restart()
	{
		if (_switching)
			return;

		_switching = true;
		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();
	}

	public void Quit()
	{
		GetTree().Quit();
	}
}
