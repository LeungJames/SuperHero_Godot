using Godot;

/// <summary>
/// 武器库：按章节解锁的武器列表与切枪节奏（设计文档 §5.2 "武器按章节解锁"）。
/// 只承载配置，不含逻辑；章节由剧情推进写入 CurrentChapter。
/// </summary>
[GlobalClass]
public partial class ArsenalData : Resource
{
	/// <summary>当前章节：0 = 序章 / 第一章（港口，仅手枪 + 近战），2 = 赤蛇工厂，3 = 铁锚据点。</summary>
	[Export] public int CurrentChapter { get; set; }

	/// <summary>武器列表，顺序即快捷栏顺序（1 起）。UnlockChapter 大于当前章节的不出现在切枪循环里。</summary>
	[Export] public Godot.Collections.Array<WeaponData> Weapons { get; set; } = new();

	[ExportGroup("切枪")]
	[Export] public float SwapLowerTime { get; set; } = 0.16f;
	[Export] public float SwapRaiseTime { get; set; } = 0.20f;
	[Export] public float SwapLowerDrop { get; set; } = 0.22f;
}
