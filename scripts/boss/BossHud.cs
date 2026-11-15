using Godot;

/// <summary>
/// Boss 血条与阶段提示（2.0 设计 §8.4）。挂在 Hud 下，默认隐藏；
/// 控件在代码里搭建，不依赖额外美术资源。
/// </summary>
public partial class BossHud : Control
{
	private BossController _boss;

	private ColorRect _background;
	private Label _name;
	private Label _phase;
	private ProgressBar _bar;
	private Label _weak;
	private StyleBoxFlat _fillStyle;

	private float _hideTimer;
	private float _pulse;

	public bool Bound => _boss != null;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Visible = false;
		BuildUi();
		Callable.From(BindBoss).CallDeferred();
	}

	private void BuildUi()
	{
		_background = new ColorRect
		{
			Name = "Backdrop",
			Color = new Color(0.07f, 0.055f, 0.045f, 0.78f),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_background.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(_background);

		_name = new Label
		{
			Name = "BossName",
			Text = "霍克 · 铁锚帮",
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_name.Position = new Vector2(16f, 6f);
		_name.AddThemeFontSizeOverride("font_size", 20);
		_name.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.72f));
		AddChild(_name);

		_phase = new Label
		{
			Name = "Phase",
			Text = "阶段 I",
			HorizontalAlignment = HorizontalAlignment.Right,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_phase.SetAnchorsPreset(LayoutPreset.TopRight);
		_phase.OffsetLeft = -140f;
		_phase.OffsetTop = 6f;
		_phase.OffsetRight = -16f;
		_phase.AddThemeFontSizeOverride("font_size", 18);
		_phase.AddThemeColorOverride("font_color", new Color(0.92f, 0.52f, 0.24f));
		AddChild(_phase);

		_bar = new ProgressBar
		{
			Name = "HealthBar",
			MinValue = 0,
			MaxValue = 100,
			Value = 100,
			ShowPercentage = false,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_bar.SetAnchorsPreset(LayoutPreset.TopWide);
		_bar.OffsetLeft = 16f;
		_bar.OffsetTop = 38f;
		_bar.OffsetRight = -16f;
		_bar.OffsetBottom = 60f;

		var backgroundStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.16f, 0.13f, 0.11f, 0.9f),
			BorderColor = new Color(0.05f, 0.04f, 0.04f),
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
		};
		_fillStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.78f, 0.32f, 0.12f),
		};
		_bar.AddThemeStyleboxOverride("background", backgroundStyle);
		_bar.AddThemeStyleboxOverride("fill", _fillStyle);
		AddChild(_bar);

		_weak = new Label
		{
			Name = "WeakPoint",
			Text = string.Empty,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_weak.Position = new Vector2(16f, 66f);
		_weak.AddThemeFontSizeOverride("font_size", 16);
		_weak.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.42f));
		AddChild(_weak);
	}

	private void BindBoss()
	{
		_boss = GetTree().GetFirstNodeInGroup("boss") as BossController;
		if (_boss == null)
			return;

		_boss.HealthChanged += OnHealthChanged;
		_boss.PhaseChanged += OnPhaseChanged;
		_boss.WeakPointChanged += OnWeakPointChanged;
		_boss.Engaged += OnEngaged;
		_boss.Defeated += OnDefeated;

		_bar.MaxValue = _boss.MaxHealth;
		_bar.Value = _boss.Health;
		OnPhaseChanged(_boss.CurrentPhase);

		if (_boss.IsEngaged)
			Show();
	}

	private void OnEngaged()
	{
		_hideTimer = 0f;
		Show();
		OnHealthChanged(_boss.Health, _boss.MaxHealth);
	}

	private void OnHealthChanged(float health, float maxHealth)
	{
		if (maxHealth <= 0f)
			return;

		_bar.MaxValue = maxHealth;
		_bar.Value = health;
		_hideTimer = 0f;
	}

	private void OnPhaseChanged(int phase)
	{
		_phase.Text = phase switch
		{
			2 => "阶段 II · 断链",
			3 => "阶段 III · 沉锚",
			_ => "阶段 I",
		};
		_fillStyle.BgColor = phase switch
		{
			3 => new Color(0.92f, 0.2f, 0.1f),
			2 => new Color(0.9f, 0.42f, 0.12f),
			_ => new Color(0.78f, 0.32f, 0.12f),
		};
	}

	private void OnWeakPointChanged(bool exposed)
	{
		_weak.Text = exposed ? "锚链核心暴露 ×2.0" : "绕背寻找锚链核心";
		_weak.AddThemeColorOverride("font_color",
			exposed ? new Color(1f, 0.86f, 0.42f) : new Color(0.72f, 0.68f, 0.6f));
	}

	private void OnDefeated()
	{
		_bar.Value = 0;
		_hideTimer = 2.2f;
	}

	public override void _Process(double delta)
	{
		if (_boss == null || !_boss.IsEngaged)
			return;

		_pulse += (float)delta;
		if (_boss.WeakPointExposed)
		{
			float alpha = 0.6f + 0.4f * Mathf.Sin(_pulse * 8f);
			_weak.Modulate = new Color(1f, 1f, 1f, alpha);
		}
		else
		{
			_weak.Modulate = Colors.White;
		}

		if (_hideTimer <= 0f)
			return;

		_hideTimer -= (float)delta;
		if (_hideTimer <= 0f)
			Hide();
	}
}
