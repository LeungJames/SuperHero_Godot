using Godot;

/// <summary>
/// HUD 漫画分格底板：斜切纸面 + 网点 + 黑色钢笔边框。只画背景，不接管任何数据。
/// </summary>
[GlobalClass]
public partial class ComicPanel : Control
{
	[Export] public Color Paper { get; set; } = new(0.97f, 0.93f, 0.85f, 0.55f);
	[Export] public Color Ink { get; set; } = new(0.06f, 0.05f, 0.09f, 1f);
	[Export] public float BorderWidth { get; set; } = 3f;
	[Export] public float DotSpacing { get; set; } = 12f;
	[Export] public float DotRadius { get; set; } = 1.2f;
	[Export] public float DotAlpha { get; set; } = 0.10f;
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

		Vector2[] quad =
		{
			new(Skew, 0f),
			new(size.X, 0f),
			new(size.X - Skew, size.Y),
			new(0f, size.Y),
		};

		DrawColoredPolygon(quad, Paper);

		Color dot = new(Ink.R, Ink.G, Ink.B, DotAlpha);
		for (float y = DotSpacing * 0.5f; y < size.Y; y += DotSpacing)
			for (float x = DotSpacing * 0.5f; x < size.X; x += DotSpacing)
				DrawCircle(new Vector2(x, y), DotRadius, dot);

		for (int i = 0; i < quad.Length; i++)
			DrawLine(quad[i], quad[(i + 1) % quad.Length], Ink, BorderWidth, true);
	}
}
