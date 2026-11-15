using Godot;

/// <summary>Illustrated cards share focus/hover states and keep standard Button input.</summary>
public static class ComicCards
{
    public static Button Create(string title, string description, Texture2D picture, float height = 132, float pictureWidth = 176, bool crop = true)
    {
        var button = new Button {
            CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        var content = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        button.AddChild(content);
        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        content.OffsetLeft = 12; content.OffsetTop = 10; content.OffsetRight = -16; content.OffsetBottom = -10;
        content.AddThemeConstantOverride("separation", 22);
        if (picture != null)
        {
            var image = new TextureRect {
                Texture = picture, CustomMinimumSize = new Vector2(pictureWidth, 0),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = crop ? TextureRect.StretchModeEnum.KeepAspectCovered : TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            content.AddChild(image);
        }
        var words = new VBoxContainer {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        content.AddChild(words);
        var heading = new Label {
            Name = "CardTitle", Text = title, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        heading.AddThemeFontOverride("font", ComicTheme.Bold);
        heading.AddThemeFontSizeOverride("font_size", 22);
        heading.AddThemeColorOverride("font_color", ComicTheme.Ink);
        words.AddChild(heading);
        var detail = new Label {
            Name = "CardDescription", Text = description, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        detail.AddThemeFontSizeOverride("font_size", 16);
        detail.AddThemeColorOverride("font_color", ComicTheme.Muted);
        words.AddChild(detail);
        return button;
    }
}
