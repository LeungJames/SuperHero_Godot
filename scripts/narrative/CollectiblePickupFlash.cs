using Godot;

/// <summary>A short visual acknowledgement with no screen-space text.</summary>
public partial class CollectiblePickupFlash : Node3D
{
	private const float Lifetime = 0.32f;
	private float _age;
	private MeshInstance3D _ring;
	private readonly MeshInstance3D[] _flecks = new MeshInstance3D[4];

	public override void _Ready()
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color("c2e5df"),
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			BillboardKeepScale = true,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		// This annulus is authored in XY so billboarding keeps its face toward the camera.
		var surface = new SurfaceTool();
		surface.Begin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < 32; i++)
		{
			float a = i * Mathf.Tau / 32, b = (i + 1) * Mathf.Tau / 32;
			Vector3 p = new(Mathf.Cos(a), Mathf.Sin(a), 0), q = new(Mathf.Cos(b), Mathf.Sin(b), 0);
			foreach (Vector3 v in new[] { p * 0.92f, p, q, p * 0.92f, q, q * 0.92f }) surface.AddVertex(v);
		}
		_ring = new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		surface.Dispose();
		AddChild(_ring);
		var fleckMesh = new PrismMesh { Size = new Vector3(0.035f, 0.065f, 0.008f) };
		for (int i = 0; i < _flecks.Length; i++)
		{
			_flecks[i] = new MeshInstance3D { Mesh = fleckMesh, MaterialOverride = material,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild(_flecks[i]);
		}
		UpdateVisuals();
	}

	public override void _Process(double delta)
	{
		_age += (float)delta;
		if (_age >= Lifetime) { QueueFree(); return; }
		UpdateVisuals();
	}

	private void UpdateVisuals()
	{
		float t = Mathf.Clamp(_age / Lifetime, 0, 1);
		_ring.Scale = Vector3.One * Mathf.Lerp(0.12f, 0.43f, t);
		_ring.Transparency = t;
		for (int i = 0; i < _flecks.Length; i++)
		{
			float angle = i * Mathf.Tau / _flecks.Length + Mathf.Pi / 4;
			_flecks[i].Position = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * Mathf.Lerp(0.08f, 0.30f, t);
			_flecks[i].Scale = Vector3.One * (1 - t);
			_flecks[i].Transparency = t;
		}
	}
}
