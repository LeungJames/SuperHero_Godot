using Godot;

/// <summary>
/// 落水判定：玩家进入水体立即死亡。只做判定，不处理 UI 与结算（设计文档 §9.3）。
/// </summary>
public partial class WaterKillVolume : Area3D
{
	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		body.GetNodeOrNull<PlayerHealth>("Health")?.Kill(DeathCause.Drown);
	}
}
