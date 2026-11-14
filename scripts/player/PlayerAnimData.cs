using Godot;

/// <summary>
/// 玩家全身动画配置（A 阶段）。只承载数值与动画名契约，不含逻辑。
///
/// 素材契约（player_hero_lod0/1/2.glb）：面朝 +Z、脚底 y=0、UE5 Mannequin 骨架，
/// 剪辑名与本文件一致时直接复用；AnimationTree 由 <see cref="PlayerAnimation"/> 按同名状态补。
/// </summary>
[GlobalClass]
public partial class PlayerAnimData : Resource
{
	[ExportGroup("状态阈值")]
	/// <summary>超过该水平速度进入走路状态（m/s）。</summary>
	[Export] public float WalkSpeedThreshold { get; set; } = 0.3f;
	/// <summary>超过该水平速度改用跑步剪辑（m/s）。低于它用走路剪辑、高于它用跑步剪辑，避免播放速率被拉得过高。</summary>
	[Export] public float RunSpeedThreshold { get; set; } = 2.4f;

	[ExportGroup("滑步修正")]
	/// <summary>walk 剪辑的原生地面速度（m/s），来自角色资产 README。</summary>
	[Export] public float WalkClipSpeed { get; set; } = 0.7f;
	/// <summary>run 剪辑的原生地面速度（m/s）。</summary>
	[Export] public float RunClipSpeed { get; set; } = 1.77f;
	/// <summary>播放速率下限（速度接近 0 时不至于停死）。</summary>
	[Export] public float MinPlaybackScale { get; set; } = 0.5f;
	/// <summary>播放速率上限，防止速度远超剪辑原生速度时动作抽搐。</summary>
	[Export] public float MaxPlaybackScale { get; set; } = 4f;

	[ExportGroup("节奏")]
	/// <summary>状态之间的交叉淡入时间（秒）。</summary>
	[Export] public float StateBlendTime { get; set; } = 0.15f;
	/// <summary>落地姿势保持时间（秒）。</summary>
	[Export] public float LandHoldTime { get; set; } = 0.6f;
	/// <summary>开火姿势保持时间（秒）。</summary>
	[Export] public float FireHoldTime { get; set; } = 0.4f;
	/// <summary>受击姿势保持时间（秒）。</summary>
	[Export] public float HitHoldTime { get; set; } = 0.5f;

	[ExportGroup("动画名契约")]
	[Export] public string IdleClip { get; set; } = "idle";
	[Export] public string WalkClip { get; set; } = "walk";
	[Export] public string RunClip { get; set; } = "run";
	[Export] public string JumpClip { get; set; } = "jump_start";
	[Export] public string LandClip { get; set; } = "jump_land";
	[Export] public string AimClip { get; set; } = "aim";
	[Export] public string FireClip { get; set; } = "fire";
	[Export] public string ReloadClip { get; set; } = "reload";
	[Export] public string HitClip { get; set; } = "hit";
	[Export] public string DeathClip { get; set; } = "death";
	/// <summary>素材没有滑铲剪辑时留空，滑铲沿用落地剪辑的蹲伏姿势。</summary>
	[Export] public string SlideClip { get; set; } = "";

	[ExportGroup("LOD")]
	/// <summary>LOD0 的最远距离（米），超过切 LOD1。</summary>
	[Export] public float Lod0Distance { get; set; } = 10f;
	/// <summary>LOD1 的最远距离（米），超过切 LOD2。</summary>
	[Export] public float Lod1Distance { get; set; } = 25f;

	[ExportGroup("第三人称武器")]
	/// <summary>武器挂点相对 hand_r 骨骼原点的偏移（米，玩家朝向坐标系）。</summary>
	[Export] public Vector3 WeaponGripOffset { get; set; } = new(0.02f, -0.02f, -0.02f);
	/// <summary>武器绕 Y 轴的额外偏转（度）。</summary>
	[Export] public float WeaponYawOffset { get; set; }

	[ExportGroup("占位骨架")]
	/// <summary>素材动画缺剪辑时，用占位骨架程序化生成动作。素材已齐时保持关闭。</summary>
	[Export] public bool GeneratePlaceholderClips { get; set; }
	[Export] public float PlaceholderWalkCycle { get; set; } = 1f;
	[Export] public float PlaceholderRunCycle { get; set; } = 0.66f;
}
