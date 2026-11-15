using Godot;
using System;

/// <summary>
/// 章节选择（2.0 设计 §10.4）。只列出已解锁章节：
/// 第一章初始解锁，之后每通关一章解锁下一章；已通关章节显示历史最佳时间。
/// </summary>
public partial class ChapterSelect : Control
{
	public event Action Closed;

	private VBoxContainer _list;
	private Button _backButton;

	public override void _Ready()
	{
		Visible = false;
		_list = GetNode<VBoxContainer>("Panel/VBox/List");
		_backButton = GetNode<Button>("Panel/VBox/BackButton");
		_backButton.Pressed += Close;
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
			var button = new Button
			{
				Text = BuildLabel(chapter),
				CustomMinimumSize = new Vector2(0, 52),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			button.Pressed += () => Enter(chapter.Scene);
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

	private void Enter(string scene)
	{
		if (string.IsNullOrEmpty(scene))
			return;

		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(scene);
	}
}
