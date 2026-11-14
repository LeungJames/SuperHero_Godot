using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 漫画化表现层：受击墨线、网点、速度线、拟声词与击杀分镜框（设计文档 §4.2.2–§4.2.4）。
/// 只订阅事件做表现，不改任何玩法数值。
/// </summary>
public partial class ComicOverlay : Control
{
	[Export] public ComicStyleData Style { get; set; }

	private static readonly string[] BodyWords = { "POW!", "BAM!", "WHAM!", "THUD!", "CRACK!" };
	private static readonly string[] KillWords = { "K.O.!", "DOWN!", "OUT!", "BOOM!" };

	private sealed class Popup
	{
		public string Text;
		public Vector2 Position;
		public Color Color;
		public float Tilt;
		public float Size;
		public float Age;
		public float Lifetime;
	}

	private readonly List<Popup> _popups = new();
	private readonly RandomNumberGenerator _rng = new();

	private PlayerController _player;
	private PlayerHealth _health;
	private WeaponController _weapon;
	private MeleeController _melee;
	private Camera3D _camera;
	private Font _font;

	private ColorRect _halftone;
	private ColorRect _speedLines;
	private ShaderMaterial _halftoneMaterial;
	private ShaderMaterial _speedLineMaterial;

	private float _halftoneTimer;
	private float _inkTimer;
	private float _panelTimer;
	private float _panelDuration = 0.55f;
	private float _speedBlend;

	public override void _Ready()
	{
		Style ??= new ComicStyleData();
		MouseFilter = MouseFilterEnum.Ignore;
		ZIndex = 5;
		_rng.Randomize();
		_font = ThemeDB.FallbackFont;

		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player != null)
		{
			_health = _player.GetNodeOrNull<PlayerHealth>("Health");
			_weapon = _player.GetNodeOrNull<WeaponController>("Head/Camera3D/ViewModel/Weapon");
			_melee = _player.GetNodeOrNull<MeleeController>("Melee");
			_camera = _player.GetNodeOrNull<Camera3D>("Head/Camera3D");

			if (_health != null)
				_health.Damaged += OnPlayerDamaged;

			if (_weapon != null)
			{
				_weapon.ShotResolved += OnShotResolved;
				_weapon.ReloadStarted += () => Spawn("CLICK!", Crosshair() + new Vector2(0f, 76f), Style.PaperColor, 0.62f);
			}

			if (_melee != null)
				_melee.Resolved += OnMeleeResolved;
		}

		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			// 组挂在 EnemyHealth 上，敌人的根节点是它的父节点
			if (node is not EnemyHealth health)
				continue;

