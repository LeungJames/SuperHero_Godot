using Godot;

/// <summary>敌人倒地时的一次性血雾粒子，播完自毁（设计文档 §10 M7）。</summary>
public partial class DeathBurst : Node3D
{
	[Export] public float Lifetime { get; set; } = 0.7f;

	private float _time;
	private readonly System.Collections.Generic.List<(MeshInstance3D Mesh, Vector3 Velocity)> _flecks = new();
	public override void _Ready()
	{
		var old = GetNodeOrNull<CpuParticles3D>("Particles");
		if (old != null) { old.Emitting = false; old.Hide(); }
		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color("a62e3f"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			BillboardKeepScale = true,
		};
		var shape = new PrismMesh { Size = new Vector3(0.08f, 0.16f, 0.008f) };
		for (int i = 0; i < 9; i++)
		{
			float angle = Mathf.Tau * i / 9f;
			var shard = new MeshInstance3D { Mesh = shape, MaterialOverride = material,
				Rotation = new Vector3(0, 0, angle), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild(shard);
			_flecks.Add((shard, new Vector3(Mathf.Cos(angle), 0.8f + Mathf.Sin(angle), Mathf.Sin(angle * 3)) * 1.5f));
		}
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		float dt = (float)delta;
		foreach (var fleck in _flecks)
		{
			fleck.Mesh.Position += (fleck.Velocity + Vector3.Down * _time * 4f) * dt;
			fleck.Mesh.Scale = Vector3.One * Mathf.Max(0.05f, 1 - _time / Lifetime);
			fleck.Mesh.Transparency = Mathf.Clamp((_time - 0.25f) / Lifetime, 0, 1);
		}
		if (_time >= Lifetime)
			QueueFree();
	}
}
