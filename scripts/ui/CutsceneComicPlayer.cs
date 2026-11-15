using Godot;
using System;

/// <summary>
/// 漫画过场播放器（2.0 设计 §4.4）。正式分镜插画、轻缓推镜、旁白和印刷图形由 UI 层呈现，
/// 播放期间暂停游戏，首次不可跳过，看过一次后可按空格 / 回车跳过。
/// </summary>
public partial class CutsceneComicPlayer : CanvasLayer
{
	/// <summary>任意一段过场正在播放；GameManager 用它避免把 Esc 当成暂停。</summary>
	public static bool AnyPlaying { get; private set; }

	public event Action Finished;

	public bool IsPlaying { get; private set; }
	public bool IsSkippable { get; private set; }
	public CutsceneData CurrentData => _data;

	private CutsceneCanvas _canvas;
	private CutsceneData _data;
	private int _index;
	private float _panelTime;
	private float _fadeTime = 0.25f;
	private bool _markSeen;
	private bool _wasPaused;
	private float _inputDelay;
	private float _hintTimer;

	public override void _Ready()
	{
		Layer = 17;
		ProcessMode = ProcessModeEnum.Always;

		_canvas = new CutsceneCanvas { Name = "Canvas", MouseFilter = Control.MouseFilterEnum.Stop };
		AddChild(_canvas);
		_canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_canvas.Visible = false;
	}

	public override void _ExitTree()
	{
		if (IsPlaying)
		{
			IsPlaying = false;
			AnyPlaying = false;
			GetTree().Paused = false;
		}
	}

