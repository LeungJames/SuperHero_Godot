using Godot;

/// <summary>命中火花 / 灰尘：贴在命中面上的一小块短命网格（设计文档 §4.4）。</summary>
public partial class ImpactEffect : Node3D
{
	[Export] public float Lifetime { get; set; } = 0.12f;

	private float _remaining;

	public void Play(Vector3 point, Vector3 normal)
	{
		GlobalPosition = point;

		if (normal.LengthSquared() > 0.0001f)
		{
			Vector3 up = Mathf.Abs(normal.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
			LookAt(point + normal, up);
		}

		_remaining = Lifetime;
	}

	public override void _Process(double delta)
	{
		_remaining -= (float)delta;
		if (_remaining <= 0f)
			QueueFree();
	}
}
