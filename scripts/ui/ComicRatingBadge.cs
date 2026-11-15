using Godot;

/// <summary>A legible printed rating stamp with its supporting stat underneath.</summary>
public partial class ComicRatingBadge : PanelContainer
{
    private Label _name;
    private Label _grade;
    private Label _detail;
    public ComicRatingBadge()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(0, 136);
        AddThemeStyleboxOverride("panel", ComicTheme.Box(ComicTheme.PaperLight, 2, 12));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        AddChild(box);
        _name = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _name.AddThemeFontOverride("font", ComicTheme.Bold);
        _name.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(_name);
        _grade = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _grade.AddThemeFontOverride("font", ComicTheme.Bold);
        _grade.AddThemeFontSizeOverride("font_size", 38);
        box.AddChild(_grade);
        _detail = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _detail.AddThemeFontSizeOverride("font_size", 12);
        _detail.AddThemeColorOverride("font_color", ComicTheme.Muted);
        box.AddChild(_detail);
    }
    public void Present(DimensionRating rating)
    {
        _name.Text = rating.Label;
        _grade.Text = $"{rating.Grade}  +{rating.Awarded}";
        _grade.AddThemeColorOverride("font_color", rating.Grade == "S" ? ComicTheme.Orange.Darkened(0.2f) : rating.Grade == "A" ? ComicTheme.Blue : ComicTheme.Ink);
        _detail.Text = rating.Detail;
    }
}