			Subscribe(health);
		}

		// 增援是运行时生成的，同样要有击杀表现
		EnemyHealth.Spawned += Subscribe;

		BuildShaderLayers();
	}

	public override void _ExitTree()
	{
		EnemyHealth.Spawned -= Subscribe;
	}

	private void Subscribe(EnemyHealth health)
	{
		if (health == null)
			return;

		Node3D agent = health.GetParent() as Node3D;
		health.Died += () => OnEnemyDied(agent);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		_halftoneTimer = Mathf.Max(0f, _halftoneTimer - dt);
		_inkTimer = Mathf.Max(0f, _inkTimer - dt);
		_panelTimer = Mathf.Max(0f, _panelTimer - dt);

		for (int i = _popups.Count - 1; i >= 0; i--)
		{
			_popups[i].Age += dt;
			if (_popups[i].Age >= _popups[i].Lifetime)
				_popups.RemoveAt(i);
		}

		UpdateSpeedLines(dt);
		UpdateHalftone();
		QueueRedraw();
	}

	public override void _Draw()
	{
		DrawKillPanel();
		DrawInkFrame();
		DrawPopups();
	}

	// ---------------------------------------------------------------- 事件

	private void OnPlayerDamaged(float amount, DeathCause cause)
	{
		if (amount <= 0f)
			return;

		_inkTimer = Style.HitInkTime;
		_halftoneTimer = Mathf.Max(_halftoneTimer, Style.HalftoneFadeTime * 0.7f);
	}

	private void OnShotResolved(HitscanHit hit)
	{
		if (!hit.HasHit)
			return;

		bool head = hit.Collider is Hitbox box && box.IsHead;
		string text = head ? "HEADSHOT!" : BodyWords[_rng.RandiRange(0, BodyWords.Length - 1)];
		Color color = head ? Style.PaperColor : Style.AccentColor;
		Spawn(text, ToScreen(hit.Point) + RandomJitter(46f), color, head ? 1f : 0.86f);
	}

	private void OnMeleeResolved(Vector3 point, bool hit)
	{
		if (!hit)
			return;

		Spawn("WHACK!", ToScreen(point) + RandomJitter(40f), Style.PaperColor, 0.9f);
	}

	private void OnEnemyDied(Node3D agent)
	{
		Vector3 point = agent != null ? agent.GlobalPosition + Vector3.Up * 1.1f : _player.GlobalPosition;
		Spawn(KillWords[_rng.RandiRange(0, KillWords.Length - 1)], ToScreen(point) + RandomJitter(30f),
			Style.KillColor, 1.25f);

		_panelDuration = Style.KillPanelTime;
		_panelTimer = _panelDuration;
		_halftoneTimer = Mathf.Max(_halftoneTimer, Style.HalftoneFadeTime);
	}

	// ---------------------------------------------------------------- 表现

	private void Spawn(string text, Vector2 position, Color color, float scale)
	{
		_popups.Add(new Popup
		{
			Text = text,
			Position = position,
			Color = color,
			Tilt = _rng.RandfRange(-Style.PopupTilt, Style.PopupTilt),
			Size = Style.PopupFontSize * scale,
			Age = 0f,
			Lifetime = Style.PopupLifetime * Mathf.Lerp(1f, 1.3f, scale - 0.6f),
		});
	}

	private void DrawPopups()
	{
		if (_popups.Count == 0 || _font == null)
			return;

		foreach (Popup popup in _popups)
		{
			float k = popup.Age / popup.Lifetime;
			float pop = k < 0.18f ? Mathf.Lerp(1.5f, 1f, k / 0.18f) : 1f;
			float alpha = k > 0.62f ? 1f - (k - 0.62f) / 0.38f : 1f;
			int fontSize = Mathf.Max(8, Mathf.RoundToInt(popup.Size * pop));

			Vector2 position = popup.Position + new Vector2(0f, -Style.PopupRise * k);
			DrawSetTransform(position, Mathf.DegToRad(popup.Tilt), Vector2.One);

			Vector2 metrics = _font.GetStringSize(popup.Text, HorizontalAlignment.Left, -1, fontSize);
			Vector2 origin = new(-metrics.X * 0.5f, metrics.Y * 0.3f);

			Color halo = Style.PaperColor;
			halo.A = alpha * 0.9f;
			Color ink = Style.InkColor;
			ink.A = alpha;
			Color fill = popup.Color;
			fill.A = alpha;

			DrawStringOutline(_font, origin, popup.Text, HorizontalAlignment.Left, -1, fontSize, 16, halo);
			DrawStringOutline(_font, origin, popup.Text, HorizontalAlignment.Left, -1, fontSize, 9, ink);
			DrawString(_font, origin, popup.Text, HorizontalAlignment.Left, -1, fontSize, fill);
		}

		DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
	}

	private void DrawKillPanel()
	{
		if (_panelTimer <= 0f)
			return;

		float k = 1f - _panelTimer / Mathf.Max(_panelDuration, 0.01f);
		float alpha = Mathf.Min(1f, k * 8f) * Mathf.Min(1f, _panelTimer * 6f);
		float thickness = Style.KillPanelThickness * Mathf.Lerp(0.55f, 1f, Mathf.Min(1f, k * 5f));

		Color ink = Style.InkColor;
		ink.A = alpha;

		Vector2 size = Size;
		DrawRect(new Rect2(0f, 0f, size.X, thickness), ink);
		DrawRect(new Rect2(0f, size.Y - thickness, size.X, thickness), ink);
		DrawRect(new Rect2(0f, 0f, thickness, size.Y), ink);
		DrawRect(new Rect2(size.X - thickness, 0f, thickness, size.Y), ink);
	}

	private void DrawInkFrame()
	{
		if (_inkTimer <= 0f)
			return;

		float alpha = _inkTimer / Mathf.Max(Style.HitInkTime, 0.01f);
		float thickness = Style.HitInkThickness * alpha;

		Color ink = Style.InkColor;
		ink.A = alpha * 0.85f;

		Vector2 size = Size;
		DrawRect(new Rect2(0f, 0f, size.X, thickness), ink);
		DrawRect(new Rect2(0f, size.Y - thickness, size.X, thickness), ink);
		DrawRect(new Rect2(0f, 0f, thickness, size.Y), ink);
		DrawRect(new Rect2(size.X - thickness, 0f, thickness, size.Y), ink);
	}

	private void UpdateSpeedLines(float dt)
	{
		float target = 0f;
		if (_player != null)
		{
			if (_player.IsWallRunning)
				target = 1f;
			else if (_player.IsSliding)
				target = 0.75f;
			else if (new Vector2(_player.Velocity.X, _player.Velocity.Z).Length() > Style.SpeedLineMinSpeed)
				target = 0.3f;
		}

		_speedBlend = Mathf.MoveToward(_speedBlend, target, Style.SpeedLineBlend * dt);

		float strength = _speedBlend * Style.SpeedLineStrength;
		_speedLines.Visible = strength > 0.01f;
		if (_speedLines.Visible)
			_speedLineMaterial.SetShaderParameter("strength", strength);
	}

	private void UpdateHalftone()
	{
		float strength = Style.HalftoneStrength * (_halftoneTimer / Mathf.Max(Style.HalftoneFadeTime, 0.01f));
		_halftone.Visible = strength > 0.01f;
		if (_halftone.Visible)
			_halftoneMaterial.SetShaderParameter("strength", strength);
	}

	// ---------------------------------------------------------------- 构建

	private void BuildShaderLayers()
	{
		_halftone = GetNodeOrNull<ColorRect>("../ComicHalftone");
		_speedLines = GetNodeOrNull<ColorRect>("../ComicSpeedLines");

		if (_halftone != null)
		{
			_halftoneMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/comic_halftone.gdshader") };
			_halftoneMaterial.SetShaderParameter("ink", Style.InkColor);
			_halftoneMaterial.SetShaderParameter("spacing", Style.HalftoneSpacing);
			_halftoneMaterial.SetShaderParameter("edge_start", Style.HalftoneEdgeStart);
			_halftoneMaterial.SetShaderParameter("strength", 0f);
			_halftone.Material = _halftoneMaterial;
			_halftone.Visible = false;
		}

		if (_speedLines != null)
		{
			_speedLineMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/comic_speedlines.gdshader") };
			_speedLineMaterial.SetShaderParameter("ink", Style.InkColor);
			_speedLineMaterial.SetShaderParameter("line_count", Style.SpeedLineCount);
			_speedLineMaterial.SetShaderParameter("inner", Style.SpeedLineInner);
			_speedLineMaterial.SetShaderParameter("strength", 0f);
			_speedLines.Material = _speedLineMaterial;
			_speedLines.Visible = false;
		}
	}

	private Vector2 Crosshair() => Size * 0.5f;

	private Vector2 RandomJitter(float radius)
	{
		float angle = _rng.RandfRange(0f, Mathf.Tau);
		float distance = _rng.RandfRange(radius * 0.35f, radius);
		return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * distance;
	}

	/// <summary>世界坐标投到屏幕上；在相机背后时退回准心位置。</summary>
	private Vector2 ToScreen(Vector3 world)
	{
		if (_camera == null)
			return Crosshair();

		Vector3 local = _camera.GlobalTransform.AffineInverse() * world;
		if (local.Z >= -0.05f)
			return Crosshair();

		return _camera.UnprojectPosition(world);
	}
}
