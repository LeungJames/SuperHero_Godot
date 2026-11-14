using Godot;

/// <summary>
/// 处决演出配置（C 阶段）。只承载数值与节奏，不含逻辑。
///
/// 触发条件：玩家站在任意存活敌人的背后（不区分敌人类型与警觉状态），
/// 处于地面、水平距离与高低差都在允许范围内，且视线没有被关卡几何挡住。
/// </summary>
[GlobalClass]
public partial class ExecutionData : Resource
{
	[ExportGroup("触发判定")]
	/// <summary>玩家到敌人的最大水平距离（米）。</summary>
	[Export] public float Range { get; set; } = 2.4f;
	/// <summary>玩家必须在敌人背后：敌人朝向与「敌→玩家」方向的点积小于该值（越负越正后方）。</summary>
	[Export] public float RearDot { get; set; } = -0.2f;
	/// <summary>玩家视线朝向敌人的最小点积，避免背对目标时也能触发（0 = 侧面即可，1 = 必须正对）。</summary>
	[Export] public float FacingDot { get; set; } = 0.35f;
	/// <summary>允许的高低差（米）：站在台阶 / 集装箱边缘也能处决。</summary>
	[Export] public float MaxVerticalGap { get; set; } = 1.2f;
	/// <summary>判定点相对敌人脚底的视线高度（米）。</summary>
	[Export] public float AimHeight { get; set; } = 1.15f;
	/// <summary>一次处决后的冷却（秒）。</summary>
	[Export] public float Cooldown { get; set; } = 1.4f;
	/// <summary>可用时 HUD 显示的提示文案。</summary>
	[Export] public string PromptText { get; set; } = "【F】处决";

	[ExportGroup("演出节奏")]
	/// <summary>镜头切入时间（秒）。</summary>
	[Export] public float BlendInTime { get; set; } = 0.22f;
	/// <summary>从开始到致命一击的时间（秒），敌人在这段时间里被制服。</summary>
	[Export] public float StrikeTime { get; set; } = 0.45f;
	/// <summary>击杀后机位停留时间（秒），让倒地动作完整可见。</summary>
	[Export] public float RecoverTime { get; set; } = 0.65f;
	/// <summary>镜头切回玩家视角的时间（秒）。</summary>
	[Export] public float BlendOutTime { get; set; } = 0.25f;

	[ExportGroup("演出机位")]
	/// <summary>机位到「玩家—敌人」连线的横向距离（米），侧向构图同时看到处决者与目标。</summary>
	[Export] public float CameraSideOffset { get; set; } = 1.9f;
	/// <summary>机位高出注视点的高度（米）。</summary>
	[Export] public float CameraRise { get; set; } = 0.45f;
	/// <summary>注视点高度（米，相对敌人脚底）。</summary>
	[Export] public float CameraLookHeight { get; set; } = 1.1f;
	/// <summary>注视点沿连线朝玩家方向挪的距离（米），让两个人都在画面里。</summary>
	[Export] public float CameraLookAhead { get; set; } = 0.3f;
	/// <summary>机位被墙挡住时保留的安全距离（米）。</summary>
	[Export] public float CameraCollisionRadius { get; set; } = 0.3f;
	/// <summary>镜头回到距玩家这么近时收起全身模型，避免镜头穿进角色（米）。</summary>
	[Export] public float BodyHideDistance { get; set; } = 0.7f;
}
