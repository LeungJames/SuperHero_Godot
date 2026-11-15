using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 天赋页（设计文档 §5.3、§5.4）。三条线分开记账；未满足前置 / 章节 / 点数的节点置灰。
/// 页面本身不判断"关卡中途"：由打开方通过 editable 传入是否允许加点与重置。
/// </summary>
public partial class TalentMenu : Control
{
	public event Action Closed;

	private const float ConfirmWindow = 3f;

	private Label _title;
	private Label _hint;
	private VBoxContainer _list;
	private Button _closeButton;

	private bool _editable;
	private TalentLine? _armedLine;
	private float _armTimer;

	public override void _Ready()
	{
		Visible = false;
		_title = GetNode<Label>("Panel/VBox/Title");
		_hint = GetNode<Label>("Panel/VBox/Hint");
		_list = GetNode<VBoxContainer>("Panel/VBox/Scroll/List");
		_closeButton = GetNode<Button>("Panel/VBox/CloseButton");
		_closeButton.Pressed += Close;
		SetHint(string.Empty);
	}

	public void Open(bool editable)
	{
		_editable = editable;
		_armedLine = null;
		_armTimer = 0f;
		Visible = true;
		_title.Text = editable ? "天赋 · 加点" : "天赋 · 查看（关卡中不可修改）";
		Rebuild();
		_closeButton.GrabFocus();
	}

	public void Close()
	{
		if (!Visible)
			return;
		Visible = false;
		Closed?.Invoke();
	}

	public override void _Process(double delta)
	{
		if (_armedLine == null)
			return;

		_armTimer -= (float)delta;
		if (_armTimer > 0f)
			return;

		_armedLine = null;
		if (Visible)
		{
			SetHint("重置确认已取消。");
			Rebuild();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible)
			return;

		if (@event.IsActionPressed("ui_cancel"))
		{
			Close();
			GetViewport().SetInputAsHandled();
			return;
		}

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

		if (!SaveSystem.HasInstance)
		{
			_list.AddChild(new Label { Text = "存档尚未就绪。" });
			return;
		}

		foreach (TalentLine line in new[] { TalentLine.Mobility, TalentLine.Weapon, TalentLine.Assassin })
			BuildSection(line);

		SetHint(string.Empty);
	}

	private void BuildSection(TalentLine line)
	{
		int earned = TalentSystem.EarnedPoints(line);
		int spent = TalentSystem.SpentPoints(line);
		int available = TalentSystem.AvailablePoints(line);
		int penalty = TalentSystem.PenaltyPoints(line);

		string penaltyText = penalty > 0 ? $"　重置罚分 -{penalty}" : string.Empty;
		var header = new Label
		{
			Text = $"{LineName(line)}　　可用 {available} / 已获得 {earned} / 已投入 {spent}{penaltyText}",
			CustomMinimumSize = new Vector2(0, 34),
		};
		header.AddThemeFontSizeOverride("font_size", 20);
		_list.AddChild(header);

		foreach (TalentNodeData node in TalentSystem.NodesOf(line))
			_list.AddChild(BuildNodeButton(node));

		Button reset = BuildResetButton(line);
		_list.AddChild(reset);
		_list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
	}

	private Button BuildNodeButton(TalentNodeData node)
	{
		bool owned = TalentSystem.IsPurchased(node.Id);
		bool prereq = TalentSystem.IsPrerequisiteMet(node);
		bool chapter = TalentSystem.IsChapterAllowed(node);
		bool canBuy = _editable && TalentSystem.CanPurchase(node);

		string state;
		if (owned)
			state = node.Cost <= 0 ? "已拥有" : "已点";
		else if (!chapter)
			state = "需通关第二章";
		else if (!prereq)
			state = $"需先点 {TalentSystem.Get(node.PrerequisiteId)?.DisplayName ?? node.PrerequisiteId}";
		else
			state = $"{node.Cost} 点";

		string prefix = owned ? "✓ " : !chapter || !prereq ? "[锁] " : "○ ";
		var button = new Button
		{
			Text = $"{prefix}{node.DisplayName}　（{state}）",
			TooltipText = node.Description,
			Disabled = !canBuy,
			CustomMinimumSize = new Vector2(0, 42),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};

		if (!owned && (!prereq || !chapter))
			button.AddThemeColorOverride("font_disabled_color", new Color(0.55f, 0.52f, 0.48f));

		if (canBuy)
			button.Pressed += () => Purchase(node);
		return button;
	}

	private Button BuildResetButton(TalentLine line)
	{
		int resets = SaveSystem.Instance.GetTalentResetCount(TalentSystem.LineKey(line));
		bool free = resets < TalentSystem.ResetConfig.FreeResets;
		string cost = free ? "免费" : $"{TalentSystem.ResetConfig.PointCost} 点";
		bool armed = _armedLine == line;

		var button = new Button
		{
			Text = armed
				? $"再次点击确认重置（{cost}）"
				: $"重置{LineName(line)}（{cost}）",
			Disabled = !_editable || !TalentSystem.CanReset(line, inChapter: false),
			CustomMinimumSize = new Vector2(0, 38),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		button.Pressed += () => RequestReset(line);
		return button;
	}

	private void Purchase(TalentNodeData node)
	{
		if (!TalentSystem.Purchase(node.Id))
			return;

		_armedLine = null;
		SetHint($"已加点：{node.DisplayName}");
		Rebuild();
	}

	private void RequestReset(TalentLine line)
	{
		if (!_editable)
			return;

		if (_armedLine != line)
		{
			_armedLine = line;
			_armTimer = ConfirmWindow;
			SetHint($"再次点击确认重置：已点节点会全部退还，代价按配置扣除。");
			Rebuild();
			return;
		}

		if (!TalentSystem.ResetLine(line, inChapter: false))
		{
			SetHint("当前无法重置。");
			return;
		}

		_armedLine = null;
		SetHint($"已重置{LineName(line)}。");
		Rebuild();
	}

	private void SetHint(string text)
	{
		_hint.Text = text;
		_hint.Visible = !string.IsNullOrEmpty(text);
	}

	private static string LineName(TalentLine line) => line switch
	{
		TalentLine.Mobility => "战术机动",
		TalentLine.Weapon => "武器大师",
		_ => "忍杀",
	};
}
