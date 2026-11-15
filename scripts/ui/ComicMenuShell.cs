using Godot;

/// <summary>Pause cover. Gameplay menu paths and signals stay owned by GameManager.</summary>
public partial class ComicMenuShell : Control
{
    public override void _Ready()
    {
        ComicTheme.Apply(this);
        var box = GetNode<VBoxContainer>("Panel/VBox");
        ComicTheme.Heading(box, "PUERTO SAN MIGUEL  /  行动记录");
        GetNode<Label>("Panel/VBox/Title").Text = "黄昏港口";
        var resume = GetNode<Button>("Panel/VBox/ResumeButton");
        ComicTheme.Accent(resume, "continue");
        GetNode<Button>("Panel/VBox/ChaptersButton").Icon = ComicTheme.Icon("chapters");
        GetNode<Button>("Panel/VBox/CollectiblesButton").Icon = ComicTheme.Icon("evidence");
        GetNode<Button>("Panel/VBox/TalentButton").Icon = ComicTheme.Icon("mobility");
        GetNode<Button>("Panel/VBox/SettingsButton").Icon = ComicTheme.Icon("settings");
        GetNode<Button>("Panel/VBox/DifficultyButton").Icon = ComicTheme.Icon("target");
        GetNode<Button>("Panel/VBox/QuitButton").Icon = ComicTheme.Icon("exit");
        var footer = ComicTheme.Eyebrow("暂停中  ·  计划你的下一步");
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(footer);
    }
}
