using Godot;

/// <summary>三条天赋线（设计文档 §5.2）。</summary>
public enum TalentLine
{
	Mobility,
	Weapon,
	Assassin,
}

/// <summary>
/// 单个天赋节点（设计文档 §5.6）。数值与文案全部外置到
/// <c>res://data/talents/&lt;line&gt;/&lt;id&gt;.tres</c>，代码只读不写。
/// </summary>
[GlobalClass]
public partial class TalentNodeData : Resource
{
	[Export] public string Id { get; set; } = string.Empty;
	[Export] public TalentLine Line { get; set; } = TalentLine.Mobility;
	[Export] public string DisplayName { get; set; } = string.Empty;
	[Export] public int Cost { get; set; }
	/// <summary>前置节点 ID；空表示只依赖该线起点。</summary>
	[Export] public string PrerequisiteId { get; set; } = string.Empty;
	/// <summary>效果键；运行时由 <see cref="TalentEffects"/> 解释，节点 ID 本身也参与判断。</summary>
	[Export] public string[] Effects { get; set; } = System.Array.Empty<string>();
	[Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;
	/// <summary>需要先通关的章节 ID；空表示无章节门槛（A7 填 factory）。</summary>
	[Export] public string RequiredClearedChapter { get; set; } = string.Empty;
}
