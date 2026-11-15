using Godot;

/// <summary>
/// A7 激光剑气的瞬时表现：沿视线铺一段横向发光条，快速淡出（设计文档 §5.2.1）。
/// 只做视觉，不做命中判定，也不产生屏幕震动。
/// </summary>
public partial class LaserSlash : Node3D
{
	[Export] public float Lifetime { get; set; } = 0.2f;
	[Export] public float Thickness { get; set; } = 0.06f;

	private MeshInstance3D _mesh;
	private StandardMaterial3D _material;
	private float _remaining;

	public override void _Ready()
	{
		_mesh = GetNodeOrNull<MeshInstance3D>("Mesh");
	}

	/// <summary>放在光条起点，朝 forward 方向铺满 length 米。</summary>
	public void Play(Vector3 origin, Vector3 forward, float length, float width)
	{
		Vector3 direction = forward.LengthSquared() > 0.0001f ? forward.Normalized() : Vector3.Forward;
		float safeLength = Mathf.Max(length, 1f);
		float safeWidth = Mathf.Max(width, 0.1f);

		_material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(0.72f, 0.95f, 1f, 0.95f),
		};

		if (_mesh == null)
			return;

		_mesh.Mesh = new BoxMesh
		{
			Size = new Vector3(safeWidth, Thickness, safeLength),
			Material = _material,
		};

		// 长轴沿 -Z，用 LookAt 把 -Z 对到视线方向，再前移半段让光条从起点铺出去
		Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.98f ? Vector3.Forward : Vector3.Up;
		GlobalPosition = origin + direction * (safeLength * 0.5f);
		LookAt(GlobalPosition + direction, up);

		_remaining = Lifetime;
	}

	public override void _Process(double delta)
	{
		if (_material == null)
			return;

		_remaining = Mathf.Max(0f, _remaining - (float)delta);
		float k = 1f - _remaining / Mathf.Max(Lifetime, 0.01f);
		float alpha = Mathf.Pow(1f - k, 1.5f);
		_material.AlbedoColor = new Color(0.72f, 0.95f, 1f, alpha);

		if (_remaining <= 0f)
			QueueFree();
	}
}
