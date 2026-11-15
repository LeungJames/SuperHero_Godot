using Godot;

/// <summary>Follows the existing light pulse, so cadence and shot timing stay authoritative.</summary>
public partial class ComicMuzzleFlash : Node3D
{
	private OmniLight3D _light;
	private MeshInstance3D _burst;
	private float _age;
	private bool _lit;
	public override void _Ready()
	{
		_light = GetParent<OmniLight3D>();
		_burst = new MeshInstance3D
		{
			Mesh = ComicBurstMesh.Star, MaterialOverride = ComicBurstMesh.Billboard,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = new Vector3(0, 0, -0.025f),
		};
		AddChild(_burst);
	}
	public override void _Process(double delta)
	{
		if (_light.Visible && !_lit) _age = 0;
		_lit = _light.Visible;
		_age += (float)delta;
		_burst.Visible = _lit && _age < 0.085f;
		float radius = Mathf.Lerp(0.11f, 0.035f, Mathf.Clamp(_age / 0.085f, 0, 1));
		_burst.Scale = Vector3.One * radius;
	}
}
