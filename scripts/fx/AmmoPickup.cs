using Godot;

/// <summary>
/// 弹药包：靠近自动拾取 6 发；备弹满时留在原地（设计文档 §4.6）。
/// </summary>
public partial class AmmoPickup : Area3D
{
	[Export] public int Rounds { get; set; } = 6;

	public override void _Ready()
	{
		// 难度只改补给量（§10.5），不改玩家生命 / 回血
		Rounds = DifficultySystem.ApplyAmmo(Rounds);
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is not PlayerController)
			return;

		foreach (Node node in GetTree().GetNodesInGroup("weapon"))
		{
			if (node is WeaponController weapon && weapon.TryAddAmmo(Rounds))
			{
				QueueFree();
				return;
			}
		}
	}
}
