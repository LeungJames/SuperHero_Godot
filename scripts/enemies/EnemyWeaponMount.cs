using Godot;

/// <summary>
/// 敌人武器挂点（B 阶段）：把 EnemyData 指定的枪挂到 hand_r 骨骼上。
/// 位置跟随手骨，朝向跟随该敌人的实际射击方向（看得见玩家时指向玩家胸口，
/// 否则沿敌人正前方），因此不同武器共用同一挂点也能对准。
/// 同时把场景里的 Gun 标记与 MuzzleLight 拖到枪口，曳光与枪火不用改 EnemyAgent。
/// </summary>
public partial class EnemyWeaponMount : Node3D
{
	[Export] public EnemyData Data { get; set; }

	/// <summary>全身模型根节点；留空时自动查找同级带 Skeleton3D 的节点。</summary>
	[Export] public Node Model { get; set; }

	private EnemyAgent _agent;
	private EnemyAnimation _animation;
	private PlayerController _player;
	private Skeleton3D _skeleton;
	private int _handBone = -1;
	private Node3D _holder;
	private Marker3D _gun;
	private OmniLight3D _muzzleLight;

	public override void _Ready()
	{
		_agent = GetParent<EnemyAgent>();
		if (Data == null)
			Data = _agent.Data;
		if (Model == null)
			Model = FindModel();
		_animation = _agent.GetNodeOrNull<EnemyAnimation>("Animation");
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;

		_gun = _agent.GetNodeOrNull<Marker3D>("Gun");
		_muzzleLight = _agent.GetNodeOrNull<OmniLight3D>("MuzzleLight");

		_skeleton = Model != null ? Find<Skeleton3D>(Model) : null;
		if (_skeleton != null)
			_handBone = _skeleton.FindBone("hand_r");

		_holder = new Node3D { Name = "HeldWeapon" };
		AddChild(_holder);

		Build();
	}

	public override void _Process(double delta)
	{
		if (Data == null || _holder == null)
			return;

		// LOD 切换后要跟着当前正在驱动的那档骨架，否则武器会冻在旧姿势上
		Skeleton3D skeleton = _animation?.ActiveSkeleton ?? _skeleton;
		if (skeleton == null || _handBone < 0 || _handBone >= skeleton.GetBoneCount())
			return;

		Transform3D hand = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(_handBone);
		Vector3 origin = hand.Origin;

		Vector3 direction = -_agent.GlobalTransform.Basis.Z;
		if (_player != null && _agent.HasSight)
		{
			Vector3 toPlayer = _player.GlobalPosition + Vector3.Up * 1.1f - origin;
			if (toPlayer.LengthSquared() > 0.04f)
				direction = toPlayer.Normalized();
		}

		if (Mathf.Abs(direction.Dot(Vector3.Up)) > 0.98f)
			direction = -_agent.GlobalTransform.Basis.Z;

		var basis = Basis.LookingAt(direction, Vector3.Up);
		_holder.GlobalTransform = new Transform3D(basis, origin + basis * Data.WeaponGripOffset);

		if (_gun != null)
			_gun.GlobalPosition = _holder.GlobalTransform * Data.WeaponMuzzleOffset;
		if (_muzzleLight != null)
			_muzzleLight.GlobalPosition = (_gun != null ? _gun.GlobalPosition : origin)
				+ basis * new Vector3(0f, 0f, -0.03f);
	}

	private void Build()
	{
		if (_holder == null || Data?.WeaponScene == null)
			return;

		if (Data.WeaponScene.Instantiate() is not Node3D model)
		{
			GD.PushWarning($"{_agent?.Name}: WeaponScene 不是 Node3D。");
			return;
		}

		model.Name = "Model";
		_holder.AddChild(model);

		// 视角模型自带手套，第三人称由角色自己的手负责
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

	private static void ApplyToonMaterials(Node root)
	{
		ComicMaterials.Apply(root);
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

	private Node FindModel()
	{
		foreach (Node child in GetParent().GetChildren())
		{
			if (child == this)
				continue;
			if (Find<Skeleton3D>(child) != null)
				return child;
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
