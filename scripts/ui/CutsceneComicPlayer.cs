using Godot;
using System;

/// <summary>
/// 程序化漫画过场播放器（2.0 设计 §4.4）。全部用 UI 层色块、剪影、网点与拟声词绘制，
/// 不接外部图片；播放期间暂停游戏，首次不可跳过，看过一次后可按空格 / 回车跳过。
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
		_canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
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
		public string Hint { get; set; } = string.Empty;

		private static readonly Color Ink = new(0.06f, 0.05f, 0.09f);
		private static readonly Color Paper = new(0.95f, 0.91f, 0.82f);
		private static readonly Color Cream = new(0.97f, 0.93f, 0.85f);
		private const float Skew = 14f;
		private const float DotSpacing = 13f;

		public override void _Ready()
		{
			MouseFilter = MouseFilterEnum.Stop;
		}

		public override void _Draw()
		{
			if (Data == null || Data.Panels == null || Data.Panels.Count == 0)
				return;

			Vector2 viewport = Size;
			if (viewport.X < 32f || viewport.Y < 32f)
				return;

			int index = Mathf.Clamp(Index, 0, Data.Panels.Count - 1);
			CutscenePanelData panel = Data.Panels[index];

			Modulate = new Color(1f, 1f, 1f, Alpha);
			DrawRect(new Rect2(Vector2.Zero, viewport), new Color(0.035f, 0.04f, 0.055f));

			float marginX = viewport.X * 0.075f;
			float marginY = viewport.Y * 0.085f;
			Rect2 frame = new(new Vector2(marginX, marginY),
				new Vector2(viewport.X - marginX * 2f, viewport.Y - marginY * 2f));

			DrawPaper(frame);

			float titleHeight = Mathf.Clamp(frame.Size.Y * 0.09f, 34f, 56f);
			float bandHeight = Mathf.Clamp(frame.Size.Y * 0.24f, 92f, 168f);
			Rect2 art = new(frame.Position + new Vector2(18f, titleHeight),
				new Vector2(frame.Size.X - 36f, frame.Size.Y - titleHeight - bandHeight - 12f));

			DrawArt(panel.Art, art, panel.Sky, panel.Accent);
			DrawHalftone(frame);
			DrawFrameBorder(frame);
			DrawTitle(frame, titleHeight);
			DrawCaption(frame, bandHeight, panel.Caption);
			DrawSfx(art, panel.SfxText, panel.Accent);

			if (!string.IsNullOrEmpty(Hint))
			{
				Font font = ThemeDB.FallbackFont;
				DrawString(font, new Vector2(viewport.X * 0.5f - 90f, viewport.Y - 28f), Hint,
					HorizontalAlignment.Left, -1f, 22, new Color(0.95f, 0.82f, 0.42f, 0.95f));
			}
			else if (Alpha > 0.2f)
			{
				Font font = ThemeDB.FallbackFont;
				DrawString(font, new Vector2(viewport.X - 300f, viewport.Y - 28f), "空格 / 回车 跳过",
					HorizontalAlignment.Left, -1f, 18, new Color(0.75f, 0.72f, 0.66f, 0.7f));
			}
		}

		private void DrawPaper(Rect2 frame)
		{
			Vector2[] quad = BuildQuad(frame);
			DrawColoredPolygon(quad, Paper);
		}

		private static Vector2[] BuildQuad(Rect2 frame) => new[]
		{
			frame.Position + new Vector2(Skew, 0f),
			frame.Position + new Vector2(frame.Size.X, 0f),
			frame.Position + new Vector2(frame.Size.X - Skew, frame.Size.Y),
			frame.Position + new Vector2(0f, frame.Size.Y),
		};

		private void DrawHalftone(Rect2 frame)
		{
			Color dot = new(Ink.R, Ink.G, Ink.B, 0.055f);
			for (float y = frame.Position.Y + DotSpacing * 0.5f; y < frame.End.Y; y += DotSpacing)
				for (float x = frame.Position.X + DotSpacing * 0.5f; x < frame.End.X; x += DotSpacing)
					DrawCircle(new Vector2(x, y), 1.15f, dot);
		}

		private void DrawFrameBorder(Rect2 frame)
		{
			Vector2[] quad = BuildQuad(frame);
			for (int i = 0; i < quad.Length; i++)
				DrawLine(quad[i], quad[(i + 1) % quad.Length], Ink, 4f, true);
		}

		private void DrawTitle(Rect2 frame, float height)
		{
			Font font = ThemeDB.FallbackFont;
			var strip = new Rect2(frame.Position + new Vector2(0f, 2f),
				new Vector2(frame.Size.X, height - 2f));
			DrawRect(strip, new Color(0.1f, 0.09f, 0.12f, 0.92f));

			DrawString(font, strip.Position + new Vector2(22f, height * 0.68f), Data.Title ?? string.Empty,
				HorizontalAlignment.Left, strip.Size.X - 44f, Mathf.RoundToInt(height * 0.42f), Cream);
		}

		private void DrawCaption(Rect2 frame, float bandHeight, string caption)
		{
			var band = new Rect2(frame.Position.X, frame.End.Y - bandHeight, frame.Size.X, bandHeight);
			DrawRect(band, new Color(0.1f, 0.09f, 0.12f, 0.94f));
			DrawLine(new Vector2(band.Position.X, band.Position.Y), new Vector2(band.End.X, band.Position.Y), Cream, 3f);

			Font font = ThemeDB.FallbackFont;
			int size = Mathf.Clamp(Mathf.RoundToInt(frame.Size.Y * 0.048f), 18, 34);
			DrawMultilineString(font, band.Position + new Vector2(24f, size + 14f), caption ?? string.Empty,
				HorizontalAlignment.Left, band.Size.X - 48f, size);
		}

		private void DrawSfx(Rect2 art, string text, Color accent)
		{
			if (string.IsNullOrEmpty(text))
				return;

			Font font = ThemeDB.FallbackFont;
			int size = Mathf.Clamp(Mathf.RoundToInt(art.Size.Y * 0.24f), 40, 120);
			Vector2 pos = new(art.Position.X + art.Size.X * 0.66f, art.Position.Y + art.Size.Y * 0.2f);
			DrawSetTransform(pos, Mathf.DegToRad(-8f), Vector2.One);
			DrawStringOutline(font, Vector2.Zero, text, HorizontalAlignment.Left, -1f, size, 18, Ink);
			DrawString(font, Vector2.Zero, text, HorizontalAlignment.Left, -1f, size, accent);
			DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
		}

		// ------------------------------------------------------------ 画面类型

		private void DrawArt(CutsceneArtKind kind, Rect2 r, Color sky, Color accent)
		{
			DrawRect(r, sky);
			switch (kind)
			{
				case CutsceneArtKind.Sea: DrawSea(r, sky, accent); break;
				case CutsceneArtKind.Crane: DrawCrane(r, sky, accent); break;
				case CutsceneArtKind.Silhouette: DrawSilhouette(r, sky, accent); break;
				case CutsceneArtKind.Yard: DrawYard(r, sky, accent); break;
				case CutsceneArtKind.Smoke: DrawSmoke(r, sky, accent); break;
				case CutsceneArtKind.Snake: DrawSnake(r, sky, accent); break;
				case CutsceneArtKind.Door: DrawDoor(r, sky, accent); break;
				case CutsceneArtKind.Gate: DrawGate(r, sky, accent); break;
				case CutsceneArtKind.Anchor: DrawAnchor(r, sky, accent); break;
				case CutsceneArtKind.Aftermath: DrawAftermath(r, sky, accent); break;
			}
		}

		private static Vector2 Pt(Rect2 r, float x, float y) =>
			new(r.Position.X + r.Size.X * x, r.Position.Y + r.Size.Y * y);

		private void DrawSea(Rect2 r, Color sky, Color accent)
		{
			float seaTop = 0.56f;
			DrawRect(new Rect2(Pt(r, 0f, seaTop), new Vector2(r.Size.X, r.Size.Y * (1f - seaTop))),
				sky.Darkened(0.38f));
			DrawCircle(Pt(r, 0.77f, 0.2f), r.Size.Y * 0.075f, accent);
			DrawCircle(Pt(r, 0.77f, 0.2f), r.Size.Y * 0.11f, new Color(accent.R, accent.G, accent.B, 0.18f));

			for (int i = 0; i < 7; i++)
			{
				float y = seaTop + 0.05f + i * 0.055f;
				float width = 3f - i * 0.25f;
				Vector2 previous = Pt(r, 0f, y);
				for (int x = 1; x <= 8; x++)
				{
					float wobble = (x % 2 == 0 ? -1f : 1f) * r.Size.Y * 0.012f;
					Vector2 next = Pt(r, x / 8f, y + wobble / r.Size.Y);
					DrawLine(previous, next, new Color(0.92f, 0.95f, 1f, 0.28f), Mathf.Max(1f, width));
					previous = next;
				}
			}

			// 小船剪影
			Vector2 a = Pt(r, 0.18f, seaTop + 0.12f);
			Vector2 b = Pt(r, 0.36f, seaTop + 0.12f);
			Vector2 c = Pt(r, 0.33f, seaTop + 0.19f);
			Vector2 d = Pt(r, 0.21f, seaTop + 0.19f);
			DrawColoredPolygon(new[] { a, b, c, d }, Ink);
			DrawLine(Pt(r, 0.27f, seaTop + 0.12f), Pt(r, 0.27f, seaTop - 0.12f), Ink, 3f);
			DrawColoredPolygon(new[]
			{
				Pt(r, 0.27f, seaTop - 0.12f),
				Pt(r, 0.27f, seaTop + 0.08f),
				Pt(r, 0.38f, seaTop + 0.06f),
			}, Ink);
		}

		private void DrawCrane(Rect2 r, Color sky, Color accent)
		{
			DrawRect(new Rect2(Pt(r, 0f, 0.8f), new Vector2(r.Size.X, r.Size.Y * 0.2f)), sky.Darkened(0.4f));
			float min = Mathf.Min(r.Size.X, r.Size.Y);

			// 塔身
			DrawLine(Pt(r, 0.2f, 0.88f), Pt(r, 0.2f, 0.14f), Ink, min * 0.022f);
			DrawLine(Pt(r, 0.27f, 0.88f), Pt(r, 0.27f, 0.14f), Ink, min * 0.018f);
			for (float y = 0.2f; y < 0.86f; y += 0.11f)
			{
				DrawLine(Pt(r, 0.2f, y), Pt(r, 0.27f, y + 0.08f), Ink, 2.5f);
				DrawLine(Pt(r, 0.27f, y), Pt(r, 0.2f, y + 0.08f), Ink, 2.5f);
			}

			// 吊臂与配重
			DrawLine(Pt(r, 0.23f, 0.14f), Pt(r, 0.82f, 0.22f), Ink, min * 0.026f);
			DrawLine(Pt(r, 0.23f, 0.14f), Pt(r, 0.1f, 0.2f), Ink, min * 0.018f);
			DrawRect(new Rect2(Pt(r, 0.06f, 0.18f), new Vector2(r.Size.X * 0.05f, r.Size.Y * 0.07f)), Ink);
			DrawLine(Pt(r, 0.82f, 0.22f), Pt(r, 0.82f, 0.46f), Ink, 2.5f);

			// 吊起的集装箱
			DrawRect(new Rect2(Pt(r, 0.74f, 0.46f), new Vector2(r.Size.X * 0.15f, r.Size.Y * 0.13f)), Ink);
			DrawLine(Pt(r, 0.74f, 0.48f), Pt(r, 0.89f, 0.48f), accent, 3f);
		}

		private void DrawSilhouette(Rect2 r, Color sky, Color accent)
		{
			DrawColoredPolygon(new[]
			{
				Pt(r, 0f, 0.15f), Pt(r, 1f, 0f), Pt(r, 1f, 0.55f), Pt(r, 0f, 0.75f),
			}, new Color(accent.R, accent.G, accent.B, 0.22f));

			float min = Mathf.Min(r.Size.X, r.Size.Y);

			// 头与帽檐
			DrawCircle(Pt(r, 0.5f, 0.32f), min * 0.085f, Ink);
			DrawLine(Pt(r, 0.42f, 0.29f), Pt(r, 0.59f, 0.29f), Ink, min * 0.026f);

			// 风衣
			DrawColoredPolygon(new[]
			{
				Pt(r, 0.42f, 0.4f), Pt(r, 0.58f, 0.4f),
				Pt(r, 0.68f, 0.74f), Pt(r, 0.6f, 0.86f),
				Pt(r, 0.4f, 0.86f), Pt(r, 0.32f, 0.74f),
			}, Ink);

			// 双腿
			DrawLine(Pt(r, 0.47f, 0.85f), Pt(r, 0.44f, 1f), Ink, min * 0.032f);
			DrawLine(Pt(r, 0.53f, 0.85f), Pt(r, 0.57f, 1f), Ink, min * 0.032f);

			// 斜雨
			for (int i = 0; i < 18; i++)
			{
				float x = (i * 37 % 100) / 100f;
				float y = (i * 53 % 90) / 100f;
				DrawLine(Pt(r, x, y), Pt(r, x - 0.015f, y + 0.07f),
					new Color(0.9f, 0.93f, 1f, 0.22f), 1.5f);
			}
		}

		private void DrawYard(Rect2 r, Color sky, Color accent)
		{
			DrawRect(new Rect2(Pt(r, 0f, 0.78f), new Vector2(r.Size.X, r.Size.Y * 0.22f)), sky.Darkened(0.45f));

			DrawContainer(r, 0.06f, 0.5f, 0.3f, 0.28f, accent);
			DrawContainer(r, 0.4f, 0.35f, 0.26f, 0.43f, accent.Darkened(0.15f));
			DrawContainer(r, 0.68f, 0.56f, 0.28f, 0.22f, accent.Darkened(0.3f));

			DrawLine(Pt(r, 0f, 0.78f), Pt(r, 1f, 0.78f), Ink, 3f);
			for (int i = 0; i < 5; i++)
				DrawLine(Pt(r, i * 0.22f - 0.1f, 1f), Pt(r, i * 0.22f, 0.8f), Ink, 2f);
		}

		private void DrawContainer(Rect2 r, float x, float y, float w, float h, Color accent)
		{
			var rect = new Rect2(Pt(r, x, y), new Vector2(r.Size.X * w, r.Size.Y * h));
			DrawRect(rect, Ink);
			DrawRect(rect.Grow(-3f), accent.Darkened(0.35f));
			for (int i = 1; i < 5; i++)
			{
				float lx = rect.Position.X + rect.Size.X * i / 5f;
				DrawLine(new Vector2(lx, rect.Position.Y + 4f), new Vector2(lx, rect.End.Y - 4f),
					new Color(0.9f, 0.9f, 0.9f, 0.18f), 2f);
			}
		}

		private void DrawSmoke(Rect2 r, Color sky, Color accent)
		{
			DrawRect(new Rect2(Pt(r, 0f, 0.82f), new Vector2(r.Size.X, r.Size.Y * 0.18f)), sky.Darkened(0.5f));

			DrawRect(new Rect2(Pt(r, 0.26f, 0.34f), new Vector2(r.Size.X * 0.09f, r.Size.Y * 0.5f)), Ink);
			DrawRect(new Rect2(Pt(r, 0.52f, 0.46f), new Vector2(r.Size.X * 0.07f, r.Size.Y * 0.38f)), Ink);
			DrawRect(new Rect2(Pt(r, 0.69f, 0.28f), new Vector2(r.Size.X * 0.1f, r.Size.Y * 0.56f)), Ink);

			DrawRect(new Rect2(Pt(r, 0.26f, 0.34f), new Vector2(r.Size.X * 0.09f, r.Size.Y * 0.03f)), accent);

			for (int i = 0; i < 9; i++)
			{
				float x = 0.22f + i * 0.075f;
				float y = 0.22f - (i % 3) * 0.06f;
				float radius = r.Size.Y * (0.05f + (i % 4) * 0.012f);
				DrawCircle(Pt(r, x, y), radius, new Color(0.72f, 0.72f, 0.7f, 0.26f - i * 0.02f));
			}
		}

		private void DrawSnake(Rect2 r, Color sky, Color accent)
		{
			DrawRect(new Rect2(Pt(r, 0f, 0.86f), new Vector2(r.Size.X, r.Size.Y * 0.14f)), sky.Darkened(0.4f));
			float min = Mathf.Min(r.Size.X, r.Size.Y);

			DrawArc(Pt(r, 0.46f, 0.7f), min * 0.24f, Mathf.Pi * 0.1f, Mathf.Pi * 1.75f, 48, Ink, min * 0.045f, true);
			DrawArc(Pt(r, 0.46f, 0.7f), min * 0.16f, Mathf.Pi * 1.2f, Mathf.Pi * 2.6f, 40, Ink, min * 0.035f, true);

			Vector2[] body =
			{
				Pt(r, 0.46f, 0.48f), Pt(r, 0.56f, 0.42f), Pt(r, 0.48f, 0.34f),
				Pt(r, 0.6f, 0.26f), Pt(r, 0.53f, 0.18f),
			};
			for (int i = 0; i < body.Length - 1; i++)
				DrawLine(body[i], body[i + 1], Ink, min * 0.04f, true);

			DrawColoredPolygon(new[]
			{
				Pt(r, 0.51f, 0.16f), Pt(r, 0.64f, 0.1f), Pt(r, 0.61f, 0.22f),
			}, Ink);
			DrawCircle(Pt(r, 0.585f, 0.135f), min * 0.012f, accent);
			DrawLine(Pt(r, 0.64f, 0.12f), Pt(r, 0.7f, 0.1f), accent, 2.5f);
			DrawLine(Pt(r, 0.7f, 0.1f), Pt(r, 0.73f, 0.13f), accent, 2f);
		}

		private void DrawDoor(Rect2 r, Color sky, Color accent)
		{
			DrawRect(r, sky.Darkened(0.25f));
			var door = new Rect2(Pt(r, 0.36f, 0.2f), new Vector2(r.Size.X * 0.26f, r.Size.Y * 0.66f));
			DrawRect(door, Ink);

			// 门缝透出的光
			DrawColoredPolygon(new[]
			{
				new Vector2(door.End.X - 4f, door.Position.Y + 6f),
				new Vector2(door.End.X - 4f, door.End.Y - 6f),
				Pt(r, 0.95f, 0.95f),
				Pt(r, 0.95f, 0.36f),
			}, new Color(accent.R, accent.G, accent.B, 0.35f));

			DrawRect(new Rect2(door.Position.X + 6f, door.Position.Y + 6f,
				door.Size.X * 0.35f, door.Size.Y - 12f), accent.Darkened(0.45f));
			DrawCircle(new Vector2(door.Position.X + door.Size.X * 0.8f, door.Position.Y + door.Size.Y * 0.52f),
				r.Size.Y * 0.014f, accent);

			for (int i = 0; i < 4; i++)
				DrawLine(Pt(r, 0.03f, 0.3f + i * 0.15f), Pt(r, 0.3f, 0.3f + i * 0.15f),
					new Color(0.9f, 0.92f, 0.95f, 0.16f), 2f);
		}

		private void DrawGate(Rect2 r, Color sky, Color accent)
		{
			DrawRect(new Rect2(Pt(r, 0f, 0.82f), new Vector2(r.Size.X, r.Size.Y * 0.18f)), sky.Darkened(0.45f));

			DrawRect(new Rect2(Pt(r, 0.14f, 0.12f), new Vector2(r.Size.X * 0.06f, r.Size.Y * 0.74f)), Ink);
			DrawRect(new Rect2(Pt(r, 0.8f, 0.12f), new Vector2(r.Size.X * 0.06f, r.Size.Y * 0.74f)), Ink);

			for (int i = 0; i < 4; i++)
			{
				float y = 0.2f + i * 0.16f;
				DrawLine(Pt(r, 0.2f, y), Pt(r, 0.8f, y), Ink, 4f);
			}

			for (int i = 0; i < 8; i++)
			{
				float x = 0.24f + i * 0.075f;
				DrawLine(Pt(r, x, 0.2f), Pt(r, x, 0.68f), Ink, 2f);
			}

			DrawLine(Pt(r, 0.2f, 0.2f), Pt(r, 0.8f, 0.68f), Ink, 2.5f);
			DrawLine(Pt(r, 0.8f, 0.2f), Pt(r, 0.2f, 0.68f), Ink, 2.5f);

			DrawRect(new Rect2(Pt(r, 0.2f, 0.69f), new Vector2(r.Size.X * 0.6f, r.Size.Y * 0.05f)), accent);
		}

		private void DrawAnchor(Rect2 r, Color sky, Color accent)
		{
			float min = Mathf.Min(r.Size.X, r.Size.Y);
			DrawRect(new Rect2(Pt(r, 0f, 0.84f), new Vector2(r.Size.X, r.Size.Y * 0.16f)), sky.Darkened(0.4f));

			DrawArc(Pt(r, 0.5f, 0.24f), min * 0.055f, 0f, Mathf.Tau, 28, Ink, min * 0.022f, true);
			DrawLine(Pt(r, 0.5f, 0.3f), Pt(r, 0.5f, 0.75f), Ink, min * 0.028f);
			DrawLine(Pt(r, 0.36f, 0.4f), Pt(r, 0.64f, 0.4f), Ink, min * 0.026f);
			DrawArc(Pt(r, 0.5f, 0.62f), min * 0.2f, Mathf.Pi * 0.05f, Mathf.Pi * 0.95f, 40, Ink, min * 0.028f, true);

			DrawColoredPolygon(new[]
			{
				Pt(r, 0.29f, 0.6f), Pt(r, 0.24f, 0.72f), Pt(r, 0.33f, 0.7f),
			}, accent);
			DrawColoredPolygon(new[]
			{
				Pt(r, 0.71f, 0.6f), Pt(r, 0.76f, 0.72f), Pt(r, 0.67f, 0.7f),
			}, accent);

			DrawLine(Pt(r, 0.5f, 0.18f), Pt(r, 0.5f, 0.04f), Ink, 3f);
			DrawLine(Pt(r, 0.5f, 0.04f), Pt(r, 0.86f, 0.04f), Ink, 3f);
		}

		private void DrawAftermath(Rect2 r, Color sky, Color accent)
		{
			float horizon = 0.64f;
			DrawRect(new Rect2(Pt(r, 0f, horizon), new Vector2(r.Size.X, r.Size.Y * (1f - horizon))),
				sky.Darkened(0.4f));
			DrawRect(new Rect2(Pt(r, 0f, horizon), new Vector2(r.Size.X, r.Size.Y * 0.012f)),
				accent.Darkened(0.2f));
			DrawRect(new Rect2(Pt(r, 0f, horizon), new Vector2(r.Size.X, r.Size.Y * 0.09f)),
				new Color(1f, 0.55f, 0.25f, 0.18f));

			DrawCircle(Pt(r, 0.74f, horizon), r.Size.Y * 0.12f, new Color(accent.R, accent.G, accent.B, 0.85f));

			// 倒下的剪影
			DrawFallenFigure(r, 0.22f, 0.78f, 0.16f);
			DrawFallenFigure(r, 0.42f, 0.82f, 0.2f);
			DrawFallenFigure(r, 0.6f, 0.72f, 0.13f);

			for (int i = 0; i < 5; i++)
			{
				float x = 0.1f + i * 0.18f;
				for (int j = 0; j < 3; j++)
					DrawCircle(Pt(r, x + j * 0.02f, 0.4f - j * 0.08f),
						r.Size.Y * (0.035f - j * 0.006f), new Color(0.7f, 0.7f, 0.68f, 0.2f));
			}
		}

		private void DrawFallenFigure(Rect2 r, float x, float y, float length)
		{
			Vector2 head = Pt(r, x, y);
			Vector2 feet = Pt(r, x + length, y);
			DrawCircle(head + new Vector2(0f, -r.Size.Y * 0.012f), r.Size.Y * 0.026f, Ink);
			DrawLine(head, feet, Ink, r.Size.Y * 0.03f);
			DrawLine(head, Pt(r, x + length * 0.4f, y - 0.06f), Ink, r.Size.Y * 0.018f);
		}
	}
}
