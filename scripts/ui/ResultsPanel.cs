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
	/// <summary>M6：结算面板的「天赋」入口（胜负都可用，视为重试前调整）。</summary>
	public event Action TalentPressed;

	private Label _title;
	private Label _subtitle;
	private Label _stats;
	private VBoxContainer _ratingBox;
	private Label _ratingMobility;
	private Label _ratingWeapon;
	private Label _ratingAssassin;
	private Label _ratingTotal;
	private Button _next;
	private Button _restart;
	private Button _quit;
	private Button _talent;

	private static readonly Color GradeS = new(0.85f, 0.68f, 0.25f);
	private static readonly Color GradeA = new(0.31f, 0.52f, 0.33f);
	private static readonly Color GradeB = new(0.42f, 0.36f, 0.3f);
	private static readonly Color GradeC = new(0.55f, 0.52f, 0.48f);

	public override void _Ready()
	{
		_title = GetNode<Label>("Panel/VBox/Title");
		_subtitle = GetNode<Label>("Panel/VBox/Subtitle");
		_stats = GetNode<Label>("Panel/VBox/Stats");
		_ratingBox = GetNode<VBoxContainer>("Panel/VBox/RatingBox");
		_ratingMobility = GetNode<Label>("Panel/VBox/RatingBox/Mobility");
		_ratingWeapon = GetNode<Label>("Panel/VBox/RatingBox/Weapon");
		_ratingAssassin = GetNode<Label>("Panel/VBox/RatingBox/Assassin");
		_ratingTotal = GetNode<Label>("Panel/VBox/RatingBox/Total");
		_next = GetNode<Button>("Panel/VBox/NextButton");
		_restart = GetNode<Button>("Panel/VBox/RestartButton");
		_quit = GetNode<Button>("Panel/VBox/QuitButton");
		_talent = GetNode<Button>("Panel/VBox/TalentButton");

		_next.Pressed += () => NextPressed?.Invoke();
		_restart.Pressed += () => RestartPressed?.Invoke();
		_quit.Pressed += () => QuitPressed?.Invoke();
		_talent.Pressed += () => TalentPressed?.Invoke();

		Visible = false;
	}

	/// <summary>
	/// 展示结算。有下一章时焦点落在“进入下一章”，Enter 直接进；否则落在“重新开始”。
	/// 第三章（终局）用 finale=true 显示终局标题，不给下一章入口。
	/// rating 非空时展示三维徽章与点数（§6.6）；失败结算传 null。
	/// </summary>
	public void Present(bool victory, string subtitle, string stats, string nextLabel = null,
		bool finale = false, RatingOutcome rating = null)
	{
		_title.Text = finale ? "终局 · 三章全通" : victory ? "区域清空 · 撤离成功" : "阵亡";
		_title.AddThemeColorOverride("font_color", victory || finale
			? new Color(0.85f, 0.68f, 0.25f)
			: new Color(0.69f, 0.16f, 0.16f));
		_subtitle.Text = subtitle;
		_stats.Text = stats;

		ShowRating(rating);

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

	private void ShowRating(RatingOutcome rating)
	{
		if (rating == null || rating.Results.Count < 3)
		{
			_ratingBox.Visible = false;
			return;
		}

		_ratingBox.Visible = true;
		SetRatingLabel(_ratingMobility, rating.Mobility);
		SetRatingLabel(_ratingWeapon, rating.Weapon);
		SetRatingLabel(_ratingAssassin, rating.Assassin);
		_ratingTotal.Text = $"本次新增 {rating.AwardedTotal} 点　历史最佳 {rating.BestTotal} 点";
	}

	private static void SetRatingLabel(Label label, DimensionRating rating)
	{
		label.Text = $"{rating.Label}  {rating.Grade}  +{rating.Awarded}\n{rating.Detail}";
		label.AddThemeColorOverride("font_color", rating.Grade switch
		{
			"S" => GradeS,
			"A" => GradeA,
			"B" => GradeB,
			_ => GradeC,
		});
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

	/// <summary>从天赋页返回时把焦点还给主行动按钮。</summary>
	public void FocusPrimary()
	{
		if (!Visible)
			return;
		if (_next.Visible)
			_next.GrabFocus();
		else
			_restart.GrabFocus();
	}
}
