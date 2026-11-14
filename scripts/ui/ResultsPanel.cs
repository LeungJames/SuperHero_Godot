using Godot;
using System;

/// <summary>
/// 结算面板：胜负文案 + 统计 + 重新开始 / 退出。
/// 只发事件，不直接重开场景（设计文档 §7.4、§9.3）。
/// </summary>
public partial class ResultsPanel : Control
{
	public event Action RestartPressed;
	public event Action QuitPressed;

	private Label _title;
	private Label _subtitle;
	private Label _stats;
	private Button _restart;
	private Button _quit;

	public override void _Ready()
	{
		_title = GetNode<Label>("Panel/VBox/Title");
		_subtitle = GetNode<Label>("Panel/VBox/Subtitle");
		_stats = GetNode<Label>("Panel/VBox/Stats");
		_restart = GetNode<Button>("Panel/VBox/RestartButton");
		_quit = GetNode<Button>("Panel/VBox/QuitButton");

		_restart.Pressed += () => RestartPressed?.Invoke();
		_quit.Pressed += () => QuitPressed?.Invoke();

		Visible = false;
	}

	/// <summary>展示结算。默认焦点落在“重新开始”，Enter / 左键直接重开（§2.5）。</summary>
	public void Present(bool victory, string subtitle, string stats)
	{
		_title.Text = victory ? "区域清空 · 撤离成功" : "阵亡";
		_title.AddThemeColorOverride("font_color", victory
			? new Color(0.85f, 0.68f, 0.25f)
			: new Color(0.69f, 0.16f, 0.16f));
		_subtitle.Text = subtitle;
		_stats.Text = stats;

		Visible = true;
		_restart.GrabFocus();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible || !@event.IsActionPressed("ui_accept"))
			return;

		// 先标记事件已处理：重开会立刻销毁本面板
		GetViewport().SetInputAsHandled();
		RestartPressed?.Invoke();
	}
}
