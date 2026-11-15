using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 章节开始前的武器库（设计文档 §7.1、§7.4）。两格选择：
/// 刀固定占第二格；未解锁武器置灰并标 [锁]；只有两格都合法时才能开始。
/// </summary>
public partial class LoadoutMenu : Control
{
	public event Action<SaveSystem.ChapterInfo, string[]> StartRequested;
	public event Action Closed;

	private Label _title;
	private Label _slots;
	private Label _hint;
	private VBoxContainer _list;
	private Button _start;
	private Button _back;

	private SaveSystem.ChapterInfo _chapter;
	private readonly List<string> _selected = new();

	public override void _Ready()
	{
		Visible = false;
		_title = GetNode<Label>("Panel/VBox/Title");
		_slots = GetNode<Label>("Panel/VBox/Slots");
		_hint = GetNode<Label>("Panel/VBox/Hint");
		_list = GetNode<VBoxContainer>("Panel/VBox/Scroll/List");
		_start = GetNode<Button>("Panel/VBox/StartButton");
		_back = GetNode<Button>("Panel/VBox/BackButton");

		_start.Pressed += OnStartPressed;
		_back.Pressed += Close;
	}

	/// <summary>从章节选择进入；沿用本章待选组合，没有则用章节默认。</summary>
	public void Open(SaveSystem.ChapterInfo chapter)
	{
		LoadoutSystem.EnsureUnlocksFromClearedChapters();

		_chapter = chapter;
		Visible = true;
		_selected.Clear();

		string[] ids = LoadoutSystem.HasPendingFor(chapter.Id)
			? LoadoutSystem.GetPendingFor(chapter.Id)
			: LoadoutSystem.DefaultSelection(chapter.Id);
		_selected.AddRange(ids);

		_title.Text = $"武器库 · {chapter.Label}";
		SetHint(string.Empty);
		Rebuild(null);
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

		// 键鼠 Esc 由 GameManager 的 pause 动作关闭本页；这里只补手柄 B
		if (@event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B })
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Rebuild(string focusId)
	{
		foreach (Node child in _list.GetChildren())
		{
			_list.RemoveChild(child);
			child.QueueFree();
		}

		Button focusTarget = null;

		foreach (LoadoutSystem.Option option in LoadoutSystem.GetOptions(_chapter.Id))
		{
			bool selected = _selected.Contains(option.Id);
			var button = new Button
			{
				Text = option.Unlocked
					? $"{(selected ? "✓" : "○")} {option.Name}"
					: $"{option.Name}    [锁]",
				Disabled = !option.Unlocked,
				CustomMinimumSize = new Vector2(0, 46),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};

			if (!option.Unlocked)
				button.AddThemeColorOverride("font_disabled_color", new Color(0.55f, 0.52f, 0.48f));
			else
				button.Pressed += () => Toggle(option.Id);

			_list.AddChild(button);

			if (option.Id == focusId && option.Unlocked)
				focusTarget = button;
			else if (focusTarget == null && option.Unlocked)
				focusTarget = button;
		}

		UpdateSummary();

		if (focusId != null && focusTarget != null)
			focusTarget.GrabFocus();
		else if (!_start.Disabled)
			_start.GrabFocus();
		else
			(focusTarget ?? _back).GrabFocus();
	}

	private void UpdateSummary()
	{
		List<string> ordered = LoadoutSystem.OrderForSlots(_selected);
		string first = ordered.Count > 0 ? LoadoutSystem.DisplayName(ordered[0]) : "—";
		string second = ordered.Count > 1 ? LoadoutSystem.DisplayName(ordered[1]) : "—";
		_slots.Text = $"第一格：{first}      第二格：{second}";

		_start.Disabled = _selected.Count != 2 || !LoadoutSystem.IsValid(_chapter.Id, _selected);
		_start.Text = _start.Disabled ? "开始（需选满两格）" : "开始（Enter）";
	}

	private void Toggle(string id)
	{
		if (_selected.Contains(id))
		{
			_selected.Remove(id);
			SetHint(string.Empty);
		}
		else if (_selected.Count >= 2)
		{
			SetHint("两格已满，先取消一件再选。");
		}
		else
		{
			_selected.Add(id);
			SetHint(string.Empty);
		}

		Rebuild(id);
	}

	private void OnStartPressed()
	{
		if (_selected.Count != 2 || !LoadoutSystem.IsValid(_chapter.Id, _selected))
		{
			SetHint("请选择两件已解锁武器。");
			return;
		}

		StartRequested?.Invoke(_chapter, LoadoutSystem.OrderForSlots(_selected).ToArray());
	}

	private void SetHint(string text)
	{
		_hint.Text = text;
		_hint.Visible = !string.IsNullOrEmpty(text);
	}
}
