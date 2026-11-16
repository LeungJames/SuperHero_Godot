using Godot;
using System;

/// <summary>
/// 启动主菜单（2.0 设计 §10.4）：开始 / 继续 / 章节选择 / 设置 / 退出。
/// 只负责入口导航，不复制关卡内逻辑：已通关章节沿用武器库，未通关章节按默认配置直接进关。
/// 美术沿用漫画印刷体系（ComicTheme）+ 黄昏港口 toon/描边立体剪影。
/// </summary>
public partial class MainMenu : Node3D
{
	private const string ContinueIcon = "continue";

	private Button _start;
	private Button _continue;
	private Button _chapters;
	private Button _settings;
	private Button _quit;

	private SettingsMenu _settingsMenu;
	private ChapterSelect _chapterSelect;
	private LoadoutMenu _loadoutMenu;

	private Camera3D _camera;
	private Vector3 _cameraHome;
	private Vector3 _cameraHomeRotation;
	private float _time;

	private AudioStreamPlayer _uiSfx;
	private AudioStreamPlayer _wind;
	private AudioStreamPlayer _waves;

	private bool _loadoutFromChapters;
	private bool _switching;

	public override void _Ready()
	{
		GetTree().Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		ComicTheme.Apply(GetNode<Control>("UI/Menu"));
		_camera = GetNode<Camera3D>("World/Camera3D");
		_cameraHome = _camera.Position;
		_cameraHomeRotation = _camera.Rotation;

		_start = GetNode<Button>("UI/Menu/Card/VBox/StartButton");
		_continue = GetNode<Button>("UI/Menu/Card/VBox/ContinueButton");
		_chapters = GetNode<Button>("UI/Menu/Card/VBox/ChaptersButton");
		_settings = GetNode<Button>("UI/Menu/Card/VBox/SettingsButton");
		_quit = GetNode<Button>("UI/Menu/Card/VBox/QuitButton");

		_settingsMenu = GetNode<SettingsMenu>("UI/SettingsMenu");
		_chapterSelect = GetNode<ChapterSelect>("UI/ChapterSelect");
		_loadoutMenu = GetNode<LoadoutMenu>("UI/LoadoutMenu");

		BuildTypography();
		BuildPoster();
		BuildHalftone();
		WireButtons();
		WireSubmenus();
		BuildAudio();
		RefreshContinueButton();

		_start.GrabFocus();
		Callable.From(TryCommandLineChapter).CallDeferred();
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		if (_camera == null)
			return;

		_camera.Position = _cameraHome + new Vector3(
			Mathf.Sin(_time * 0.11f) * 0.22f,
			Mathf.Sin(_time * 0.17f) * 0.07f,
			0f);
		_camera.Rotation = _cameraHomeRotation + new Vector3(
			Mathf.DegToRad(Mathf.Sin(_time * 0.13f) * 0.35f),
			Mathf.DegToRad(Mathf.Sin(_time * 0.09f) * 0.55f),
			0f);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		bool back = @event switch
		{
			InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } => true,
			InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B } => true,
			_ => false,
		};

		if (!back)
			return;

		if (_settingsMenu is { Visible: true })
		{
			_settingsMenu.Close();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_loadoutMenu is { Visible: true })
		{
			_loadoutMenu.Close();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_chapterSelect is { Visible: true })
		{
			_chapterSelect.Close();
			GetViewport().SetInputAsHandled();
		}
	}

	// ------------------------------------------------------------ 构建

	private void BuildTypography()
	{
		var title = GetNode<Label>("UI/Menu/Card/VBox/Title");
		title.AddThemeFontOverride("font", ComicTheme.Bold);
		title.AddThemeFontSizeOverride("font_size", 62);
		title.AddThemeColorOverride("font_color", ComicTheme.Ink);

		var subtitle = GetNode<Label>("UI/Menu/Card/VBox/Subtitle");
		subtitle.AddThemeFontSizeOverride("font_size", 17);
		subtitle.AddThemeColorOverride("font_color", ComicTheme.Muted);

		var hint = GetNode<Label>("UI/Menu/Card/VBox/Hint");
		hint.AddThemeFontSizeOverride("font_size", 13);
		hint.AddThemeColorOverride("font_color", ComicTheme.Muted);

		foreach (Button button in new[] { _start, _continue, _chapters, _settings, _quit })
			button.CustomMinimumSize = new Vector2(0f, 48f);

		ComicTheme.Accent(_start, ContinueIcon);
		_continue.Icon = ComicTheme.Icon("target");
		_chapters.Icon = ComicTheme.Icon("chapters");
		_settings.Icon = ComicTheme.Icon("settings");
		_quit.Icon = ComicTheme.Icon("exit");
	}

	private void BuildPoster()
	{
		var image = GetNode<TextureRect>("UI/Menu/Poster/Box/PosterImage");
		Texture2D texture = ComicArt.Panel("port_opening", 0);
		if (texture != null)
			image.Texture = texture;
	}

	private void BuildHalftone()
	{
		if (GetNodeOrNull<ColorRect>("UI/ComicHalftone") is not ColorRect halftone)
			return;

		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/comic_halftone.gdshader") };
		material.SetShaderParameter("ink", ComicTheme.Ink);
		material.SetShaderParameter("spacing", 13f);
		material.SetShaderParameter("edge_start", 0.35f);
		material.SetShaderParameter("strength", 0.18f);
		halftone.Material = material;
		halftone.Visible = true;
	}

	private void BuildAudio()
	{
		_uiSfx = new AudioStreamPlayer { Name = "UiSfx", Bus = "SFX", VolumeDb = -12f };
		AddChild(_uiSfx);

		_wind = new AudioStreamPlayer
		{
			Name = "MenuWind",
			Stream = ProceduralAudio.Wind(),
			Bus = "Ambient",
			VolumeDb = -26f,
		};
		_waves = new AudioStreamPlayer
		{
			Name = "MenuWaves",
			Stream = ProceduralAudio.Waves(),
			Bus = "Ambient",
			VolumeDb = -27f,
		};
		AddChild(_wind);
		AddChild(_waves);
		_wind.Play();
		_waves.Play();
	}

	private void WireButtons()
	{
		_start.Pressed += () => { PlayCue(SfxKind.Melee); StartGame(); };
		_continue.Pressed += () => { PlayCue(SfxKind.Melee); ContinueGame(); };
		_chapters.Pressed += () => { PlayCue(SfxKind.ReloadSeat); OpenChapterSelect(); };
		_settings.Pressed += () => { PlayCue(SfxKind.ReloadSeat); OpenSettings(); };
		_quit.Pressed += () => { PlayCue(SfxKind.Land); GetTree().Quit(); };

		foreach (Button button in new[] { _start, _continue, _chapters, _settings, _quit })
		{
			button.MouseEntered += () => PlayCue(SfxKind.Hit, -22f, 1.25f);
			button.FocusEntered += () => PlayCue(SfxKind.Hit, -24f, 1.3f);
		}
	}

	private void WireSubmenus()
	{
		_settingsMenu.Closed += () =>
		{
			_settings.GrabFocus();
			PlayCue(SfxKind.Hit, -20f, 0.9f);
		};

		_chapterSelect.Closed += () =>
		{
			_chapters.GrabFocus();
			PlayCue(SfxKind.Hit, -20f, 0.9f);
		};

		_chapterSelect.ChapterChosen += chapter =>
		{
			PlayCue(SfxKind.Melee);
			EnterChapter(chapter, fromChapters: true);
		};

		_loadoutMenu.StartRequested += (chapter, ids) =>
		{
			if (!LoadoutSystem.IsValid(chapter.Id, ids))
				return;

			LoadoutSystem.SetPending(chapter.Id, ids);
			EnterScene(chapter.Scene);
		};

		_loadoutMenu.Closed += () =>
		{
			PlayCue(SfxKind.Hit, -20f, 0.9f);
			if (_loadoutFromChapters)
			{
				_loadoutFromChapters = false;
				_chapterSelect.Open();
				return;
			}

			_continue.GrabFocus();
		};
	}

	// ------------------------------------------------------------ 导航

	private void StartGame()
	{
		LoadoutSystem.ClearPending();
		EnterScene(SaveSystem.Chapters[0].Scene);
	}

	private void ContinueGame()
	{
		SaveSystem.ChapterInfo? chapter = FindContinueChapter();
		if (chapter is { } value)
			EnterChapter(value, fromChapters: false);
		else
			_start.GrabFocus();
	}

	private void OpenChapterSelect()
	{
		_chapterSelect.Open();
	}

	private void OpenSettings()
	{
		_settingsMenu.Open();
	}

	private void EnterChapter(SaveSystem.ChapterInfo chapter, bool fromChapters)
	{
		if (string.IsNullOrEmpty(chapter.Scene) || _switching)
			return;

		bool cleared = SaveSystem.HasInstance && SaveSystem.Instance.IsChapterCleared(chapter.Id);
		if (cleared)
		{
			_loadoutFromChapters = fromChapters;
			_loadoutMenu.Open(chapter);
			return;
		}

		LoadoutSystem.ClearPending();
		EnterScene(chapter.Scene);
	}

	private void EnterScene(string scene)
	{
		if (_switching || string.IsNullOrEmpty(scene))
			return;

		_switching = true;
		GetTree().Paused = false;
		_wind?.Stop();
		_waves?.Stop();
		_uiSfx?.Stop();
		RunCheckpoint.Clear();
		GetTree().ChangeSceneToFile(scene);
	}

	/// <summary>继续：存档里最高已解锁章节；没有任何通关记录时返回 null。</summary>
	private static SaveSystem.ChapterInfo? FindContinueChapter()
	{
		if (!SaveSystem.HasInstance)
			return null;

		SaveSystem.ChapterInfo? furthest = null;
		bool anyCleared = false;

		foreach (SaveSystem.ChapterInfo chapter in SaveSystem.Chapters)
		{
			if (!SaveSystem.Instance.IsChapterUnlocked(chapter.Id))
				continue;

			furthest = chapter;
			if (SaveSystem.Instance.IsChapterCleared(chapter.Id))
				anyCleared = true;
		}

		return anyCleared ? furthest : null;
	}

	private void RefreshContinueButton()
	{
		_continue.Visible = FindContinueChapter() != null;
	}

	/// <summary>仅调试直达：`--chapter=port|factory|anchor`，不进正式 UI。</summary>
	private void TryCommandLineChapter()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			if (!argument.StartsWith("--chapter=", StringComparison.Ordinal))
				continue;

			string id = argument["--chapter=".Length..];
			foreach (SaveSystem.ChapterInfo chapter in SaveSystem.Chapters)
			{
				if (chapter.Id != id)
					continue;

				LoadoutSystem.ClearPending();
				EnterScene(chapter.Scene);
				return;
			}
		}
	}

	// ------------------------------------------------------------ 反馈

	private void PlayCue(SfxKind kind, float volumeDb = -12f, float pitch = 1f)
	{
		if (_uiSfx == null)
			return;

		_uiSfx.Stream = ProceduralAudio.Get(kind);
		_uiSfx.VolumeDb = volumeDb;
		_uiSfx.PitchScale = pitch;
		_uiSfx.Play();
	}
}
