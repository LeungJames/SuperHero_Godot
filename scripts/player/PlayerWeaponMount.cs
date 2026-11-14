using Godot;

/// <summary>
/// 第三人称武器挂点：把当前武器的模型挂到角色 hand_r 骨骼上（A 阶段）。
/// 第一人称仍由 WeaponController 的视角模型负责，这里只在第三人称显示，
/// 位置跟随手骨、朝向跟随玩家视线，避免依赖手骨自身轴向。
/// </summary>
public partial class PlayerWeaponMount : Node3D
{
	[Export] public PlayerAnimData Data { get; set; }
	[Export] public Node3D BodyModel { get; set; }
	[Export] public WeaponController Weapon { get; set; }

	private PlayerController _player;
	private PlayerCamera _camera;
	private Skeleton3D _skeleton;
	private int _handBone = -1;
	private Node3D _holder;
	private Node3D _visual;
	private WeaponData _visualData;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_camera = _player.GetNodeOrNull<PlayerCamera>("Head");

		if (BodyModel == null)
			BodyModel = FindBodyModel();
		if (Weapon == null)
			Weapon = _player.GetNodeOrNull<WeaponController>("Head/Camera3D/ViewModel/Weapon");
		_skeleton = BodyModel != null ? Find<Skeleton3D>(BodyModel) : null;
		if (_skeleton != null)
			_handBone = _skeleton.FindBone("hand_r");

		_holder = new Node3D { Name = "ThirdPersonWeapon" };
		AddChild(_holder);

		if (Weapon != null)
			Weapon.WeaponChanged += OnWeaponChanged;

		Rebuild();
	}

	public override void _Process(double delta)
	{
		bool visible = _camera != null && _camera.IsBodyVisible;
		Visible = visible;
		if (!visible || _visual == null || _skeleton == null || _handBone < 0 || _player == null)
			return;

		// 当前武器可能在首帧之后才构建完成
		if (_visualData != Weapon?.Data)
			Rebuild();
		if (_visual == null)
			return;

		Transform3D hand = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_handBone);
		float yaw = _player.GlobalRotation.Y + Mathf.DegToRad(Data?.WeaponYawOffset ?? 0f);
		float pitch = _camera != null ? Mathf.Clamp(_camera.Rotation.X, Mathf.DegToRad(-60f), Mathf.DegToRad(60f)) : 0f;
		Basis basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch);
		Vector3 offset = basis * (Data?.WeaponGripOffset ?? Vector3.Zero);
		_holder.GlobalTransform = new Transform3D(basis, hand.Origin + offset);
	}

	private void OnWeaponChanged(WeaponData data)
	{
		Rebuild();
	}

	private void Rebuild()
	{
		if (_visual != null)
		{
			_visual.QueueFree();
			_visual = null;
		}

		WeaponData data = Weapon?.Data;
		if (data?.Viewmodel == null || _holder == null)
			return;

		if (data.Viewmodel.Instantiate() is not Node3D model)
			return;

		model.Name = "Model";
		_holder.AddChild(model);
		_visual = model;
		_visualData = data;

		// 视角模型自带手套，第三人称由角色自己的手负责，隐藏掉避免穿帮
		HideNode(model, "HandLPivot");
		HideNode(model, "HandRPivot");

		model.AddChild(new ModelOutline { Overlay = OutlineMaterial() });
		ApplyToonMaterials(model);
	}

	private static void HideNode(Node root, string name)
	{
		if (root is Node3D node && node.Name == name)
		{
			node.Visible = false;
			return;
		}
		foreach (Node child in root.GetChildren())
			HideNode(child, name);
	}

	private static ShaderMaterial _outline;
	private static ShaderMaterial OutlineMaterial()
	{
		if (_outline != null)
			return _outline;

		_outline = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/outline.gdshader") };
		_outline.SetShaderParameter("width", 0.004f);
		return _outline;
	}

	private static readonly System.Collections.Generic.Dictionary<Color, ShaderMaterial> ToonCache = new();

	/// <summary>逐 surface 把武器纯色材质换成 toon 平涂，保留原配色。</summary>
	private static void ApplyToonMaterials(Node root)
	{
		if (root is MeshInstance3D mesh && mesh.Mesh != null)
		{
			for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
			{
				Material source = mesh.GetActiveMaterial(i);
				Color color = source is StandardMaterial3D standard ? standard.AlbedoColor : new Color(0.8f, 0.8f, 0.8f);
				mesh.SetSurfaceOverrideMaterial(i, ToonMaterial(color));
			}
		}

		foreach (Node child in root.GetChildren())
			ApplyToonMaterials(child);
	}

	private static ShaderMaterial ToonMaterial(Color color)
	{
		if (ToonCache.TryGetValue(color, out ShaderMaterial cached))
			return cached;

		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/toon.gdshader") };
		material.SetShaderParameter("albedo_color", color);
		ToonCache[color] = material;
		return material;
	}

	private Node3D FindBodyModel()
	{
		foreach (Node child in GetParent().GetChildren())
		{
			if (child is Node3D node && Find<Skeleton3D>(node) != null)
				return node;
		}
		return null;
	}

	private static T Find<T>(Node root) where T : Node
	{
		if (root == null)
			return null;
		if (root is T typed)
			return typed;
		foreach (Node child in root.GetChildren())
		{
			T found = Find<T>(child);
			if (found != null)
				return found;
		}
		return null;
	}
}
