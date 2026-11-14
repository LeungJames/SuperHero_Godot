using Godot;

/// <summary>
/// 弹药包：靠近自动拾取 6 发；备弹满时留在原地（设计文档 §4.6）。
/// </summary>
public partial class AmmoPickup : Area3D
{
	[Export] public int Rounds { get; set; } = 6;

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is not PlayerController)
			return;

		foreach (Node node in GetTree().GetNodesInGroup("pistol"))
		{
			if (node is PistolController pistol && pistol.TryAddAmmo(Rounds))
			{
				QueueFree();
				return;
			}
		}
	}
}
