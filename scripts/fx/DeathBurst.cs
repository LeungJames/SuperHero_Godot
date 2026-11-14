using Godot;

/// <summary>敌人倒地时的一次性血雾粒子，播完自毁（设计文档 §10 M7）。</summary>
public partial class DeathBurst : Node3D
{
	[Export] public float Lifetime { get; set; } = 0.7f;

	private float _time;

	public override void _Process(double delta)
	{
		_time += (float)delta;
		if (_time >= Lifetime)
			QueueFree();
	}
}
