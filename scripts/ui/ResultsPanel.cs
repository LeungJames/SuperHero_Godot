using Godot;
using System;

/// <summary>
/// 结算面板：胜负文案 + 统计 + 下一章 / 重新开始 / 退出。
/// 只发事件，不直接换场景（设计文档 §7.4、§9.3）。
/// </summary>
public partial class ResultsPanel : Control
{
	public event Action NextPressed;
	public event Action RestartPressed;
	public event Action QuitPressed;

	private Label _title;
	private Label _subtitle;
	private Label _stats;
	private Button _next;
	private Button _restart;
	private Button _quit;

	public override void _Ready()
	{
		_title = GetNode<Label>("Panel/VBox/Title");
		_subtitle = GetNode<Label>("Panel/VBox/Subtitle");
		_stats = GetNode<Label>("Panel/VBox/Stats");
		_next = GetNode<Button>("Panel/VBox/NextButton");
		_restart = GetNode<Button>("Panel/VBox/RestartButton");
		_quit = GetNode<Button>("Panel/VBox/QuitButton");

		_next.Pressed += () => NextPressed?.Invoke();
		_restart.Pressed += () => RestartPressed?.Invoke();
		_quit.Pressed += () => QuitPressed?.Invoke();

		Visible = false;
	}

	/// <summary>
	/// 展示结算。有下一章时焦点落在“进入下一章”，Enter 直接进；否则落在“重新开始”。
	/// 第三章（终局）用 finale=true 显示终局标题，不给下一章入口。
	/// </summary>
	public void Present(bool victory, string subtitle, string stats, string nextLabel = null, bool finale = false)
	{
		_title.Text = finale ? "终局 · 三章全通" : victory ? "区域清空 · 撤离成功" : "阵亡";
		_title.AddThemeColorOverride("font_color", victory || finale
			? new Color(0.85f, 0.68f, 0.25f)
			: new Color(0.69f, 0.16f, 0.16f));
		_subtitle.Text = subtitle;
		_stats.Text = stats;

		bool hasNext = victory && !finale && !string.IsNullOrEmpty(nextLabel);
		_next.Visible = hasNext;
		if (hasNext)
		{
			_next.Text = nextLabel;
			_next.GrabFocus();
		}
		else
		{
			_restart.GrabFocus();
		}

		Visible = true;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible || !@event.IsActionPressed("ui_accept"))
			return;

		// 先标记事件已处理：换场景会立刻销毁本面板
		GetViewport().SetInputAsHandled();
		if (_next.Visible)
			NextPressed?.Invoke();
		else
			RestartPressed?.Invoke();
	}
}
