using Godot;

/// <summary>
/// 关卡章节装配：在关卡子树进入场景树时把章节号写回共享的 <see cref="ArsenalData"/>，
/// 让玩家武器在 _Ready 里按章节解锁（赤蛇工厂 = 第 2 章，解锁冲锋枪与突击步枪）。
/// 只写配置，不含任何玩法逻辑（设计文档 §5.2、§9.3）。
/// </summary>
public partial class ChapterSetup : Node
{
	[Export] public ArsenalData Arsenal { get; set; }

	/// <summary>本章节号：0 / 1 = 港口，2 = 赤蛇工厂，3 = 铁锚据点。</summary>
	[Export] public int Chapter { get; set; }

	public override void _EnterTree()
	{
		if (Arsenal != null)
			Arsenal.CurrentChapter = Chapter;
	}
}
