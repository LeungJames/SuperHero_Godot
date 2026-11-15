using Godot;
using System;

/// <summary>
/// 章节选择（2.0 设计 §10.4）。只列出已解锁章节：
/// 第一章初始解锁，之后每通关一章解锁下一章；已通关章节显示历史最佳时间。
/// </summary>
public partial class ChapterSelect : Control
{
	public event Action Closed;

	/// <summary>选中章节：已通关的交给武器库，未通关的直接进关（首次通关不改配置）。</summary>
	public event Action<SaveSystem.ChapterInfo> ChapterChosen;

	private VBoxContainer _list;
	private Button _backButton;

	public override void _Ready()
	{
		ComicTheme.Apply(this);
		Visible = false;
		_list = GetNode<VBoxContainer>("Panel/VBox/List");
		_backButton = GetNode<Button>("Panel/VBox/BackButton");
		_backButton.Pressed += Close;
		ComicTheme.Heading(GetNode<VBoxContainer>("Panel/VBox"), "行动档案  /  选择章节");
	}

	public void Open()
	{
		if (Visible)
			return;

		Visible = true;
		Rebuild();
		_backButton.GrabFocus();
	}

	public void Close()
	{
		if (!Visible)
			return;

		Visible = false;
		Closed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible)
			return;

		// 键鼠 Esc 由 GameManager 的 pause 动作负责关闭本页；这里只补手柄 B
		if (@event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B })
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Rebuild()
	{
		foreach (Node child in _list.GetChildren())
		{
			_list.RemoveChild(child);
			child.QueueFree();
		}

		bool any = false;
		foreach (SaveSystem.ChapterInfo chapter in SaveSystem.Chapters)
		{
			if (SaveSystem.HasInstance && !SaveSystem.Instance.IsChapterUnlocked(chapter.Id))
				continue;

			any = true;
			string place = chapter.Id switch {
				"factory" => "赤蛇会的蒸汽与管道。穿过红光，切断他们的退路。",
				"stronghold" or "anchor" => "铁锚帮的干船坞。链条尽头，霍克正在等你。",
				_ => "从吊塔一路下压。金色海面之下，盐帮控制着每一条走道。" };
			float cardHeight = Mathf.Clamp((GetViewportRect().Size.Y - 300f) / 3f, 88f, 140f);
			var button = ComicCards.Create(BuildLabel(chapter), place, ComicArt.Panel(ComicArt.ChapterPage(chapter.Id)), cardHeight);
			button.Pressed += () => Enter(chapter);
			_list.AddChild(button);
		}

		if (!any)
			_list.AddChild(new Label { Text = "尚无可进入的章节。" });
	}

	private static string BuildLabel(SaveSystem.ChapterInfo chapter)
	{
		if (!SaveSystem.HasInstance || !SaveSystem.Instance.IsChapterCleared(chapter.Id))
			return chapter.Label;

		float best = SaveSystem.Instance.GetBestTime(chapter.Id);
		return best > 0f ? $"{chapter.Label}    最佳 {Mathf.RoundToInt(best)}s" : chapter.Label;
	}

	private void Enter(SaveSystem.ChapterInfo chapter)
	{
		if (string.IsNullOrEmpty(chapter.Scene))
			return;

		// 不触发 Closed：由 GameManager 决定进武器库还是直接换场景
		Visible = false;
		ChapterChosen?.Invoke(chapter);
	}
}
