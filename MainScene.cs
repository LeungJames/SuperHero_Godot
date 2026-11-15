using Godot;

/// <summary>
/// 关卡装配根：只负责把场景树搭起来。状态机、敌人登记、胜利演出、
/// HUD 与结算分别由 Managers / UI 下的节点自己抓引用（设计文档 §9.3）。
/// </summary>
public partial class MainScene : Node3D
{
	public override void _Ready()
	{
		var sun = GetNodeOrNull<DirectionalLight3D>("World/Sun");
		if (sun != null)
			sun.Basis = Basis.LookingAt(-new Vector3(-0.60f, 0.085f, -0.795f).Normalized(), Vector3.Up);
		AddChild(new CombatPresentation { Name = "CombatPresentation" });
		// Full-body views share the same paint response as the first-person rig.
		foreach (string path in new[] { "Actors/Player/BodyModel", "Actors/Helicopter" })
		{
			var body = GetNodeOrNull<Node3D>(path);
			if (body == null) continue;
			var ink = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/outline.gdshader") };
			ink.SetShaderParameter("width", 0.018f);
			body.AddChild(new ModelOutline { Overlay = ink });
		}
	}
}
