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
		ComicTheme.Apply(this);
		Visible = false;
		_title = GetNode<Label>("Panel/VBox/Title");
		_list = GetNode<VBoxContainer>("Panel/VBox/Scroll/List");
		_back = GetNode<Button>("Panel/VBox/BackButton");
		_back.Pressed += Close;
		ComicTheme.Heading(GetNode<VBoxContainer>("Panel/VBox"), "证物档案  /  潮水带不走的痕迹");
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
			_list.AddChild(new Label { Text = "还没有发现港口的秘密。" });
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
			header.AddThemeFontOverride("font", ComicTheme.Bold);
			header.AddThemeColorOverride("font_color", ComicTheme.Ink);
			_list.AddChild(header);
			var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			grid.AddThemeConstantOverride("h_separation", 12);
			grid.AddThemeConstantOverride("v_separation", 12);
			_list.AddChild(grid);

			foreach (CollectibleData item in index.Items)
			{
				if (item == null || item.ChapterId != chapter.Id)
					continue;

				bool has = SaveSystem.HasInstance && SaveSystem.Instance.HasCollectible(item.Id);
				Texture2D illustration = has ? GD.Load<Texture2D>($"res://art/ui/evidence/{item.Id}.svg") : ComicTheme.Icon("lock");
				var row = ComicCards.Create(has ? item.Name : "未发现的证物", has ? item.Text : "留意角落、背街与暗处。", illustration, 160, 78, false);
				row.TooltipText = has ? item.Text : "探索关卡以发现这件证物";
				grid.AddChild(row);
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
