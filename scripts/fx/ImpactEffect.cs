using Godot;

/// <summary>命中火花 / 灰尘：贴在命中面上的一小块短命网格（设计文档 §4.4）。</summary>
public partial class ImpactEffect : Node3D
{
	[Export] public float Lifetime { get; set; } = 0.19f;

	private float _remaining;
	private MeshInstance3D _burst;
	public override void _Ready()
	{
		GetNodeOrNull<Node3D>("Mesh")?.Hide();
		_burst = new MeshInstance3D { Mesh = ComicBurstMesh.Star, MaterialOverride = ComicBurstMesh.Billboard,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(_burst);
	}

	public void Play(Vector3 point, Vector3 normal)
	{
		GlobalPosition = point + normal * 0.025f;

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
		if (_burst != null)
		{
			float t = 1f - Mathf.Clamp(_remaining / Lifetime, 0f, 1f);
			_burst.Scale = Vector3.One * Mathf.Lerp(0.10f, 0.24f, Mathf.Sin(t * Mathf.Pi));
			_burst.Transparency = Mathf.Pow(t, 2);
		}
		if (_remaining <= 0f)
			QueueFree();
	}
}
