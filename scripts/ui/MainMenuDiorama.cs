using Godot;

/// <summary>
/// 主菜单背后的黄昏港口立体剪影（2.0 设计 §1.3 美术口径）：
/// 复用 toon 两阶硬切与反向壳描边，只用盒子/圆柱搭货场、集装箱、吊机与远景仓库。
/// 不加载正式关卡，避免主菜单拉起敌人、导航与存档流程。
/// </summary>
public partial class MainMenuDiorama : Node3D
{
	private static readonly Color Quay = new("6b5a49");
	private static readonly Color QuayEdge = new("b8792f");
	private static readonly Color Water = new("27455c");
	private static readonly Color Rust = new("b5542a");
	private static readonly Color Teal = new("2f6571");
	private static readonly Color Salt = new("d8cdb7");
	private static readonly Color Silhouette = new("2a2b33");
	private static readonly Color Steel = new("3c4550");

	private Node3D _hook;
	private OmniLight3D _warningLight;
	private float _time;

	public override void _Ready()
	{
		Build();

		var ink = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/outline.gdshader") };
		ink.SetShaderParameter("width", 0.02f);
		var outline = new ModelOutline { Name = "MenuInk", Overlay = ink };
		AddChild(outline);
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;

		if (_hook != null)
			_hook.Rotation = new Vector3(Mathf.Sin(_time * 0.8f) * 0.06f, 0f, Mathf.Sin(_time * 0.55f) * 0.09f);

		if (_warningLight != null)
			_warningLight.LightEnergy = 1.6f + Mathf.Sin(_time * 2.4f) * 1.2f;
	}

