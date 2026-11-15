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
		ComicTheme.Apply(this);
		Visible = false;
		_title = GetNode<Label>("Panel/VBox/Title");
		_slots = GetNode<Label>("Panel/VBox/Slots");
		_hint = GetNode<Label>("Panel/VBox/Hint");
		_list = GetNode<VBoxContainer>("Panel/VBox/Scroll/List");
		_start = GetNode<Button>("Panel/VBox/StartButton");
		_back = GetNode<Button>("Panel/VBox/BackButton");

		_start.Pressed += OnStartPressed;
		_back.Pressed += Close;
		ComicTheme.Heading(GetNode<VBoxContainer>("Panel/VBox"), "装备检查  /  出发之前");
		ComicTheme.Accent(_start, "continue");
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
			WeaponData weapon = LoadoutSystem.FindWeapon(option.Id);
			string description = option.Id == LoadoutSystem.KnifeId ? "近战  /  暗杀与格挡"
				: weapon != null ? $"弹匣 {weapon.MagazineSize}  ·  伤害 {weapon.BaseDamage:0}  ·  换弹 {weapon.ReloadTime:0.0}s" : "备用装备";
			string path = $"res://art/ui/weapons/{option.Id}.svg";
			Texture2D picture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : ComicTheme.Icon("weapon");
			var button = ComicCards.Create($"{(selected ? "已携带  /  " : "")}{option.Name}", option.Unlocked ? description : "尚未解锁", picture, 104, 170, false);
			button.Disabled = !option.Unlocked;
			if (!option.Unlocked) button.Modulate = new Color(0.7f, 0.7f, 0.7f);
			if (selected) button.AddThemeStyleboxOverride("normal", ComicTheme.Box(new Color("f4cc8a"), 3));

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
		_slots.Text = $"01   {first}                 02   {second}";

		_start.Disabled = _selected.Count != 2 || !LoadoutSystem.IsValid(_chapter.Id, _selected);
		_start.Text = _start.Disabled ? "选择两件装备后出发" : "携带装备 · 出发";
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
