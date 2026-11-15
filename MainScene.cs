using Godot;

/// <summary>
/// 关卡装配根：只负责把场景树搭起来。状态机、敌人登记、胜利演出、
/// HUD 与结算分别由 Managers / UI 下的节点自己抓引用（设计文档 §9.3）。
/// </summary>
public partial class MainScene : Node3D
{
	public override void _Ready()
	{
		// Full-body views share the same paint response as the first-person rig.
		var body = GetNodeOrNull<Node3D>("Actors/Player/BodyModel");
		if (body != null)
		{
			var ink = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/outline.gdshader") };
			ink.SetShaderParameter("width", 0.018f);
			body.AddChild(new ModelOutline { Overlay = ink });
		}
	}
}
