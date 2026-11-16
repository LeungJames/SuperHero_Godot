using Godot;
using System.Collections.Generic;

/// <summary>
/// 铁锚据点 Boss 场地（2.0 设计 §8.1、§8.5）：
/// 闸门检查点、30×30 场地边界、4 根可打断的柱、可破坏锚机与 2 个高处锚环。
/// 场地几何在运行时按 <see cref="BossConfig"/> 搭建，锚环仍复用 HookAnchor.tscn。
/// </summary>
public partial class BossArena : Node3D
{
	private readonly List<ColumnInfo> _columns = new();
	private readonly List<Node3D> _anchors = new();

	[Export] public BossConfig Config { get; set; }
	[Export] public NodePath BossPath { get; set; }
	[Export] public PackedScene BreakScene { get; set; }

	public BossController Boss { get; private set; }
	public BossWinch Winch { get; private set; }
	public bool BossEngaged => Boss != null && Boss.IsEngaged;
	public int BrokenColumnCount { get; private set; }
	public int AnchorCount => _anchors.Count;
	public bool CheckpointArmed { get; private set; }

	private PlayerController _player;
	private Area3D _trigger;

	public override void _Ready()
	{
		AddToGroup("boss_arena");

		if (Config == null)
		{
			GD.PushError("BossArena: 缺少 BossConfig。");
			return;
		}

		Boss = GetNodeOrNull<BossController>(BossPath)
			?? GetTree().GetFirstNodeInGroup("boss") as BossController;

		if (Config.BuildArenaGeometry)
			BuildArenaGeometry();

		if (Winch != null)
			Winch.Destroyed += OnWinchDestroyed;

		BuildTrigger();
		CollectAnchors();
		Callable.From(BindDeferred).CallDeferred();
	}

	/// <summary>锚机破坏收益（§8.4）：停用一次落锚雨，次数由配置外置。</summary>
	private void OnWinchDestroyed()
	{
		Boss?.SuppressAnchorRain(Config?.WinchRainSuppressCount ?? 0);
	}

	private void BindDeferred()
	{
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
	}

	private void BuildTrigger()
	{
		_trigger = new Area3D
		{
			Name = "BossTrigger",
			CollisionLayer = 0,
			CollisionMask = 2,
			Monitoring = true,
		};

		var shape = new CollisionShape3D
		{
			Shape = new BoxShape3D
			{
				Size = new Vector3(Config.ArenaSize.X, 3f, Config.ArenaSize.Y),
			},
		};
		_trigger.AddChild(shape);
		AddChild(_trigger);
		_trigger.Position = Config.ArenaCenter + new Vector3(0f, 1.5f, 0f);
		_trigger.BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is not PlayerController)
			return;
		if (Boss == null || Boss.IsDefeated || Boss.IsEngaged)
			return;

