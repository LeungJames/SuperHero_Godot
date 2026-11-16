using Godot;

/// <summary>
/// 战斗 HUD：血量、弹药、剩余敌人、高度、目标条、准星、换弹进度、受击与消息提示。
/// 只订阅事件刷新界面，不做玩法判断（设计文档 §7、§9.3）。
/// </summary>
public partial class HudController : Control
{
	private static readonly Color Cream = new(0.96f, 0.92f, 0.86f);
	private static readonly Color Danger = new(0.69f, 0.16f, 0.16f);
	private static readonly Color Warning = new(0.85f, 0.68f, 0.25f);
	private static readonly Color Ink = new(0.09f, 0.07f, 0.06f);

	private const float CrosshairLength = 8f;
	private const float HitMarkerTime = 0.2f;
	private const float HitMarkerPopTime = 0.16f;
	private const float CrosshairFlashTime = 0.1f;
	private const float MessageTime = 0.8f;
	private const float FlashRecoverTime = 0.5f;
	private const float SuppressionTime = 1.2f;

	private PlayerController _player;
	private PlayerHealth _health;
	private WeaponController _weapon;

	private Label _enemiesLabel;
	private Label _objectiveLabel;
	private Label _heightLabel;
	private Label _healthLabel;
	private ProgressBar _healthBar;
	private Label _ammoLabel;
	private Label _weaponLabel;
	private ProgressBar _reloadBar;
	private Label _messageLabel;
	private Label _promptLabel;
	private Label _bannerLabel;
	private Label _hitMarker;
	private ColorRect _damageFlash;
	private ColorRect _fade;

	private ColorRect _crossTop;
	private ColorRect _crossBottom;
	private ColorRect _crossLeft;
	private ColorRect _crossRight;
	private Control _crosshair;

	private StyleBoxFlat _healthFill;
	private float _messageTimer;
	private float _messageDuration = MessageTime;
	private ComicPanel _messagePanel;
	private float _flashAlpha;
	private float _hitMarkerTimer;
	private float _hitMarkerPop;
	private float _crosshairFlashTimer;
	private Color _crosshairFlashColor = Warning;
	private float _enemyPulse;
	private bool _combatVisible = true;
	private int _lastAlive = -1;
	private string _promptText = string.Empty;

	private VignetteOverlay _vignetteOverlay;
	private WarningArrow _warningArrow;
	private float _vignetteTimer;
	private float _arrowTimer;
	private float _arrowDuration = 0.6f;

