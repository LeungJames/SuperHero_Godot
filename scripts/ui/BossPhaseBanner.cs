using Godot;

/// <summary>
/// Boss 阶段切换的漫画分格横幅（2.0 设计 §8.4）：1 格漫画 + 拟声词，
/// 0.8s 内淡入淡出，不暂停场景树、不接管输入。
/// </summary>
public partial class BossPhaseBanner : CanvasLayer
{
	private Control _root;
	private Panel _panel;
	private Label _title;
	private Label _sfx;

	private float _timer;
	private float _duration = 0.8f;

	public bool IsShowing => _timer > 0f;

	public override void _Ready()
	{
		Layer = 16;
		ProcessMode = ProcessModeEnum.Always;

		_root = new Control { Name = "BannerRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		_panel = new Panel { Name = "BannerPanel", MouseFilter = Control.MouseFilterEnum.Ignore };
		_panel.AnchorLeft = 0.5f;
		_panel.AnchorRight = 0.5f;
		_panel.OffsetLeft = -300f;
		_panel.OffsetRight = 300f;
		_panel.OffsetTop = 92f;
		_panel.OffsetBottom = 196f;
		_root.AddChild(_panel);

		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.94f, 0.88f, 0.74f, 0.95f),
			BorderColor = new Color(0.07f, 0.06f, 0.05f),
			BorderWidthLeft = 6,
			BorderWidthTop = 6,
			BorderWidthRight = 6,
			BorderWidthBottom = 6,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
		};
		style.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
		style.ShadowSize = 6;
		_panel.AddThemeStyleboxOverride("panel", style);

		_title = new Label
		{
			Name = "Title",
			Text = string.Empty,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_title.AddThemeFontSizeOverride("font_size", 36);
		_title.AddThemeColorOverride("font_color", new Color(0.08f, 0.07f, 0.06f));
		_panel.AddChild(_title);

		_sfx = new Label
		{
			Name = "Sfx",
			Text = string.Empty,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_sfx.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_sfx.OffsetRight = -18f;
		_sfx.OffsetBottom = -12f;
		_sfx.AddThemeFontSizeOverride("font_size", 24);
		_sfx.AddThemeColorOverride("font_color", new Color(0.72f, 0.24f, 0.08f));
		_panel.AddChild(_sfx);

		_root.Visible = false;
	}

	public void ShowPhase(int phase, string title, string sfx, float duration = 0.8f)
	{
		_title.Text = title;
		_sfx.Text = sfx;
		_duration = Mathf.Max(duration, 0.2f);
		_timer = _duration;
		_root.Visible = true;
		_root.Modulate = new Color(1f, 1f, 1f, 0f);
		_panel.Scale = new Vector2(1.08f, 1.08f);

		if (!IsInsideTree())
			return;
	}

	public override void _Process(double delta)
	{
		if (_timer <= 0f)
			return;

		_timer = Mathf.Max(0f, _timer - (float)delta);
		float elapsed = _duration - _timer;
		float fadeIn = 0.12f;
		float fadeOut = 0.18f;
		float alpha;

		if (elapsed < fadeIn)
			alpha = elapsed / fadeIn;
		else if (_timer < fadeOut)
			alpha = _timer / fadeOut;
		else
			alpha = 1f;

		_root.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f));
		float scale = Mathf.Lerp(1.08f, 1f, Mathf.Clamp(elapsed / fadeIn, 0f, 1f));
		_panel.Scale = new Vector2(scale, scale);

		if (_timer <= 0f)
			_root.Visible = false;
	}
}
