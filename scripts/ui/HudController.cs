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

	private const float CrosshairLength = 8f;
	private const float HitMarkerTime = 0.12f;
	private const float MessageTime = 0.8f;
	private const float FlashRecoverTime = 0.5f;

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
	private float _flashAlpha;
	private float _hitMarkerTimer;
	private float _enemyPulse;
	private bool _combatVisible = true;
	private int _lastAlive = -1;
	private string _promptText = string.Empty;

	public override void _Ready()
	{
		AddToGroup("hud");

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

		_bannerLabel.Visible = false;
		_reloadBar.Visible = false;
		_messageLabel.Modulate = new Color(1f, 1f, 1f, 0f);
		_hitMarker.Modulate = new Color(1f, 1f, 1f, 0f);
		_damageFlash.Color = new Color(Danger.R, Danger.G, Danger.B, 0f);
		_fade.Color = new Color(0.96f, 0.90f, 0.80f, 0f);
		SetCrosshairGap(6f);

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
		_heightLabel.Text = $"高度 {Mathf.RoundToInt(height)}m";
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
		_messageLabel.AddThemeColorOverride("font_color", color);
		_messageTimer = MessageTime;
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
				_healthFill.BgColor = Cream;
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
			_ammoLabel.Text = "0 / " + reserve + "   按 R 换弹";
		else
			_ammoLabel.Text = $"{mag} / {reserve}";

		_ammoLabel.AddThemeColorOverride("font_color", mag <= 3 ? Danger : Cream);
	}

	private void OnShotResolved(HitscanHit hit)
	{
		if (!hit.HasHit || hit.Collider is not Hitbox hitbox)
			return;

		_hitMarkerTimer = HitMarkerTime;
		_hitMarker.Text = "×";
		_hitMarker.AddThemeColorOverride("font_color", hitbox.IsHead ? Danger : Cream);
		_hitMarker.Scale = hitbox.IsHead ? new Vector2(1.4f, 1.4f) : Vector2.One;
	}

	// ------------------------------------------------------------ 刷新

	private void UpdateTimers(float dt)
	{
		if (_messageTimer > 0f)
		{
			_messageTimer -= dt;
			float alpha = Mathf.Clamp(_messageTimer / MessageTime, 0f, 1f);
			_messageLabel.Modulate = new Color(1f, 1f, 1f, alpha);
		}
		else if (_messageLabel.Modulate.A > 0f)
		{
			_messageLabel.Modulate = new Color(1f, 1f, 1f, 0f);
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
}