		ArmCheckpoint();
		Boss.BeginEncounter();
	}

	private void ArmCheckpoint()
	{
		if (CheckpointArmed)
			return;

		CheckpointArmed = true;

		// 检查点重试：保留 ConsumedOnLoad，跳过已看过的登场过场
		if (RunCheckpoint.ConsumedOnLoad)
			return;

		string scenePath = Owner?.SceneFilePath ?? GetTree().CurrentScene?.SceneFilePath ?? string.Empty;
		RunCheckpoint.Set(scenePath, Config.CheckpointPosition, Config.CheckpointYaw);
	}

	/// <summary>Boss 的落锚雨砸断 1 根还没断的柱；优先半高掩体。</summary>
	public bool BreakColumn()
	{
		ColumnInfo target = null;
		foreach (ColumnInfo column in _columns)
		{
			if (column.Broken)
				continue;
			if (column.HalfHeight)
			{
				target = column;
				break;
			}

			target ??= column;
		}

		if (target == null)
			return false;

		target.Broken = true;
		target.Body.CollisionLayer = 0;
		if (target.Body.GetChildCount() > 0 && target.Body.GetChild(0) is MeshInstance3D mesh)
			mesh.Visible = false;
		BrokenColumnCount++;

		SpawnBreakFx(target.Body.GlobalPosition + Vector3.Up * 1.0f);
		AudioDirector.Instance?.PlayWorld(SfxKind.Land, target.Body.GlobalPosition, 2f, 0.65f);
		return true;
	}

	private void BuildArenaGeometry()
	{
		Node3D root = new() { Name = "BossArenaGeometry" };
		AddChild(root);

		Vector3 center = Config.ArenaCenter;
		float halfW = Config.ArenaSize.X * 0.5f;
		float halfL = Config.ArenaSize.Y * 0.5f;

		// 南侧闸门：中间留 6m 通道，两侧各 12m 墙
		CreateBox(root, "GateLeft", new Vector3(halfW - 3f, 6f, 0.4f),
			new Vector3(center.X - (halfW + 3f) * 0.5f, 3f, center.Z + halfL),
			new Color(0.32f, 0.24f, 0.19f));
		CreateBox(root, "GateRight", new Vector3(halfW - 3f, 6f, 0.4f),
			new Vector3(center.X + (halfW + 3f) * 0.5f, 3f, center.Z + halfL),
			new Color(0.32f, 0.24f, 0.19f));
		CreateBox(root, "GateBeam", new Vector3(6f, 1f, 0.5f),
			new Vector3(center.X, 5.5f, center.Z + halfL),
			new Color(0.55f, 0.27f, 0.12f));

		// 东西侧矮护栏，避开 x=±15 的屋顶坡道
		foreach (float side in new[] { -1f, 1f })
		{
			float x = center.X + side * halfW;
			CreateBox(root, $"Rail_{side}_North", new Vector3(0.35f, 1.2f, 6f),
				new Vector3(x, 0.6f, center.Z - 11.5f), new Color(0.58f, 0.3f, 0.13f));
			CreateBox(root, $"Rail_{side}_South", new Vector3(0.35f, 1.2f, 6f),
				new Vector3(x, 0.6f, center.Z + 11.5f), new Color(0.58f, 0.3f, 0.13f));
		}

		// 4 根柱：2 根半高掩体、2 根全高可绕
		CreateColumn(root, "BossColumn_HalfWest", new Vector3(center.X - 6f, 0.6f, center.Z + 9f),
			new Vector3(1.6f, 1.2f, 1.6f), true);
		CreateColumn(root, "BossColumn_HalfEast", new Vector3(center.X + 6f, 0.6f, center.Z + 9f),
			new Vector3(1.6f, 1.2f, 1.6f), true);
		CreateColumn(root, "BossColumn_FullWest", new Vector3(center.X - 6f, 3f, center.Z - 9f),
			new Vector3(1.6f, 6f, 1.6f), false);
		CreateColumn(root, "BossColumn_FullEast", new Vector3(center.X + 6f, 3f, center.Z - 9f),
			new Vector3(1.6f, 6f, 1.6f), false);

		// 锚环支撑塔
		foreach (Vector3 anchor in new[] { Config.AnchorLeft, Config.AnchorRight })
		{
			float x = anchor.X > center.X ? center.X + halfW - 0.9f : center.X - halfW + 0.9f;
			CreateBox(root, "AnchorPylon", new Vector3(0.8f, anchor.Y + 0.6f, 0.8f),
				new Vector3(x, (anchor.Y + 0.6f) * 0.5f, anchor.Z), new Color(0.3f, 0.25f, 0.22f));
			CreateBox(root, "AnchorArm", new Vector3(Mathf.Abs(anchor.X - x) + 0.8f, 0.25f, 0.5f),
				new Vector3((x + anchor.X) * 0.5f, anchor.Y, anchor.Z), new Color(0.52f, 0.27f, 0.12f));
		}

		// 地面警示条
		for (int i = -1; i <= 1; i++)
		{
			CreateBox(root, $"Hazard_{i}", new Vector3(2.2f, 0.02f, 0.5f),
				new Vector3(center.X + i * 2.6f, 0.02f, center.Z + halfL - 1.2f),
				new Color(0.86f, 0.66f, 0.14f), false);
		}

		BuildWinch(root);
	}

	private void CreateColumn(Node3D parent, string name, Vector3 position, Vector3 size, bool halfHeight)
	{
		StaticBody3D body = CreateBox(parent, name, size, position, new Color(0.38f, 0.29f, 0.23f));
		_columns.Add(new ColumnInfo { Body = body, HalfHeight = halfHeight });
	}

	private void BuildWinch(Node3D parent)
	{
		Winch = new BossWinch
		{
			Name = "BossWinch",
			MaxHealth = Config.WinchHealth,
			BreakScene = BreakScene,
			CollisionLayer = 1,
			CollisionMask = 0,
		};

		Node3D visual = new() { Name = "Visual" };
		Winch.AddChild(visual);

		MeshInstance3D baseMesh = new()
		{
			Mesh = new BoxMesh
			{
				Size = new Vector3(2.6f, 0.5f, 1.8f),
				Material = CreateMaterial(new Color(0.3f, 0.24f, 0.2f)),
			},
		};
		baseMesh.Position = new Vector3(0f, 0.25f, 0f);
		visual.AddChild(baseMesh);

		MeshInstance3D drum = new()
		{
			Mesh = new CylinderMesh
			{
				TopRadius = 0.75f,
				BottomRadius = 0.75f,
				Height = 2.4f,
				RadialSegments = 18,
				Material = CreateMaterial(new Color(0.58f, 0.3f, 0.13f)),
			},
			Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f),
		};
		drum.Position = new Vector3(0f, 1.15f, 0f);
		visual.AddChild(drum);

		MeshInstance3D chain = new()
		{
			Mesh = new BoxMesh
			{
				Size = new Vector3(0.12f, 1.6f, 0.12f),
				Material = CreateMaterial(new Color(0.2f, 0.19f, 0.18f)),
			},
		};
		chain.Position = new Vector3(0.86f, 0.9f, 0f);
		chain.Rotation = new Vector3(0f, 0f, 0.18f);
		visual.AddChild(chain);

		CollisionShape3D bodyShape = new()
		{
			Shape = new BoxShape3D { Size = new Vector3(2.6f, 2.2f, 2.0f) },
		};
		bodyShape.Position = new Vector3(0f, 1.1f, 0f);
		Winch.AddChild(bodyShape);

		Node3D hitboxes = new() { Name = "Hitboxes" };
		Hitbox area = new() { CollisionLayer = 8, CollisionMask = 0, DamageMultiplier = 1f };
		CollisionShape3D hitShape = new()
		{
			// 命中盒包住整台锚机，保证子弹先命中 Area 而不是外层 StaticBody
			Shape = new BoxShape3D { Size = new Vector3(3.0f, 2.6f, 2.4f) },
		};
		hitShape.Position = new Vector3(0f, 1.1f, 0f);
		area.AddChild(hitShape);
		hitboxes.AddChild(area);
		Winch.AddChild(hitboxes);

		parent.AddChild(Winch);
		Winch.Position = Config.WinchPosition;
	}

	/// <summary>场地锚环在关卡场景里静态摆放，这里只收集用于验收与后续扩展。</summary>
	private void CollectAnchors()
	{
		Vector3 center = Config.ArenaCenter;
		float halfX = Config.ArenaSize.X * 0.5f + 1f;
		float halfZ = Config.ArenaSize.Y * 0.5f + 1f;

		foreach (Node node in GetTree().GetNodesInGroup("hook_anchor"))
		{
			if (node is not Node3D anchor)
				continue;
			if (Mathf.Abs(anchor.GlobalPosition.X - center.X) > halfX)
				continue;
			if (Mathf.Abs(anchor.GlobalPosition.Z - center.Z) > halfZ)
				continue;
			if (Mathf.Abs(anchor.GlobalPosition.Y - Config.AnchorLeft.Y) > 0.6f)
				continue;
			if (anchor.GlobalPosition.Y < 3f)
				continue;

			_anchors.Add(anchor);
		}
	}

	private StaticBody3D CreateBox(Node3D parent, string name, Vector3 size, Vector3 position,
		Color color, bool collision = true)
	{
		StaticBody3D body = new()
		{
			Name = name,
			CollisionLayer = collision ? 1u : 0u,
			CollisionMask = 0,
		};

		MeshInstance3D mesh = new()
		{
			Mesh = new BoxMesh
			{
				Size = size,
				Material = CreateMaterial(color),
			},
		};
		body.AddChild(mesh);

		if (collision)
		{
			CollisionShape3D shape = new()
			{
				Shape = new BoxShape3D { Size = size },
			};
			body.AddChild(shape);
		}

		parent.AddChild(body);
		body.Position = position;
		return body;
	}

	private static StandardMaterial3D CreateMaterial(Color color) => new()
	{
		AlbedoColor = color,
		Metallic = 0.35f,
		Roughness = 0.7f,
	};

	private void SpawnBreakFx(Vector3 position)
	{
		if (BreakScene?.Instantiate() is not Node3D fx)
			return;

		GetTree().CurrentScene?.AddChild(fx);
		fx.GlobalPosition = position;
	}

	private sealed class ColumnInfo
	{
		public StaticBody3D Body;
		public bool HalfHeight;
		public bool Broken;
	}
}
