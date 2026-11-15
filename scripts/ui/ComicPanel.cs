using Godot;

/// <summary>
/// HUD 漫画分格底板：斜切纸面 + 网点 + 黑色钢笔边框。只画背景，不接管任何数据。
/// </summary>
[GlobalClass]
public partial class ComicPanel : Control
{
	[Export] public Color Paper { get; set; } = new(0.97f, 0.91f, 0.78f, 0.98f);
	[Export] public Color Ink { get; set; } = new(0.09f, 0.10f, 0.13f, 1f);
	[Export] public bool AccentStripe { get; set; } = true;
	[Export] public float BorderWidth { get; set; } = 3f;
	[Export] public float DotSpacing { get; set; } = 6f;
	[Export] public float DotRadius { get; set; } = 0.8f;
	[Export] public float DotAlpha { get; set; } = 0.12f;
	/// <summary>左右斜切量（像素），模仿手贴分格。</summary>
	[Export] public float Skew { get; set; } = 10f;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Resized += QueueRedraw;
	}

	public override void _Draw()
	{
		Vector2 size = Size;
		if (size.X < 4f || size.Y < 4f)
			return;

		float skew = Mathf.Abs(Skew);
		Vector2[] quad =
		{
			new(skew, 0f),
			new(size.X, 0f),
			new(size.X - skew, size.Y),
			new(0f, size.Y),
		};
		Vector2[] shadow = new Vector2[4];
		for (int i = 0; i < 4; i++) shadow[i] = quad[i] + new Vector2(3, 4);
		DrawColoredPolygon(shadow, new Color(Ink, 0.8f));
		DrawColoredPolygon(quad, Paper);

		Color dot = new(Ink.R, Ink.G, Ink.B, DotAlpha);
		for (float y = DotSpacing * 0.5f; y < size.Y; y += DotSpacing)
			for (float x = size.X * 0.58f; x < size.X - skew - 4; x += DotSpacing)
				if (x > size.X * 0.93f - y * 0.75f)
					DrawCircle(new Vector2(x, y), DotRadius, dot);
		if (AccentStripe)
			DrawColoredPolygon(new[] { new Vector2(skew, 1), new Vector2(skew + 8, 1), new Vector2(8, size.Y - 1), new Vector2(0, size.Y - 1) }, ComicTheme.Orange);

		for (int i = 0; i < quad.Length; i++)
			DrawLine(quad[i], quad[(i + 1) % quad.Length], Ink, BorderWidth, true);
	}
}
