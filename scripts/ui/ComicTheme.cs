using Godot;
using System.Collections.Generic;

/// <summary>The shared print vocabulary used by every game screen.</summary>
public static class ComicTheme
{
    public static readonly Color Paper = new("f5e5c5");
    public static readonly Color PaperLight = new("fff2d7");
    public static readonly Color Ink = new("171a21");
    public static readonly Color Muted = new("6a6259");
    public static readonly Color Orange = new("ed791e");
    public static readonly Color Red = new("b83b36");
    public static readonly Color Blue = new("356579");
    private static Theme _theme;
    private static Font _body;
    private static Font _bold;
    private static readonly Dictionary<string, Texture2D> Icons = new();
    public static Font Body => _body ??= GD.Load<FontFile>("res://art/ui/fonts/SuperHeroSans-Medium.ttf") ?? ThemeDB.FallbackFont;
    public static Font Bold => _bold ??= GD.Load<FontFile>("res://art/ui/fonts/SuperHeroSans-Bold.ttf") ?? ThemeDB.FallbackFont;
    public static Theme Shared => _theme ??= Build();

    public static StyleBoxFlat Box(Color fill, int border = 2, float padding = 12f)
    {
        return new StyleBoxFlat {
            BgColor = fill, BorderColor = Ink,
            BorderWidthLeft = border, BorderWidthTop = border, BorderWidthRight = border, BorderWidthBottom = border,
            ContentMarginLeft = padding, ContentMarginTop = padding * 0.6f,
            ContentMarginRight = padding, ContentMarginBottom = padding * 0.6f,
            CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1, CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1,
        };
    }