	private void Build()
	{
		Box("Water", new Vector3(70f, 0.1f, 70f), new Vector3(0f, -0.55f, -8f), Water);
		Box("Quay", new Vector3(30f, 0.7f, 20f), new Vector3(0f, -0.05f, 1f), Quay);
		Box("QuayEdge", new Vector3(30f, 0.24f, 0.5f), new Vector3(0f, 0.4f, 10.7f), QuayEdge);

		// 集装箱堆：锈橙 / 咸青 / 盐白，右侧给镜头留出层次
		Box("Container_A", new Vector3(4.6f, 1.6f, 2.4f), new Vector3(4.6f, 0.95f, 2.2f), Rust);
		Box("Container_B", new Vector3(3.8f, 1.6f, 2.4f), new Vector3(7.9f, 0.95f, 3.4f), Teal);
		Box("Container_C", new Vector3(3.6f, 1.6f, 2.2f), new Vector3(8.2f, 2.55f, 2.4f), Salt);
		Box("Container_D", new Vector3(3.2f, 1.5f, 2.2f), new Vector3(-2.2f, 0.9f, 1.2f), Salt);
		Box("Container_E", new Vector3(2.8f, 1.5f, 2.2f), new Vector3(-5.2f, 0.9f, 1.9f), Teal);
		Box("Pallet", new Vector3(2.2f, 0.3f, 1.8f), new Vector3(1.2f, 0.55f, 5.0f), new Color("8a6f4d"));

		// 吊机：两条腿 + 横梁 + 悬臂 + 吊索与吊钩
		Box("CraneLeg_West", new Vector3(0.7f, 8.2f, 0.7f), new Vector3(5.2f, 4.4f, -4.4f), Silhouette);
		Box("CraneLeg_East", new Vector3(0.7f, 8.2f, 0.7f), new Vector3(9.4f, 4.4f, -4.4f), Silhouette);
		Box("CraneBeam", new Vector3(5.2f, 0.7f, 0.9f), new Vector3(7.3f, 8.4f, -4.4f), Silhouette);
		Box("CraneJib", new Vector3(12.5f, 0.55f, 0.7f), new Vector3(4.6f, 8.9f, -4.4f), Silhouette);
		Box("CraneCab", new Vector3(1.7f, 1.3f, 1.6f), new Vector3(9.4f, 7.5f, -4.4f), Steel);

		_hook = new Node3D { Name = "HookPivot", Position = new Vector3(-0.4f, 8.7f, -4.4f) };
		AddChild(_hook);
		var cable = new MeshInstance3D
		{
			Name = "CraneCable",
			Mesh = new BoxMesh { Size = new Vector3(0.09f, 4.6f, 0.09f), Material = Ink() },
			Position = new Vector3(0f, -2.3f, 0f),
		};
		_hook.AddChild(cable);
		var hook = new MeshInstance3D
		{
			Name = "CraneHook",
			Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.7f, 0.5f), Material = new StandardMaterial3D { AlbedoColor = QuayEdge } },
			Position = new Vector3(0f, -4.8f, 0f),
		};
		_hook.AddChild(hook);

		// 锚形吊牌：铁锚帮的符号，挂在吊机梁下
		Box("AnchorPlate", new Vector3(0.09f, 1.5f, 0.09f), new Vector3(9.4f, 6.0f, -4.0f), QuayEdge);
		Box("AnchorCross", new Vector3(1.3f, 0.12f, 0.09f), new Vector3(9.4f, 5.55f, -4.0f), QuayEdge);
		Box("AnchorFluke_West", new Vector3(0.55f, 0.12f, 0.09f), new Vector3(9.12f, 5.25f, -4.0f), QuayEdge).RotationDegrees = new Vector3(0f, 0f, 38f);
		Box("AnchorFluke_East", new Vector3(0.55f, 0.12f, 0.09f), new Vector3(9.68f, 5.25f, -4.0f), QuayEdge).RotationDegrees = new Vector3(0f, 0f, -38f);

		_warningLight = new OmniLight3D
		{
			Name = "WarningLight",
			Position = new Vector3(9.4f, 9.3f, -4.2f),
			OmniRange = 3.2f,
			LightColor = new Color("ff6a3d"),
		};
		AddChild(_warningLight);

		// 远景仓库与烟囱剪影
		Box("Warehouse_A", new Vector3(9f, 4.5f, 4f), new Vector3(-7f, 2.2f, -11f), Silhouette);
		Box("Warehouse_B", new Vector3(6f, 3.2f, 4f), new Vector3(3.5f, 1.6f, -12f), Silhouette);
		Box("Chimney", new Vector3(1.2f, 9f, 1.2f), new Vector3(-11.5f, 4.5f, -13f), Silhouette);
		Box("ChimneyCap", new Vector3(1.6f, 0.4f, 1.6f), new Vector3(-11.5f, 9.2f, -13f), new Color("4a3a32"));

		for (int i = 0; i < 4; i++)
			Cylinder($"Bollard_{i}", 0.22f, 0.9f, new Vector3(-3.4f + i * 2.3f, 0.75f, 9.4f), QuayEdge);

		// 系船桩之间的锚链
		Box("MooringChain", new Vector3(7.2f, 0.08f, 0.08f), new Vector3(-0.1f, 0.55f, 9.5f), new Color("4a4140"));
	}

	private MeshInstance3D Box(string name, Vector3 size, Vector3 position, Color color)
	{
		var node = new MeshInstance3D
		{
			Name = name,
			Mesh = new BoxMesh { Size = size, Material = new StandardMaterial3D { AlbedoColor = color } },
			Position = position,
		};
		AddChild(node);
		return node;
	}

	private void Cylinder(string name, float radius, float height, Vector3 position, Color color)
	{
		var node = new MeshInstance3D
		{
			Name = name,
			Mesh = new CylinderMesh
			{
				TopRadius = radius,
				BottomRadius = radius,
				Height = height,
				Material = new StandardMaterial3D { AlbedoColor = color },
			},
			Position = position,
		};
		AddChild(node);
	}

	private static StandardMaterial3D Ink() => new() { AlbedoColor = Silhouette };
}
