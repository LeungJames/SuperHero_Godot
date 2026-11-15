using Godot;

/// <summary>Printed page framing behind modal menus, drawn once per resize.</summary>
public partial class ComicMenuBackdrop : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.035f, 0.045f, 0.07f, 0.84f));
        float margin = Mathf.Min(Size.X, Size.Y) * 0.035f;
        var frame = new Rect2(new Vector2(margin, margin), Size - Vector2.One * margin * 2);
        DrawRect(frame, new Color(ComicTheme.Paper, 0.23f), false, 1);
        DrawLine(new Vector2(margin, margin), new Vector2(margin + 100, margin), ComicTheme.Orange, 5);
        DrawLine(frame.End - new Vector2(100, 0), frame.End, ComicTheme.Orange, 5);
        for (float y = margin * 2; y < Size.Y - margin * 2; y += 14)
            for (float x = margin * 2; x < Size.X * 0.14f; x += 14)
                DrawCircle(new Vector2(x, y), 1.2f, new Color(ComicTheme.Paper, 0.07f));
    }
}
