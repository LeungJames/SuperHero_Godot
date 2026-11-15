using Godot;
using System;

/// <summary>
/// 暂停菜单里的收集品页（2.0 设计 §4.2）。按章节列出 15 件，
/// 未收集只显示 ???；纯展示，不给任何数值收益。
/// </summary>
public partial class CollectiblesMenu : Control
{
	public event Action Closed;

	[Export] public CollectibleIndex Index { get; set; }

	private const string IndexPath = "res://data/narrative/collectibles/index.tres";

	private Label _title;
	private VBoxContainer _list;
	private Button _back;

	public override void _Ready()
	{
		Visible = false;
		_title = GetNode<Label>("Panel/VBox/Title");
		_list = GetNode<VBoxContainer>("Panel/VBox/Scroll/List");
		_back = GetNode<Button>("Panel/VBox/BackButton");
		_back.Pressed += Close;
	}

	public void Open()
	{
		if (Visible)
			return;

		Visible = true;
		Rebuild();
		_back.GrabFocus();
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

		CollectibleIndex index = Index ??
			(ResourceLoader.Exists(IndexPath) ? GD.Load<CollectibleIndex>(IndexPath) : null);
		if (index == null)
		{
			_title.Text = "收集品 0/0";
			_list.AddChild(new Label { Text = "收集品数据缺失。" });
			return;
		}

		int total = index.Items.Count;
		int collected = SaveSystem.HasInstance ? SaveSystem.Instance.CollectedCount : 0;
		_title.Text = $"收集品 {collected}/{total}";

		foreach (SaveSystem.ChapterInfo chapter in SaveSystem.Chapters)
		{
			var header = new Label
			{
				Text = BuildChapterHeader(chapter, index),
				CustomMinimumSize = new Vector2(0, 34),
			};
			header.AddThemeFontSizeOverride("font_size", 21);
			header.AddThemeColorOverride("font_color", new Color(0.95f, 0.78f, 0.4f));
			_list.AddChild(header);

			foreach (CollectibleData item in index.Items)
			{
				if (item == null || item.ChapterId != chapter.Id)
					continue;

				bool has = SaveSystem.HasInstance && SaveSystem.Instance.HasCollectible(item.Id);
				var row = new Label
				{
					Text = has ? $"● {item.Name}\n　{item.Text}" : "● ？？？\n　未发现",
					AutowrapMode = TextServer.AutowrapMode.WordSmart,
					CustomMinimumSize = new Vector2(0, 48),
				};
				row.AddThemeFontSizeOverride("font_size", 17);
				row.AddThemeColorOverride("font_color",
					has ? new Color(0.93f, 0.9f, 0.84f) : new Color(0.58f, 0.55f, 0.5f));
				_list.AddChild(row);
			}
		}
	}

	private static string BuildChapterHeader(SaveSystem.ChapterInfo chapter, CollectibleIndex index)
	{
		int total = 0;
		int collected = 0;
		foreach (CollectibleData item in index.Items)
		{
			if (item == null || item.ChapterId != chapter.Id)
				continue;

			total++;
			if (SaveSystem.HasInstance && SaveSystem.Instance.HasCollectible(item.Id))
				collected++;
		}

		return $"{chapter.Label}　{collected}/{total}";
	}
}