	/// <summary>
	/// 播放一段过场。<paramref name="markSeen"/> 为 false 时用于调试预览：不写存档、随时可跳过。
	/// </summary>
	public void Play(CutsceneData data, bool markSeen = true)
	{
		if (IsPlaying || AnyPlaying || data == null || data.Panels == null || data.Panels.Count == 0)
			return;

		_data = data;
		_index = 0;
		_panelTime = 0f;
		_fadeTime = Mathf.Max(0.05f, data.PanelFadeTime);
		_markSeen = markSeen;
		IsSkippable = !markSeen || (SaveSystem.Instance?.IsCutsceneSeen(data.Id) ?? false);
		IsPlaying = true;
		AnyPlaying = true;
		_inputDelay = IsSkippable ? 0.2f : 0.4f;
		_hintTimer = 0f;

		_wasPaused = GetTree().Paused;
		GetTree().Paused = true;

		_canvas.Data = data;
		_canvas.Skippable = IsSkippable;
		_canvas.Progress = 0;
		_canvas.Index = 0;
		_canvas.Alpha = 0f;
		_canvas.Hint = string.Empty;
		_canvas.Visible = true;
		_canvas.QueueRedraw();

		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public override void _Process(double delta)
	{
		if (!IsPlaying)
			return;

		float dt = (float)delta;
		_inputDelay = Mathf.Max(0f, _inputDelay - dt);
		_hintTimer = Mathf.Max(0f, _hintTimer - dt);
		_panelTime += dt;

		CutscenePanelData panel = _data.Panels[_index];
		float duration = Mathf.Max(0.5f, panel.Duration);

		if (_panelTime >= duration)
		{
			_index++;
			if (_index >= _data.Panels.Count)
			{
				Finish();
				return;
			}

			_panelTime = 0f;
			_canvas.Index = _index;
		}

		float t = _panelTime;
		float alpha = Mathf.Min(1f, t / _fadeTime) * Mathf.Min(1f, Mathf.Max(0f, duration - t) / _fadeTime);
		_canvas.Alpha = Mathf.Clamp(alpha, 0f, 1f);
		_canvas.Progress = Mathf.Clamp(t / duration, 0f, 1f);
		_canvas.Hint = _hintTimer > 0f ? "首次播放不可跳过" : string.Empty;
		_canvas.QueueRedraw();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsPlaying)
			return;

		bool wantsSkip = @event switch
		{
			InputEventKey { Pressed: true, Echo: false } key =>
				key.Keycode is Key.Space or Key.Enter or Key.Escape,
			InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } => true,
			InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A } => true,
			_ => false,
		};

		if (!wantsSkip)
			return;

		GetViewport().SetInputAsHandled();
		if (_inputDelay > 0f)
			return;

		if (IsSkippable)
		{
			Finish();
			return;
		}

		_hintTimer = 1.2f;
		_canvas.Hint = "首次播放不可跳过";
	}

	private void Finish()
	{
		IsPlaying = false;
		AnyPlaying = false;
		_canvas.Visible = false;
		_canvas.Hint = string.Empty;

		if (_markSeen && _data != null)
			SaveSystem.Instance?.MarkCutsceneSeen(_data.Id);

		GetTree().Paused = _wasPaused;
		if (!_wasPaused)
			Input.MouseMode = Input.MouseModeEnum.Captured;

		Finished?.Invoke();
	}

	/// <summary>探针用：直接跳到指定格，避免等待整段时长。</summary>
	public void DebugJumpTo(int index, float panelTime = 0f)
	{
		if (!IsPlaying || _data == null)
			return;

		_index = Mathf.Clamp(index, 0, _data.Panels.Count - 1);
		_panelTime = panelTime;
		_canvas.Index = _index;
		_canvas.QueueRedraw();
	}

	/// <summary>
	/// 探针 / 手柄兼容用：与空格跳过走完全相同的判定。
	/// 返回是否真的结束播放；首次播放会返回 false。
	/// </summary>
	public bool DebugAttemptSkip()
	{
		if (!IsPlaying || _inputDelay > 0f)
			return false;

		if (!IsSkippable)
		{
			_hintTimer = 1.2f;
			_canvas.Hint = "首次播放不可跳过";
			return false;
		}

		Finish();
		return true;
	}

	// ---------------------------------------------------------------- 绘制

	private sealed partial class CutsceneCanvas : Control
    {
        public CutsceneData Data { get; set; }
        public int Index { get; set; }
        public float Alpha { get; set; }
        public float Progress { get; set; }
        public bool Skippable { get; set; }
        public string Hint { get; set; } = string.Empty;
        private readonly Color _ink = ComicTheme.Ink;
        private Label _caption;
        private Label _title;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            Theme = ComicTheme.Shared;
            _caption = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
            _caption.AddThemeColorOverride("font_color", ComicTheme.Ink);
            _caption.AddThemeFontOverride("font", ComicTheme.Bold);
            AddChild(_caption);
            _title = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
            _title.AddThemeFontOverride("font", ComicTheme.Bold);
            _title.AddThemeColorOverride("font_color", ComicTheme.Ink);
            AddChild(_title);
        }

        public override void _Draw()
        {
            if (Data?.Panels == null || Data.Panels.Count == 0 || Size.X < 32) return;
            int index = Mathf.Clamp(Index, 0, Data.Panels.Count - 1);
            CutscenePanelData panel = Data.Panels[index];
            float scale = Mathf.Clamp(Size.Y / 800f, 0.72f, 1.6f);
            float margin = 28f * scale;
            var page = new Rect2(Vector2.One * margin, Size - Vector2.One * margin * 2);
            DrawRect(new Rect2(Vector2.Zero, Size), _ink);
            DrawRect(page, ComicTheme.Paper);
            DrawRect(page, _ink, false, 5);
            Texture2D texture = ComicArt.Page(Data.Id);
            Rect2 sourcePanel = ComicArt.Region(Data.Id, index);
            float artHeight = page.Size.Y - 24 * scale;
            float aspect = sourcePanel.Size.Y > 0 ? sourcePanel.Size.X / sourcePanel.Size.Y : 1.2f;
            float artWidth = Mathf.Min(page.Size.X * 0.72f, artHeight * aspect);
            artHeight = artWidth / aspect;
            var art = new Rect2(page.Position + Vector2.One * 12 * scale, new Vector2(artWidth, artHeight));
            var notes = new Rect2(new Vector2(art.End.X + 28 * scale, art.Position.Y), new Vector2(page.End.X - art.End.X - 48 * scale, art.Size.Y));
            DrawRect(art, _ink);
            if (texture != null)
            {
                Rect2 source = ComicArt.Region(Data.Id, index);
                float ratio = art.Size.X / art.Size.Y;
                Vector2 crop = source.Size;
                if (crop.X / crop.Y > ratio) crop.X = crop.Y * ratio;
                else crop.Y = crop.X / ratio;
                crop /= 1.0f + Progress * 0.025f;
                Vector2 room = source.Size - crop;
                Vector2 offset = new(room.X * (0.44f + Progress * 0.12f), room.Y * (0.46f + Progress * 0.08f));
                DrawTextureRectRegion(texture, art, new Rect2(source.Position + offset, crop), new Color(1, 1, 1, Mathf.Max(0.12f, Alpha)));
            }
            DrawRect(art, _ink, false, 3 * scale);
            for (float y = notes.End.Y - 110 * scale; y < notes.End.Y - 18 * scale; y += 8 * scale)
                for (float x = notes.Position.X; x < notes.End.X; x += 8 * scale)
                    if (x - notes.Position.X > (notes.End.Y - y) * 0.6f)
                        DrawCircle(new Vector2(x, y), 0.9f * scale, new Color(_ink, 0.17f));
            DrawRect(new Rect2(notes.Position, new Vector2(notes.Size.X, 8 * scale)), ComicTheme.Orange);
            Font font = ComicTheme.Bold;
            DrawString(font, notes.Position + new Vector2(0, 42 * scale), $"{index + 1:00}  /  {Data.Panels.Count:00}", HorizontalAlignment.Left, -1, (int)(18 * scale), ComicTheme.Muted);
            _title.Text = Data.Title ?? string.Empty;
            _title.Position = notes.Position + new Vector2(0, 66 * scale);
            _title.Size = new Vector2(notes.Size.X, 116 * scale);
            _title.AddThemeFontSizeOverride("font_size", (int)(26 * scale));
            _caption.Text = panel.Caption ?? string.Empty;
            _caption.Position = notes.Position + new Vector2(0, 200 * scale);
            _caption.Size = new Vector2(notes.Size.X, Mathf.Max(100 * scale, notes.Size.Y - 330 * scale));
            _caption.AddThemeFontSizeOverride("font_size", (int)(21 * scale));
            DrawLine(notes.Position + new Vector2(0, 180 * scale), notes.Position + new Vector2(notes.Size.X, 180 * scale), _ink, 2);
            if (!string.IsNullOrEmpty(panel.SfxText))
            {
                Vector2 pos = new(art.Position.X + 24 * scale, art.End.Y - 34 * scale);
                DrawSetTransform(pos, Mathf.DegToRad(-7), Vector2.One);
                DrawStringOutline(font, Vector2.Zero, panel.SfxText, HorizontalAlignment.Left, -1, (int)(45 * scale), (int)(8 * scale), _ink);
                DrawString(font, Vector2.Zero, panel.SfxText, HorizontalAlignment.Left, -1, (int)(45 * scale), ComicTheme.PaperLight);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            for (int i = 0; i < Data.Panels.Count; i++)
            {
                float width = (notes.Size.X - (Data.Panels.Count - 1) * 7 * scale) / Data.Panels.Count;
                Rect2 segment = new(notes.Position.X + i * (width + 7 * scale), notes.End.Y - 38 * scale, width, 4 * scale);
                DrawRect(segment, new Color(_ink, 0.18f));
                if (i <= index) DrawRect(new Rect2(segment.Position, new Vector2(width * (i == index ? Progress : 1), segment.Size.Y)), ComicTheme.Orange);
            }
            string hint = !string.IsNullOrEmpty(Hint) ? Hint : Skippable ? "空格 / 回车 / A  跳过" : "故事正在展开";
            DrawString(ComicTheme.Body, new Vector2(notes.Position.X, notes.End.Y - 8 * scale), hint, HorizontalAlignment.Left, notes.Size.X, (int)(13 * scale), ComicTheme.Muted);
        }
    }
}