    private static Theme Build()
    {
        var theme = new Theme { DefaultFont = Body, DefaultFontSize = 18 };
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_color", "RichTextLabel", Ink);
        theme.SetConstant("separation", "VBoxContainer", 10);
        theme.SetConstant("separation", "HBoxContainer", 12);
        foreach (string type in new[] { "Button", "OptionButton", "CheckButton", "CheckBox" })
        {
            theme.SetFont("font", type, Bold);
            theme.SetFontSize("font_size", type, 18);
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
                theme.SetColor(state, type, Ink);
            theme.SetColor("font_disabled_color", type, Muted);
            theme.SetStylebox("normal", type, Box(PaperLight));
            theme.SetStylebox("hover", type, Box(new Color("f8c674")));
            theme.SetStylebox("pressed", type, Box(Orange));
            theme.SetStylebox("disabled", type, Box(new Color("d8cdb9"), 1));
            var focus = Box(new Color(0, 0, 0, 0), 3, 0);
            focus.BorderColor = Orange;
            focus.ExpandMarginLeft = 3; focus.ExpandMarginTop = 3;
            focus.ExpandMarginRight = 3; focus.ExpandMarginBottom = 3;
            theme.SetStylebox("focus", type, focus);
            theme.SetConstant("h_separation", type, 12);
            theme.SetConstant("icon_max_width", type, 30);
        }
        var panel = Box(Paper, 3, 28);
        panel.ContentMarginTop = 24; panel.ContentMarginBottom = 24;
        panel.ShadowColor = new Color(Ink, 0.65f); panel.ShadowSize = 0; panel.ShadowOffset = new Vector2(8, 8);
        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetStylebox("panel", "Panel", panel);
        theme.SetIcon("arrow", "OptionButton", Icon("down"));
        theme.SetIcon("checked", "CheckButton", Icon("toggle-on"));
        theme.SetIcon("unchecked", "CheckButton", Icon("toggle-off"));
        theme.SetStylebox("panel", "PopupMenu", Box(PaperLight, 2, 14));
        theme.SetStylebox("hover", "PopupMenu", Box(Orange, 0, 14));
        theme.SetColor("font_color", "PopupMenu", Ink);
        theme.SetColor("font_hover_color", "PopupMenu", Ink);
        theme.SetStylebox("panel", "TabContainer", Box(PaperLight, 2, 18));
        theme.SetStylebox("tab_selected", "TabContainer", Box(Orange, 2, 20));
        theme.SetStylebox("tab_unselected", "TabContainer", Box(Paper, 2, 20));
        theme.SetStylebox("tab_hovered", "TabContainer", Box(new Color("f8c674"), 2, 20));
        theme.SetColor("font_selected_color", "TabContainer", Ink);
        theme.SetColor("font_unselected_color", "TabContainer", Muted);
        theme.SetColor("font_hovered_color", "TabContainer", Ink);
        theme.SetFont("font", "TabContainer", Bold);
        theme.SetFontSize("font_size", "TabContainer", 18);
        theme.SetStylebox("background", "ProgressBar", Box(Ink, 2, 0));
        theme.SetStylebox("fill", "ProgressBar", Box(Orange, 1, 0));
        var rail = Box(new Color("c6bda9"), 1, 0); rail.ContentMarginTop = 3; rail.ContentMarginBottom = 3;
        theme.SetStylebox("slider", "HSlider", rail);
        theme.SetStylebox("grabber_area", "HSlider", Box(Orange, 0, 0));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Box(Orange, 0, 0));
        theme.SetIcon("grabber", "HSlider", Icon("slider"));
        theme.SetIcon("grabber_highlight", "HSlider", Icon("slider"));
        theme.SetStylebox("scroll", "VScrollBar", Box(new Color("d4c6af"), 0, 0));
        theme.SetStylebox("grabber", "VScrollBar", Box(Ink, 0, 4));
        theme.SetStylebox("grabber_highlight", "VScrollBar", Box(Orange, 0, 4));
        theme.SetStylebox("grabber_pressed", "VScrollBar", Box(Orange, 0, 4));
        theme.SetStylebox("panel", "TooltipPanel", Box(Ink, 2, 14));
        theme.SetColor("font_color", "TooltipLabel", PaperLight);
        return theme;
    }

    public static Texture2D Icon(string name)
    {
        if (!Icons.TryGetValue(name, out Texture2D texture))
        {
            string path = $"res://art/ui/icons/{name}.svg";
            texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            Icons[name] = texture;
        }
        return texture;
    }

    public static void Apply(Control root)
    {
        root.Theme = Shared;
        ConfigureChildren(root);
        if (root.GetNodeOrNull<PanelContainer>("Panel") is PanelContainer panel)
        {
            panel.AddThemeStyleboxOverride("panel", Shared.GetStylebox("panel", "PanelContainer"));
            void Fit()
            {
                Vector2 viewport = root.GetViewportRect().Size;
                float preferred = root.Name.ToString() switch {
                    "PauseMenu" => 440, "ChapterSelect" => 1040, "SettingsMenu" => 1040,
                    "TalentMenu" => 1080, "ResultsPanel" => 880, _ => 960 };
                float width = Mathf.Min(preferred, viewport.X - 72);
                float height = Mathf.Min(root.Name == "PauseMenu" ? 620 : 720, viewport.Y - 64);
                panel.OffsetLeft = -width / 2; panel.OffsetRight = width / 2;
                panel.OffsetTop = -height / 2; panel.OffsetBottom = height / 2;
            }
            root.Resized += Fit;
            Fit();
        }
    }

    private static void ConfigureChildren(Node parent)
    {
        foreach (Node node in parent.GetChildren())
        {
            if (node is ScrollContainer scroll) scroll.FollowFocus = true;
            if (node is Label label)
            {
                label.AddThemeColorOverride("font_color", Ink);
                label.AddThemeConstantOverride("outline_size", 0);
                if (label.Name == "Title") label.AddThemeFontOverride("font", Bold);
            }
            if (node is Button button)
            {
                foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) button.RemoveThemeStyleboxOverride(state);
                button.RemoveThemeColorOverride("font_color");
                button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            }
            ConfigureChildren(node);
        }
    }

    public static void Accent(Button button, string icon = null)
    {
        button.AddThemeStyleboxOverride("normal", Box(Orange));
        if (icon != null) button.Icon = Icon(icon);
    }

    public static Label Eyebrow(string text)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeFontOverride("font", Bold);
        label.AddThemeColorOverride("font_color", Muted);
        return label;
    }

    public static void Heading(VBoxContainer box, string text)
    {
        var label = Eyebrow(text);
        box.AddChild(label); box.MoveChild(label, 0);
    }
}
