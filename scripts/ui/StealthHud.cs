using Godot;
using System.Collections.Generic;

/// <summary>
/// 潜行可读反馈（2.0 设计 §3.6）：屏幕中央警戒眼睛（三档填充）+ 敌人头顶 ?/! 图标。
/// 只显示警戒等级与短时图标，不显示敌人位置；由 <see cref="StealthDirector"/> 运行时创建。
/// </summary>
public partial class StealthHud : Control
{
	public StealthDirector Source { get; set; }

	private readonly List<IconToast> _icons = new();

	private StealthAlertLevel _level = StealthAlertLevel.None;

	private static readonly Color Level0Color = new(0.95f, 0.83f, 0.28f);
	private static readonly Color Level1Color = new(0.95f, 0.55f, 0.22f);
	private static readonly Color Level2Color = new(0.88f, 0.22f, 0.18f);
	private static readonly Color IconBack = new(0.05f, 0.03f, 0.03f, 0.72f);

	private struct IconToast
	{
		public Vector3 World;
		public string Text;
		public Color Tint;
		public float TimeLeft;
		public float Duration;
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
	}

	public void SetLevel(StealthAlertLevel level) => _level = level;

	/// <summary>在敌人头顶显示 1.2s 的 ? / !。</summary>
	public void ShowIcon(Vector3 worldPosition, string text, Color color)
	{
		float duration = Mathf.Max(0.2f, Source?.Config?.IconDuration ?? 1.2f);

		// 同一敌人短时间重复发现时只刷新，不叠加一堆图标
		for (int i = 0; i < _icons.Count; i++)
		{
			IconToast existing = _icons[i];
			if (existing.Text != text || existing.World.DistanceSquaredTo(worldPosition) > 0.01f)
				continue;

			existing.TimeLeft = duration;
			existing.Duration = duration;
			_icons[i] = existing;
			return;
		}

		_icons.Add(new IconToast
		{
			World = worldPosition,
			Text = text,
			Tint = color,
			TimeLeft = duration,
			Duration = duration,
		});
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		for (int i = _icons.Count - 1; i >= 0; i--)
		{
			IconToast icon = _icons[i];
			icon.TimeLeft -= dt;
			if (icon.TimeLeft <= 0f)
				_icons.RemoveAt(i);
			else
				_icons[i] = icon;
		}

		QueueRedraw();
	}

	public override void _Draw()
	{
		DrawAlertEye();
		DrawIcons();
	}

	private void DrawAlertEye()
	{
		if (_level == StealthAlertLevel.None)
			return;

		Color color = _level switch
		{
			StealthAlertLevel.L2 => Level2Color,
			StealthAlertLevel.L1 => Level1Color,
			_ => Level0Color,
		};

		Vector2 center = new(Size.X * 0.5f, 92f);
		const float radius = 15f;

		// 眼睛轮廓：圆 + 瞳孔，避免和准星混淆
		DrawArc(center, radius, 0f, Mathf.Tau, 32, color, 2.4f);
		DrawCircle(center, 5f, color);

		// 三档填充：L0 1/3、L1 2/3、L2 满
		int filled = (int)_level;
		for (int i = 0; i < 3; i++)
		{
			Vector2 pip = center + new Vector2(-22f + i * 22f, 30f);
			var rect = new Rect2(pip - new Vector2(8f, 3f), new Vector2(16f, 6f));
			if (i < filled)
				DrawRect(rect, color, true);
			else
				DrawRect(rect, new Color(color, 0.35f), false, 1.4f);
		}
	}

	private void DrawIcons()
	{
		if (_icons.Count == 0)
			return;

		Camera3D camera = GetViewport()?.GetCamera3D();
		if (camera == null)
			return;

		Font font = ThemeDB.FallbackFont;
		Vector2 screen = GetViewportRect().Size;

		foreach (IconToast icon in _icons)
		{
			Vector3 world = icon.World + Vector3.Up * 2.2f;
			if (camera.IsPositionBehind(world))
				continue;

			Vector2 point = camera.UnprojectPosition(world);
			if (point.X < -40f || point.Y < -40f || point.X > screen.X + 40f || point.Y > screen.Y + 40f)
				continue;

			float alpha = Mathf.Clamp(icon.TimeLeft / Mathf.Max(icon.Duration, 0.01f), 0f, 1f);
			Color color = new(icon.Tint.R, icon.Tint.G, icon.Tint.B, alpha);

			DrawCircle(point, 14f, new Color(IconBack.R, IconBack.G, IconBack.B, IconBack.A * alpha));
			DrawString(font, point + new Vector2(-6f, 8f), icon.Text,
				HorizontalAlignment.Left, -1f, 26, color);
		}
	}
}
