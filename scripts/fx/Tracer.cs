using Godot;

/// <summary>枪口拖尾：从枪口连到命中点的短命细杆（设计文档 §4.4）。</summary>
public partial class Tracer : Node3D
{
	[Export] public float Lifetime { get; set; } = 0.06f;

	private float _remaining;

	/// <summary>把拖尾摆到两点之间，长度为两点距离。</summary>
	public void Fire(Vector3 from, Vector3 to)
	{
		Vector3 delta = to - from;
		float length = delta.Length();
		if (length < 0.01f)
		{
			QueueFree();
			return;
		}

		GlobalPosition = from;
		Vector3 up = Mathf.Abs(delta.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
		LookAt(to, up);
		Scale = new Vector3(1f, 1f, length);

		_remaining = Lifetime;
	}

	public override void _Process(double delta)
	{
		_remaining -= (float)delta;
		if (_remaining <= 0f)
			QueueFree();
	}
}