	public override void _Ready()
	{
		AddToGroup("hud");
		Theme = ComicTheme.Shared;

		_enemiesLabel = GetNode<Label>("EnemiesLabel");
		_objectiveLabel = GetNode<Label>("ObjectiveLabel");
		_heightLabel = GetNode<Label>("HeightLabel");
		_healthLabel = GetNode<Label>("HealthLabel");
		_healthBar = GetNode<ProgressBar>("HealthBar");
		_ammoLabel = GetNode<Label>("AmmoLabel");
		_weaponLabel = GetNode<Label>("WeaponLabel");
		_reloadBar = GetNode<ProgressBar>("ReloadBar");
		_messageLabel = GetNode<Label>("MessageLabel");
		_bannerLabel = GetNode<Label>("BannerLabel");
		_hitMarker = GetNode<Label>("Crosshair/HitMarker");
		_damageFlash = GetNode<ColorRect>("DamageFlash");
		_fade = GetNode<ColorRect>("Fade");

		_crosshair = GetNode<Control>("Crosshair");
		_crossTop = GetNode<ColorRect>("Crosshair/Top");
		_crossBottom = GetNode<ColorRect>("Crosshair/Bottom");
		_crossLeft = GetNode<ColorRect>("Crosshair/Left");
		_crossRight = GetNode<ColorRect>("Crosshair/Right");

		_healthFill = _healthBar.GetThemeStylebox("fill") as StyleBoxFlat;
		StylePaperHud();

		_bannerLabel.Visible = false;
		_reloadBar.Visible = false;
		_messageLabel.Modulate = new Color(1f, 1f, 1f, 0f);
		_hitMarker.Modulate = new Color(1f, 1f, 1f, 0f);
		_hitMarker.PivotOffset = _hitMarker.Size * 0.5f;
		_hitMarker.AddThemeFontSizeOverride("font_size", 46);
		_hitMarker.AddThemeConstantOverride("outline_size", 8);
		_hitMarker.AddThemeColorOverride("font_outline_color", Ink);
		_damageFlash.Color = new Color(Danger.R, Danger.G, Danger.B, 0f);
		_fade.Color = new Color(0.96f, 0.90f, 0.80f, 0f);
		SetCrosshairGap(6f);

		// 暗角垫在最底层，方向箭头盖在最上层；都只做提示，不拦输入
		_vignetteOverlay = new VignetteOverlay { Name = "SuppressionVignette", MouseFilter = MouseFilterEnum.Ignore };
		AddChild(_vignetteOverlay);
		_vignetteOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MoveChild(_vignetteOverlay, 0);

		_warningArrow = new WarningArrow { Name = "DirectionalWarning", MouseFilter = MouseFilterEnum.Ignore };
		AddChild(_warningArrow);
		_warningArrow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_weapon = GetTree().GetFirstNodeInGroup("weapon") as WeaponController;

		if (_player != null)
		{
			_health = _player.GetNode<PlayerHealth>("Health");
			_health.HealthChanged += OnHealthChanged;
			_health.Damaged += OnDamaged;
			OnHealthChanged(_health.Health, _health.MaxHealth);
		}

		if (_weapon != null)
		{
			_weapon.AmmoChanged += OnAmmoChanged;
			_weapon.ShotResolved += OnShotResolved;
			_weapon.KnifeStateChanged += OnKnifeStateChanged;
			OnAmmoChanged(_weapon.MagAmmo, _weapon.ReserveAmmo);
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		UpdateTimers(dt);
		UpdateCrosshair();
		UpdateReloadBar();
	}

	private void StylePaperHud()
	{
		foreach (Label label in new[] { _healthLabel, _ammoLabel, _weaponLabel, _enemiesLabel, _objectiveLabel, _heightLabel })
		{
			label.AddThemeColorOverride("font_color", ComicTheme.Ink);
			label.AddThemeConstantOverride("outline_size", 0);
			label.AddThemeFontOverride("font", ComicTheme.Bold);
			label.MouseFilter = MouseFilterEnum.Ignore;
		}
		_heightLabel.AddThemeColorOverride("font_color", ComicTheme.Orange.Darkened(0.25f));
		var fill = ComicTheme.Box(ComicTheme.Orange, 1, 0);
		_healthBar.AddThemeStyleboxOverride("fill", fill);
		_healthBar.AddThemeStyleboxOverride("background", ComicTheme.Box(ComicTheme.Ink, 2, 0));
		_healthFill = fill;
		foreach (string name in new[] { "ObjectivePanel", "EnemyPanel", "HeightPanel" })
		{
			var panel = new ComicPanel { Name = name, MouseFilter = MouseFilterEnum.Ignore, AccentStripe = name == "ObjectivePanel" };
			AddChild(panel);
			MoveChild(panel, 1);
			if (name == "ObjectivePanel") { panel.Position = new Vector2(18, 18); panel.Size = new Vector2(320, 48); }
			if (name == "EnemyPanel") { panel.Position = new Vector2(18, 76); panel.Size = new Vector2(250, 40); }
			if (name == "HeightPanel") {
				panel.AnchorLeft = 1; panel.AnchorRight = 1;
				panel.OffsetLeft = -144; panel.OffsetRight = -18; panel.OffsetTop = 18; panel.OffsetBottom = 62;
			}
		}
		_messagePanel = new ComicPanel { Name = "MessagePanel", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		AddChild(_messagePanel);
		MoveChild(_messagePanel, 1);
		_messageLabel.AddThemeFontOverride("font", ComicTheme.Bold);
		_messageLabel.AddThemeConstantOverride("outline_size", 0);
		_messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_messageLabel.VerticalAlignment = VerticalAlignment.Center;
	}

	// ------------------------------------------------------------ 外部指令

	public void SetEnemyCount(int alive, int total)
	{
		// GameManager 的 _Ready 早于 HUD 的 _Ready，这里按需补齐引用
		_enemiesLabel ??= GetNode<Label>("EnemiesLabel");
		_enemiesLabel.Text = $"剩余敌人 {alive}/{total}";
		_enemiesLabel.PivotOffset = _enemiesLabel.Size * 0.5f;
		if (alive != _lastAlive && _lastAlive >= 0)
			_enemyPulse = 1f;
		_lastAlive = alive;
	}

	public void SetHeight(float height)
	{
		_heightLabel ??= GetNode<Label>("HeightLabel");
		_heightLabel.Text = $"{Mathf.RoundToInt(height)} m";
	}

	public void SetObjective(string text)
	{
		_objectiveLabel ??= GetNode<Label>("ObjectiveLabel");
		_objectiveLabel.Text = text;
	}

	public void SetCombatVisible(bool visible)
	{
		_combatVisible = visible;
		_crosshair.Visible = visible;
		_healthLabel.Visible = visible;
		_healthBar.Visible = visible;
		_ammoLabel.Visible = visible;
		_weaponLabel.Visible = visible;
		_enemiesLabel.Visible = visible;
		_objectiveLabel.Visible = visible;
		_heightLabel.Visible = visible;
		foreach (string name in new[] { "HealthPanel", "AmmoPanel", "ObjectivePanel", "EnemyPanel", "HeightPanel" })
			if (GetNodeOrNull<Control>(name) is Control panel) panel.Visible = visible;
		_reloadBar.Visible = visible && _weapon != null && _weapon.IsReloading;

		_promptLabel ??= GetNodeOrNull<Label>("ExecutionPrompt");
		if (_promptLabel != null)
			_promptLabel.Visible = visible && !string.IsNullOrEmpty(_promptText);
	}

	public void ShowBanner(string text)
	{
		_bannerLabel.Text = text;
		_bannerLabel.Visible = true;
	}

	public void HideBanner()
	{
		_bannerLabel.Visible = false;
	}

	public void ShowMessage(string text, Color color)
	{
		_messageLabel.Text = text;
		bool radio = text.StartsWith("无线电");
		_messageLabel.AddThemeColorOverride("font_color", color.G < 0.3f && color.R > 0.5f ? ComicTheme.Red : ComicTheme.Ink);
		_messageLabel.AnchorLeft = 0.5f; _messageLabel.AnchorRight = 0.5f;
		_messageLabel.AnchorTop = radio ? 1 : 0.5f; _messageLabel.AnchorBottom = _messageLabel.AnchorTop;
		// Radio captions occupy the space between the bottom HUD cards, clear of reload hands.
		_messageLabel.OffsetLeft = radio ? -225 : -330; _messageLabel.OffsetRight = radio ? 225 : 330;
		_messageLabel.OffsetTop = radio ? -84 : 88; _messageLabel.OffsetBottom = radio ? -26 : 136;
		_messageLabel.AddThemeFontSizeOverride("font_size", radio ? 18 : 20);
		_messagePanel.AnchorLeft = 0.5f; _messagePanel.AnchorRight = 0.5f;
		_messagePanel.AnchorTop = _messageLabel.AnchorTop; _messagePanel.AnchorBottom = _messageLabel.AnchorBottom;
		_messagePanel.OffsetLeft = radio ? -241 : -350; _messagePanel.OffsetRight = radio ? 241 : 350;
		_messagePanel.OffsetTop = _messageLabel.OffsetTop - 6; _messagePanel.OffsetBottom = _messageLabel.OffsetBottom + 6;
		_messageDuration = radio ? Mathf.Clamp(text.Length * 0.09f, 2.8f, 5f) : MessageTime;
		_messageTimer = _messageDuration;
		_messagePanel.Visible = true;
	}

	public void SetPrompt(string text)
	{
		if (_promptText == text)
			return;

		_promptText = text;
		_promptLabel ??= GetNodeOrNull<Label>("ExecutionPrompt");
		if (_promptLabel == null)
			return;

		_promptLabel.Text = text;
		_promptLabel.Visible = _combatVisible && !string.IsNullOrEmpty(text);
	}

	/// <summary>演出淡出：0 透明 → 1 全米白。</summary>
	public void SetFade(float alpha)
	{
		_fade.Color = new Color(0.96f, 0.90f, 0.80f, Mathf.Clamp(alpha, 0f, 1f));
	}

	/// <summary>被压制：屏幕边缘轻微暗角（§2.7）。</summary>
	public void ShowSuppression()
	{
		_vignetteTimer = SuppressionTime;
	}

	/// <summary>
	/// 方向提示：从屏幕中心指向该世界位置，在屏幕边缘画 0.6s 的箭头（§2.7 包抄提示）。
	/// 只给方向，不给精确位置。
	/// </summary>
	public void ShowDirectionalWarning(Vector3 worldPosition, Color color, float duration = 0.6f)
	{
		Camera3D camera = GetViewport()?.GetCamera3D();
		if (camera == null || _warningArrow == null)
			return;

		Vector2 screen = camera.UnprojectPosition(worldPosition + Vector3.Up * 1.5f);
		Vector2 direction = screen - GetViewportRect().Size * 0.5f;
		if (direction.LengthSquared() < 1f)
			return;

		_warningArrow.Direction = direction.Normalized();
		_warningArrow.Tint = color;
		_arrowTimer = duration;
		_arrowDuration = Mathf.Max(0.01f, duration);
	}

	// ------------------------------------------------------------ 事件

	private void OnHealthChanged(float health, float max)
	{
		_healthLabel.Text = $"HP {Mathf.RoundToInt(health)}";
		_healthBar.MaxValue = max;
		_healthBar.Value = health;

		if (_healthFill != null)
		{
			if (health <= 30f)
				_healthFill.BgColor = Danger;
			else if (health <= 50f)
				_healthFill.BgColor = Warning;
			else
				_healthFill.BgColor = ComicTheme.Orange;
		}
	}

	private void OnDamaged(float amount, DeathCause cause)
	{
		if (!_combatVisible)
			return;

		_flashAlpha = Mathf.Min(0.8f, amount / 100f * 0.8f);

		if (cause == DeathCause.Fall)
			ShowMessage($"摔伤 -{Mathf.RoundToInt(amount)}", Danger);
	}

	private void OnAmmoChanged(int mag, int reserve)
	{
		_weaponLabel.Text = _weapon?.Data?.DisplayName ?? "—";

		if (mag <= 0)
			_ammoLabel.Text = "0 / " + reserve;
		else
			_ammoLabel.Text = $"{mag} / {reserve}";

		_ammoLabel.AddThemeColorOverride("font_color", mag <= 3 ? Danger : ComicTheme.Ink);
	}

	/// <summary>刀槽：武器名显示“刀”，弹药位显示“近战”，切回枪时恢复。</summary>
	private void OnKnifeStateChanged(bool knife)
	{
		if (!knife)
		{
			OnAmmoChanged(_weapon?.MagAmmo ?? 0, _weapon?.ReserveAmmo ?? 0);
			return;
		}

		_weaponLabel.Text = LoadoutSystem.KnifeName;
		_ammoLabel.Text = "近战";
		_ammoLabel.AddThemeColorOverride("font_color", ComicTheme.Ink);
	}

	private void OnShotResolved(HitscanHit hit)
	{
		if (!hit.HasHit || hit.Collider is not Hitbox hitbox)
			return;

		bool head = hitbox.IsHead;
		_hitMarkerTimer = HitMarkerTime;
		_hitMarkerPop = 1f;
		_hitMarker.Text = head ? "✕" : "×";
		_hitMarker.AddThemeColorOverride("font_color", head ? Danger : Warning);
		_hitMarker.AddThemeFontSizeOverride("font_size", head ? 58 : 46);

		// 准星本体也闪一下命中色，跟随视线就能确认打中
		_crosshairFlashTimer = CrosshairFlashTime;
		_crosshairFlashColor = head ? Danger : Warning;
	}

	private void SetCrosshairColor(Color color)
	{
		foreach (ColorRect bar in new[] { _crossTop, _crossBottom, _crossLeft, _crossRight })
			bar.Color = new Color(color.R, color.G, color.B, 0.9f);
	}

	// ------------------------------------------------------------ 刷新

	private void UpdateTimers(float dt)
	{
		if (_messageTimer > 0f)
		{
			_messageTimer -= dt;
			float alpha = Mathf.Clamp(_messageTimer / Mathf.Min(_messageDuration, 0.3f), 0f, 1f);
			_messageLabel.Modulate = new Color(1f, 1f, 1f, alpha);
			_messagePanel.Modulate = _messageLabel.Modulate;
		}
		else if (_messageLabel.Modulate.A > 0f)
		{
			_messageLabel.Modulate = new Color(1f, 1f, 1f, 0f);
			_messagePanel.Visible = false;
		}

		if (_hitMarkerTimer > 0f)
		{
			_hitMarkerTimer -= dt;
			float alpha = Mathf.Clamp(_hitMarkerTimer / HitMarkerTime, 0f, 1f);
			_hitMarker.Modulate = new Color(1f, 1f, 1f, alpha);
		}
		else if (_hitMarker.Modulate.A > 0f)
		{
			_hitMarker.Modulate = new Color(1f, 1f, 1f, 0f);
		}

		if (_hitMarkerPop > 0f)
		{
			_hitMarkerPop = Mathf.Max(0f, _hitMarkerPop - dt / HitMarkerPopTime);
			float scale = 1f + 0.9f * _hitMarkerPop;
			_hitMarker.Scale = new Vector2(scale, scale);
		}

		if (_crosshairFlashTimer > 0f)
		{
			_crosshairFlashTimer -= dt;
			SetCrosshairColor(_crosshairFlashTimer > 0f ? _crosshairFlashColor : Cream);
		}

		if (_flashAlpha > 0f)
		{
			_flashAlpha = Mathf.Max(0f, _flashAlpha - dt / FlashRecoverTime * 0.8f);
			_damageFlash.Color = new Color(Danger.R, Danger.G, Danger.B, _flashAlpha);
		}

		if (_enemyPulse > 0f)
		{
			_enemyPulse = Mathf.Max(0f, _enemyPulse - dt / 0.2f);
			float scale = Mathf.Lerp(1f, 1.15f, _enemyPulse);
			_enemiesLabel.Scale = new Vector2(scale, scale);
		}

		if (_vignetteTimer > 0f)
		{
			_vignetteTimer = Mathf.Max(0f, _vignetteTimer - dt);
			_vignetteOverlay.Strength = Mathf.Clamp(_vignetteTimer / SuppressionTime, 0f, 1f);
			_vignetteOverlay.QueueRedraw();
		}
		else if (_vignetteOverlay.Strength > 0f)
		{
			_vignetteOverlay.Strength = 0f;
			_vignetteOverlay.QueueRedraw();
		}

		if (_arrowTimer > 0f)
		{
			_arrowTimer = Mathf.Max(0f, _arrowTimer - dt);
			_warningArrow.Alpha = Mathf.Clamp(_arrowTimer / _arrowDuration, 0f, 1f);
			_warningArrow.QueueRedraw();
		}
		else if (_warningArrow.Alpha > 0f)
		{
			_warningArrow.Alpha = 0f;
			_warningArrow.QueueRedraw();
		}
	}

	private void UpdateCrosshair()
	{
		if (_weapon == null || !_combatVisible)
			return;

		SetCrosshairGap(4f + _weapon.CurrentSpreadDegrees * 4f);
	}

	private void SetCrosshairGap(float gap)
	{
		_crossTop.OffsetTop = 32f - gap - CrosshairLength;
		_crossTop.OffsetBottom = 32f - gap;
		_crossBottom.OffsetTop = 32f + gap;
		_crossBottom.OffsetBottom = 32f + gap + CrosshairLength;
		_crossLeft.OffsetLeft = 32f - gap - CrosshairLength;
		_crossLeft.OffsetRight = 32f - gap;
		_crossRight.OffsetLeft = 32f + gap;
		_crossRight.OffsetRight = 32f + gap + CrosshairLength;
	}

	private void UpdateReloadBar()
	{
		bool reloading = _combatVisible && _weapon != null && _weapon.IsReloading;
		if (_reloadBar.Visible != reloading)
			_reloadBar.Visible = reloading;

		if (reloading)
			_reloadBar.Value = _weapon.ReloadProgress;
	}

	/// <summary>屏幕四边向内的暗角，避免受击时糊住画面中心。</summary>
	private sealed partial class VignetteOverlay : Control
	{
		public float Strength { get; set; }

		public override void _Draw()
		{
			if (Strength <= 0.001f)
				return;

			Vector2 size = Size;
			float band = Mathf.Min(size.X, size.Y) * 0.16f;
			Color edge = new(0.04f, 0.02f, 0.02f, 0.34f * Strength);
			Color clear = new(0.04f, 0.02f, 0.02f, 0f);
			Color[] colors = { edge, edge, clear, clear };

			DrawPolygon(new[] { new Vector2(0f, 0f), new Vector2(size.X, 0f), new Vector2(size.X, band), new Vector2(0f, band) }, colors);
			DrawPolygon(new[] { new Vector2(0f, size.Y - band), new Vector2(size.X, size.Y - band), new Vector2(size.X, size.Y), new Vector2(0f, size.Y) }, colors);
			DrawPolygon(new[] { new Vector2(0f, 0f), new Vector2(band, 0f), new Vector2(band, size.Y), new Vector2(0f, size.Y) }, colors);
			DrawPolygon(new[] { new Vector2(size.X - band, 0f), new Vector2(size.X, 0f), new Vector2(size.X, size.Y), new Vector2(size.X - band, size.Y) }, colors);
		}
	}

	/// <summary>屏幕边缘的方向箭头：指向威胁所在的屏幕方向（不暴露具体位置）。</summary>
	private sealed partial class WarningArrow : Control
	{
		public Vector2 Direction { get; set; } = Vector2.Down;
		public Color Tint { get; set; } = new(0.95f, 0.40f, 0.22f);
		public float Alpha { get; set; }

		public override void _Draw()
		{
			if (Alpha <= 0.001f)
				return;

			Vector2 size = Size;
			Vector2 center = size * 0.5f;
			Vector2 dir = Direction.LengthSquared() > 0.001f ? Direction.Normalized() : Vector2.Down;

			// 沿方向投到屏幕内框上，箭头贴在边缘留 28px
			float halfX = Mathf.Max(1f, center.X - 28f);
			float halfY = Mathf.Max(1f, center.Y - 28f);
			float t = Mathf.Min(
				Mathf.Abs(dir.X) > 0.0001f ? halfX / Mathf.Abs(dir.X) : 9999f,
				Mathf.Abs(dir.Y) > 0.0001f ? halfY / Mathf.Abs(dir.Y) : 9999f);

			Vector2 tip = center + dir * t;
			Vector2 tangent = new(-dir.Y, dir.X);
			Vector2 baseCenter = tip - dir * 22f;
			Color color = new(Tint.R, Tint.G, Tint.B, Alpha);
			Color[] colors = { color, color, color };
			DrawPolygon(new[] { tip, baseCenter + tangent * 12f, baseCenter - tangent * 12f }, colors);
		}
	}
}
