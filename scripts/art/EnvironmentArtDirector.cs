using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Visual-only art assembly for the three existing levels. Keeps every collision,
/// navigation resource, spawn and route intact. Repeated hardware is batched by
/// material and 32 m cell; no per-frame scripts or physics bodies are introduced.
/// </summary>
public partial class EnvironmentArtDirector : Node3D
{
	[Export] public string Chapter { get; set; } = "port";
	private readonly Dictionary<string, ShaderMaterial> _surfaces = new();
	private readonly Dictionary<string, Batch> _batches = new();
	private readonly Dictionary<string, PackedScene> _props = new();
	private Shader _surfaceShader;
	private ShaderMaterial _outline;
	private Node3D _level;
	private readonly Color _ink = new("202d39");
	private readonly Color _steel = new("58646e");
	private readonly Color _edge = new("879299");
	private readonly Color _ochre = new("d99432");
	private readonly Color _paper = new("d1c7a4");
	private readonly Color[] _cargo = { new("a84c32"), new("3a737d"), new("68734d"), new("b28c42"), new("4b6079") };
	private sealed class Batch
	{
		public Mesh Mesh;
		public Material Material;
		public readonly List<Transform3D> Transforms = new();
		public bool CastShadows;
	}
	private sealed class ArchitectureBatch
	{
		public SurfaceTool Surface;
		public Material Material;
	}

	public override void _Ready()
	{
		_level = GetParent<Node3D>();
		_surfaceShader = GD.Load<Shader>("res://shaders/environment_surface.gdshader");
		_outline = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/outline.gdshader") };
		_outline.SetShaderParameter("pixel_width", 0.85f);
		var bodies = new List<StaticBody3D>();
		CollectBodies(_level, bodies);
		foreach (StaticBody3D body in bodies)
			DressBody(body);
		BatchExistingArchitecture(bodies);

		if (Chapter == "factory")
			BuildFactory();
		else
		{
			BuildSeascape();
			if (Chapter == "anchor") BuildAnchor(); else BuildPort();
		}
		FlushBatches();
		SetProcess(false);
	}

	private static void CollectBodies(Node root, List<StaticBody3D> output)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is CharacterBody3D || child is EnvironmentArtDirector) continue;
			if (child is StaticBody3D body) output.Add(body);
			CollectBodies(child, output);
		}
	}

	private void BatchExistingArchitecture(List<StaticBody3D> bodies)
	{
		// The original kit consists of many individually drawn parts. Consolidate only
		// immutable geometry, retaining the original tree and its gameplay collisions.
		var groups = new Dictionary<string, ArchitectureBatch>();
		foreach (StaticBody3D body in bodies)
		{
			if (body.GetScript().VariantType != Variant.Type.Nil) continue;
			foreach (MeshInstance3D mesh in BodyMeshes(body))
			{
				if (!mesh.Visible || !mesh.IsVisibleInTree() || mesh.Mesh == null || mesh.MaterialOverlay != null) continue;
				Transform3D transform = GlobalTransform.AffineInverse() * mesh.GlobalTransform;
				Vector3 center = transform * mesh.GetAabb().GetCenter();
				string cell = $"{Mathf.FloorToInt(center.X / 32f)}:{Mathf.FloorToInt(center.Z / 32f)}";
				for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
				{
					Material material = mesh.GetActiveMaterial(i);
					if (material == null) continue;
					string key = material.GetInstanceId() + ":" + SurfaceFormat(mesh.Mesh, i) + ":" + cell;
					if (!groups.TryGetValue(key, out ArchitectureBatch batch))
					{
						batch = new ArchitectureBatch { Surface = new SurfaceTool(), Material = material };
						batch.Surface.Begin(Mesh.PrimitiveType.Triangles);
						batch.Surface.SetMaterial(material);
						groups.Add(key, batch);
					}
					batch.Surface.AppendFrom(mesh.Mesh, i, transform);
				}
				mesh.Visible = false;
			}
		}
		foreach (ArchitectureBatch batch in groups.Values)
		{
			ArrayMesh mesh = batch.Surface.Commit();
			batch.Surface.Dispose();
			if (mesh == null) continue;
			AddChild(new MeshInstance3D { Name = "ArchitectureBatch", Mesh = mesh, MaterialOverride = batch.Material });
		}
	}

	private static IEnumerable<MeshInstance3D> BodyMeshes(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is MeshInstance3D mesh) yield return mesh;
			foreach (MeshInstance3D descendant in BodyMeshes(child)) yield return descendant;
		}
	}

	private static ulong SurfaceFormat(Mesh mesh, int surface)
	{
		if (mesh is ArrayMesh array) return (ulong)array.SurfaceGetFormat(surface);
		Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
		ulong flags = 0;
		for (int i = 0; i < arrays.Count; i++)
			if (arrays[i].VariantType != Variant.Type.Nil) flags |= 1UL << i;
		return flags;
	}

	private void DressBody(StaticBody3D body)
	{
		string name = body.Name.ToString().ToLowerInvariant();
		string source = SceneSource(body).ToLowerInvariant();
		CollisionShape3D collision = null;
		foreach (Node child in body.GetChildren())
			if (child is CollisionShape3D c && c.Shape is BoxShape3D) { collision = c; break; }
		if (collision?.Shape is not BoxShape3D shape) return;

		Vector3 size = shape.Size;
		Transform3D frame = GlobalTransform.AffineInverse() * collision.GlobalTransform;
		bool container = source.Contains("container") || name.Contains("_cover_") || name.StartsWith("roofa_col") || name.StartsWith("roofb_col")
			|| name.StartsWith("roofc_col") || name.StartsWith("roofd_col") || name.StartsWith("roofe_col") || name.StartsWith("rooff_col") || name.StartsWith("roofg_col");
		bool wall = name.StartsWith("wall") || name.Contains("corridor_wall") || name.Contains("yard_wall") || name == "machineryhouse";
		bool ground = name.StartsWith("ground") || name == "floor" || name.StartsWith("quay_");
		bool deck = !ground && size.Y <= 1.05f && size.X >= 1.2f && size.Z >= 1.2f && !name.Contains("threshold") && !name.Contains("curb");
		bool rail = name.Contains("rail") && size.Y > 0.6f && size.Y < 1.6f;
		bool crate = source.Contains("crate") || name.Contains("crate") || name == "counterweight";
		bool structure = name.Contains("leg_") || name.Contains("ring_") || name.Contains("brace_") || name.Contains("aframe") || name.Contains("boom") || name.Contains("stairbeam") || name.Contains("post_");
		if (container)
		{
			Color color = _cargo[StableHash(body.Name.ToString()) % _cargo.Length];
			Paint(body, Surface(color, 2, true));
			ContainerHardware(frame, size);
		}
		else if (wall)
		{
			Color wallColor = name == "machineryhouse" ? new Color("485462") : Chapter == "factory" ? new Color("52656c") : new Color("63717c");
			Paint(body, Surface(wallColor, 5, true));
			WallHardware(frame, size);
		}
		else if (ground) Paint(body, Surface(new Color("7b8185"), 1));
		else if (deck)
		{
			Paint(body, Surface(new Color("787c82"), 3, true));
			DeckHardware(frame, size);
		}
		else if (rail)
		{
			HideMeshes(body);
			RailHardware(frame, size);
		}
		else if (structure) Paint(body, Surface(new Color("b96539"), 0, true));
		else if (crate)
		{
			// Swap the visual inside its existing collision envelope, so cargo never
			// introduces a decorative obstacle the player can accidentally walk through.
			HideMeshes(body);
			Node3D cargo = Prop("cargo_crate", Vector3.Zero);
			if (cargo != null)
				cargo.Transform = frame * new Transform3D(Basis.Identity.Scaled(new Vector3(size.X / 1.04f, size.Y / 1.08f, size.Z / 1.04f)), new Vector3(0, -size.Y * 0.5f, 0));
		}
		else if (name.Contains("barrier") || name.Contains("curb")) Paint(body, Surface(_ochre, 4, true));
		else if (name.Contains("tank"))
		{
			Paint(body, Surface(new Color("6d7f7c"), 0, true));
			TankHardware(frame, size);
		}
	}

	private static string SceneSource(Node node)
	{
		if (!string.IsNullOrEmpty(node.SceneFilePath)) return node.SceneFilePath;
		foreach (Node child in node.GetChildren())
		{
			string path = SceneSource(child);
			if (!string.IsNullOrEmpty(path)) return path;
		}
		return string.Empty;
	}

	private static void Paint(Node node, Material material)
	{
		if (node is MeshInstance3D mesh) mesh.MaterialOverride = material;
		foreach (Node child in node.GetChildren()) Paint(child, material);
	}

	private static void HideMeshes(Node node)
	{
		if (node is MeshInstance3D mesh) mesh.Visible = false;
		foreach (Node child in node.GetChildren()) HideMeshes(child);
	}

	private ShaderMaterial Surface(Color color, int kind = 0, bool outlined = false)
	{
		string key = color.ToHtml() + ":" + kind + ":" + outlined;
		if (_surfaces.TryGetValue(key, out ShaderMaterial cached)) return cached;
		var material = new ShaderMaterial { Shader = _surfaceShader };
		material.SetShaderParameter("base_color", color);
		material.SetShaderParameter("surface_kind", kind);
		if (outlined) material.NextPass = _outline;
		_surfaces[key] = material;
		return material;
	}

	private void Box(Transform3D frame, Vector3 position, Vector3 size, Color color, int kind = 0, bool castShadows = false)
	{
		Transform3D transform = frame * new Transform3D(Basis.Identity.Scaled(size), position);
		string cell = $"{Mathf.FloorToInt(transform.Origin.X / 32f)}:{Mathf.FloorToInt(transform.Origin.Z / 32f)}";
		string key = "box:" + color.ToHtml() + ":" + kind + ":" + cell + ":" + castShadows;
		if (!_batches.TryGetValue(key, out Batch batch))
		{
			batch = new Batch { Mesh = new BoxMesh { Size = Vector3.One }, Material = Surface(color, kind), CastShadows = castShadows };
			_batches.Add(key, batch);
		}
		batch.Transforms.Add(transform);
	}

	private void Beam(Vector3 a, Vector3 b, float width, Color color)
	{
		Vector3 delta = b - a;
		if (delta.Length() < 0.001f) return;
		Vector3 y = delta.Normalized();
		Vector3 x = (Mathf.Abs(y.Dot(Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up.Cross(y)).Normalized();
		Vector3 z = x.Cross(y).Normalized();
		Box(new Transform3D(new Basis(x, y, z), (a + b) * 0.5f), Vector3.Zero, new Vector3(width, delta.Length(), width), color);
	}

	private void ContainerHardware(Transform3D frame, Vector3 size)
	{
		// Only shallow hardware: the original collision envelope remains authoritative.
		if (size.X > 14f || size.Z > 14f || size.Y > 22f) return;
		float x = size.X * 0.5f, z = size.Z * 0.5f, y = size.Y * 0.5f;
		for (int sx = -1; sx <= 1; sx += 2)
			for (int sz = -1; sz <= 1; sz += 2)
				Box(frame, new Vector3(sx * (x - 0.045f), 0, sz * (z - 0.045f)), new Vector3(0.11f, size.Y, 0.11f), _ink);
		foreach (float sy in new[] { -y + 0.06f, y - 0.06f })
		{
			Box(frame, new Vector3(0, sy, z + 0.012f), new Vector3(size.X, 0.12f, 0.04f), _edge);
			Box(frame, new Vector3(0, sy, -z - 0.012f), new Vector3(size.X, 0.12f, 0.04f), _ink);
			Box(frame, new Vector3(x + 0.012f, sy, 0), new Vector3(0.04f, 0.12f, size.Z), _edge);
			Box(frame, new Vector3(-x - 0.012f, sy, 0), new Vector3(0.04f, 0.12f, size.Z), _ink);
		}
		// Narrow face cargo doors and two locking bars; tall stacks retain vertical seams.
		bool alongX = size.X > size.Z;
		float faceWidth = alongX ? size.Z : size.X;
		Transform3D door = frame * new Transform3D(new Basis(Vector3.Up, alongX ? Mathf.Pi / 2f : 0f), alongX ? new Vector3(x + 0.025f, 0, 0) : new Vector3(0, 0, z + 0.025f));
		Box(door, Vector3.Zero, new Vector3(0.025f, size.Y - 0.12f, 0.025f), _ink);
		for (int side = -1; side <= 1; side += 2)
		{
			float barX = side * faceWidth * 0.27f;
			Box(door, new Vector3(barX, 0, 0.03f), new Vector3(0.045f, size.Y - 0.28f, 0.045f), _edge);
			Box(door, new Vector3(barX, -y + 0.8f, 0.07f), new Vector3(0.20f, 0.055f, 0.04f), _ink);
			for (float h = -y + 0.2f; h < y; h += 2.3f)
				Box(door, new Vector3(side * (faceWidth * 0.5f - 0.11f), h, 0.035f), new Vector3(0.16f, 0.09f, 0.04f), _edge);
		}
	}

	private void WallHardware(Transform3D frame, Vector3 size)
	{
		float x = size.X * 0.5f, z = size.Z * 0.5f, y = size.Y * 0.5f;
		bool alongX = size.X >= size.Z;
		float length = alongX ? size.X : size.Z;
		if (length > 60f) return;
		for (int side = -1; side <= 1; side += 2)
		{
			for (float p = -length * 0.5f + 0.12f; p < length * 0.5f; p += 2.4f)
			{
				Vector3 pos = alongX ? new Vector3(p, 0, side * (z + 0.018f)) : new Vector3(side * (x + 0.018f), 0, p);
				Box(frame, pos, alongX ? new Vector3(0.045f, size.Y, 0.04f) : new Vector3(0.04f, size.Y, 0.045f), _ink);
				foreach (float h in new[] { -y + 0.15f, y - 0.15f })
					Box(frame, pos + new Vector3(0, h, 0), new Vector3(0.07f, 0.07f, 0.07f), _edge);
			}
			foreach (float h in new[] { -y + 0.10f, y - 0.10f })
				Box(frame, alongX ? new Vector3(0, h, side * (z + 0.02f)) : new Vector3(side * (x + 0.02f), h, 0),
					alongX ? new Vector3(size.X, 0.13f, 0.05f) : new Vector3(0.05f, 0.13f, size.Z), _ink);
		}
	}

	private void DeckHardware(Transform3D frame, Vector3 size)
	{
		if (size.X > 35f || size.Z > 40f) return;
		float y = size.Y * 0.5f + 0.008f;
		// Fasteners at plate corners; repeated bolts share one MultiMesh per cell.
		for (float x = -size.X * 0.5f + 0.16f; x < size.X * 0.5f; x += 2f)
			for (float z = -size.Z * 0.5f + 0.16f; z < size.Z * 0.5f; z += 2f)
				Box(frame, new Vector3(x, y, z), new Vector3(0.055f, 0.012f, 0.055f), _edge);
		foreach (int side in new[] { -1, 1 })
		{
			Box(frame, new Vector3(side * (size.X * 0.5f - 0.06f), y, 0), new Vector3(0.10f, 0.012f, size.Z), _ochre);
			Box(frame, new Vector3(0, y, side * (size.Z * 0.5f - 0.06f)), new Vector3(size.X, 0.012f, 0.10f), _ochre);
		}
	}

	private void TankHardware(Transform3D frame, Vector3 size)
	{
		foreach (float y in new[] { -size.Y * 0.3f, size.Y * 0.3f })
			foreach (int side in new[] { -1, 1 })
				Box(frame, new Vector3(0, y, side * (size.Z * 0.5f + 0.025f)), new Vector3(size.X, 0.10f, 0.045f), _ink);
	}

	private void RailHardware(Transform3D frame, Vector3 size)
	{
		bool alongX = size.X > size.Z;
		float length = alongX ? size.X : size.Z;
		float y = size.Y * 0.5f;
		foreach (float height in new[] { y - 0.045f, 0f })
			Box(frame, new Vector3(0, height, 0), alongX ? new Vector3(length, 0.065f, 0.065f) : new Vector3(0.065f, 0.065f, length), _ochre, 0, true);
		Box(frame, new Vector3(0, -y + 0.065f, 0), alongX ? new Vector3(length, 0.13f, 0.08f) : new Vector3(0.08f, 0.13f, length), _ink);
		int spans = Math.Max(1, (int)Mathf.Ceil(length / 1.8f));
		for (int i = 0; i <= spans; i++)
		{
			float p = Mathf.Lerp(-length * 0.5f + 0.035f, length * 0.5f - 0.035f, i / (float)spans);
			Box(frame, alongX ? new Vector3(p, 0, 0) : new Vector3(0, 0, p), new Vector3(0.07f, size.Y, 0.07f), _ochre, 0, true);
		}
	}

	private Node3D Prop(string name, Vector3 position, float yaw = 0f, float scale = 1f)
	{
		if (!_props.TryGetValue(name, out PackedScene scene))
		{
			scene = GD.Load<PackedScene>("res://art/environment/" + name + ".glb");
			_props.Add(name, scene);
		}
		if (scene == null) return null;
		Node3D prop = scene.Instantiate<Node3D>();
		prop.Name = name;
		prop.Position = position;
		prop.Rotation = new Vector3(0, yaw, 0);
		prop.Scale = Vector3.One * scale;
		ComicMaterials.Apply(prop);
		OutlineProp(prop);
		AddChild(prop);
		return prop;
	}

	private void OutlineProp(Node node)
	{
		if (node is MeshInstance3D mesh) mesh.MaterialOverlay = _outline;
		foreach (Node child in node.GetChildren()) OutlineProp(child);
	}

	private void WallLamp(Vector3 pos, float yaw = 0f, bool illuminate = false)
	{
		Prop("bulkhead_lamp", pos, yaw);
		if (!illuminate) return;
		var light = new OmniLight3D
		{
			Position = pos + new Vector3(0, 0.25f, 0), LightColor = new Color("ffd08b"),
			LightEnergy = 0.5f, OmniRange = 4.5f, ShadowEnabled = false,
			DistanceFadeEnabled = true, DistanceFadeBegin = 25f, DistanceFadeLength = 8f,
		};
		AddChild(light);
	}

	private void Sign(string text, Vector3 position, float yaw, float width, Color color)
	{
		Transform3D frame = new(new Basis(Vector3.Up, yaw), position);
		Box(frame, Vector3.Zero, new Vector3(width, 0.55f, 0.055f), _ink);
		Box(frame, new Vector3(0, 0, 0.035f), new Vector3(width - 0.10f, 0.45f, 0.015f), color);
		var label = new Label3D
		{
			Text = text, Position = position + frame.Basis.Z * 0.06f,
			Rotation = new Vector3(0, yaw, 0), FontSize = 48, PixelSize = Mathf.Min(0.008f, width / Math.Max(1, text.Length) / 34f),
			Modulate = _paper, OutlineSize = 4, OutlineModulate = _ink,
			NoDepthTest = false, Shaded = false,
		};
		AddChild(label);
	}

	private void Cable(Vector3 a, Vector3 b, float sag, float width = 0.035f)
	{
		Vector3 previous = a;
		for (int i = 1; i <= 12; i++)
		{
			float t = i / 12f;
			Vector3 next = a.Lerp(b, t) - Vector3.Up * (Mathf.Sin(t * Mathf.Pi) * sag);
			Beam(previous, next, width, _ink);
			previous = next;
		}
	}

	private void BuildPort()
	{
		// First-person opening composition: readable machinery, lit wall and deck detail.
		Prop("service_cabinet", new Vector3(5.4f, 42.12f, 75.54f));
		WallLamp(new Vector3(3.7f, 44.25f, 75.54f), 0, true);
		WallLamp(new Vector3(7.2f, 44.25f, 75.54f));
		Sign("04 / CRANE", new Vector3(5.1f, 44.3f, 75.56f), 0, 1.6f, new Color("435c69"));
		for (int i = 0; i < 8; i++)
			Box(Transform3D.Identity, new Vector3(4.7f, 43.9f + i * 0.06f, 75.56f), new Vector3(0.70f, 0.025f, 0.035f), _ink);
		// Overhead utility loops remain above head height and outside the jump openings.
		Cable(new Vector3(-5.4f, 46.4f, 69), new Vector3(7.2f, 46.4f, 69), 1.1f, 0.055f);
		Cable(new Vector3(7.4f, 45.5f, 70), new Vector3(7.4f, 45.5f, 80), 1.2f, 0.045f);
		for (int i = 0; i < 6; i++)
		{
			float y = 1.6f + i * 6.6f;
			WallLamp(new Vector3(-5.96f, y, 81.7f), -Mathf.Pi / 2f);
		}
		// Pallets sit inside the existing rack footprint, outside the playable aisle.
		Prop("pallet_stack", new Vector3(21, 0.06f, -18), 0, 0.8f);
		Prop("pallet_stack", new Vector3(35, 0.06f, -18), 0, 0.8f);
		Prop("service_cabinet", new Vector3(14.23f, 0.2f, -18), Mathf.Pi / 2f);
		Sign("SALT HARBOR  /  07", new Vector3(29, 4.5f, 6.23f), 0, 5.3f, new Color("435c69"));
		Sign("WAREHOUSE 02", new Vector3(29, 4.9f, -28.24f), Mathf.Pi, 4.2f, new Color("435c69"));
		foreach (float z in new[] { -23f, -11f, 1f })
			WallLamp(new Vector3(13.76f, 3.8f, z), -Mathf.Pi / 2f, z == -11f);
		// Quayside tires and mooring lines provide scale below the walkable deck.
		for (int i = 0; i < 8; i++)
		{
			float z = -48 + i * 18;
			Cable(new Vector3(44.5f, -0.7f, z - 2), new Vector3(44.5f, -0.7f, z + 2), 0.9f, 0.10f);
		}
	}

	private void BuildFactory()
	{
		// Tie the existing overhead pipes and short trusses into the wall structure.
		// All additions are above the encounter space and have no physics shape.
		foreach (float z in new[] { 0f, 2f, 6f })
		{
			Box(Transform3D.Identity, new Vector3(0, 12.4f, z), new Vector3(48, 0.26f, 0.22f), _steel);
			foreach (float x in new[] { -23.8f, 23.8f })
				Beam(new Vector3(x, 9, z), new Vector3(x, 12.4f, z), 0.22f, _steel);
		}
		foreach (float x in new[] { -5.7f, 5.7f })
		{
			Beam(new Vector3(x, 10.4f, 0), new Vector3(x, 12.4f, 0), 0.065f, _ink);
			Beam(new Vector3(x, 10.9f, 6), new Vector3(x, 12.4f, 6), 0.065f, _ink);
		}
		Sign("RED SERPENT  /  WORKS", new Vector3(0, 8.7f, 23.74f), Mathf.Pi, 6.7f, new Color("8c3e32"));
		Sign("AUTHORIZED PERSONNEL", new Vector3(0, 7.4f, -23.74f), 0, 6.3f, new Color("8c3e32"));
		// Twin high-level conduits, brackets and suspended task lighting.
		foreach (float x in new[] { -22.9f, 22.9f })
		{
			Beam(new Vector3(x, 8.4f, -23), new Vector3(x, 8.4f, 23), 0.18f, _ochre);
			Beam(new Vector3(x, 9.1f, -23), new Vector3(x, 9.1f, 23), 0.12f, _ink);
			for (int j = 0; j < 6; j++)
			{
				float z = -20 + j * 8;
				Beam(new Vector3(x, 8.15f, z), new Vector3(x, 9.4f, z), 0.10f, _steel);
				WallLamp(new Vector3(x, 4.8f, z), x < 0 ? Mathf.Pi / 2 : -Mathf.Pi / 2, j % 3 == 0);
			}
		}
		foreach (float z in new[] { -16f, -3f, 12f })
		{
			Cable(new Vector3(-23, 10.8f, z), new Vector3(23, 10.8f, z), 1.2f, 0.055f);
			Box(Transform3D.Identity, new Vector3(0, 9.5f, z), new Vector3(2.8f, 0.20f, 0.65f), _ink);
		}
		Prop("service_cabinet", new Vector3(-23.72f, 0.15f, 10), Mathf.Pi / 2f);
		Prop("service_cabinet", new Vector3(23.72f, 3.42f, -18), -Mathf.Pi / 2f);
		Prop("pallet_stack", new Vector3(22.1f, 0.06f, 14), Mathf.Pi / 2f, 0.8f);
		// Paint flat floor markings, so encounter space and collision stay unchanged.
		foreach (float x in new[] { -8.5f, 8.5f })
			Box(Transform3D.Identity, new Vector3(x, 0.012f, 2), new Vector3(0.09f, 0.01f, 35), _ochre);
		for (int i = 0; i < 5; i++)
			Box(Transform3D.Identity, new Vector3(-3.4f + i * 1.7f, 0.014f, 20.3f), new Vector3(0.8f, 0.01f, 2.2f), _paper);
	}

	private void BuildAnchor()
	{
		Sign("IRON ANCHOR", new Vector3(-15, 4.7f, 30.22f), Mathf.Pi, 5.2f, new Color("854532"));
		Sign("DOCK 03", new Vector3(15, 4.6f, -31.75f), 0, 3.5f, new Color("435c69"));
		foreach (float x in new[] { -29.74f, 29.74f })
		{
			float yaw = x < 0 ? Mathf.Pi / 2 : -Mathf.Pi / 2;
			Prop("service_cabinet", new Vector3(x, 0.1f, 10), yaw);
			for (int i = 0; i < 5; i++) WallLamp(new Vector3(x, 4.5f, -26 + i * 12), yaw, i == 2);
		}
		// Overhead cable and perimeter safety strips never cross the boss attack lane.
		Cable(new Vector3(-29.5f, 6.0f, 26), new Vector3(29.5f, 6.0f, 26), 1f, 0.06f);
		foreach (float x in new[] { -4.8f, 4.8f })
			Box(Transform3D.Identity, new Vector3(x, 0.015f, -45), new Vector3(0.12f, 0.01f, 20), _ochre);
		for (int i = 0; i < 8; i++)
		{
			float z = -33.5f - i * 3;
			Box(Transform3D.Identity, new Vector3(0, 0.014f, z), new Vector3(9.5f, 0.012f, 0.028f), _ink);
		}
	}

	private void BuildSeascape()
	{
		// Replace only the former sea surface; water kill volumes are untouched.
		Node3D oldSea = _level.GetNodeOrNull<Node3D>("Decor/Sea");
		if (oldSea != null) oldSea.Visible = false;
		foreach (string oldName in new[] { "Decor/Backdrop/ShipHull", "Decor/Backdrop/ShipBridge" })
		{
			Node3D old = _level.GetNodeOrNull<Node3D>(oldName);
			if (old != null) old.Visible = false;
		}
		Node3D backdrop = _level.GetNodeOrNull<Node3D>("Decor/Backdrop");
		if (backdrop != null)
			foreach (Node child in backdrop.GetChildren())
				if (child is Node3D old && child.Name.ToString().StartsWith("BgCrane")) old.Visible = false;
		BuildDistantQuay();
		var seaMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sea.gdshader") };
		var sea = new MeshInstance3D
		{
			Name = "LivingSea", Position = new Vector3(0, -4.18f, 0),
			Mesh = new PlaneMesh { Size = new Vector2(3000, 3000) }, MaterialOverride = seaMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(sea);
		// Distant silhouette layers frame the opening and remain outside every gameplay area.
		Prop("cargo_ship", new Vector3(-105, -4.15f, -185), -0.12f, 0.85f);
		Prop("cargo_ship", new Vector3(170, -4.15f, -340), 0.1f, 0.65f);
		Prop("harbor_lighthouse", new Vector3(145, -4.15f, -165), 0, 1.1f);
		MountainLayer(-800, 95, new Color("687888"), 37);
		MountainLayer(-650, 64, new Color("506779"), 19);
	}

	private void BuildDistantQuay()
	{
		// These silhouettes sit on a continuous quay rooted below the water line.
		Box(Transform3D.Identity, new Vector3(0, -4.2f, -202), new Vector3(198, 7, 42), new Color("394c5b"));
		Box(Transform3D.Identity, new Vector3(0, -0.65f, -181), new Vector3(198, 0.25f, 0.45f), new Color("899091"));
		foreach (float x in new[] { -65f, -4f, 61f })
		{
			Vector3 o = new(x, -0.5f, -202);
			Color steel = new("566576"), orange = new("a87142");
			foreach (float side in new[] { -1f, 1f })
			{
				Beam(o + new Vector3(side * 7, 0, 4), o + new Vector3(side * 3, 31, 0), 1.4f, steel);
				Beam(o + new Vector3(side * 7, 0, -4), o + new Vector3(side * 3, 31, 0), 1.4f, steel);
				Beam(o + new Vector3(-7, 0, side * 4), o + new Vector3(5, 19, side * 1.5f), 0.45f, steel);
			}
			Box(Transform3D.Identity, o + new Vector3(0, 31, 0), new Vector3(49, 1.3f, 3.2f), orange);
			Box(Transform3D.Identity, o + new Vector3(-13, 29, 0), new Vector3(6, 3.4f, 4), steel);
			Beam(o + new Vector3(-3, 31, 0), o + new Vector3(-3, 40, 0), 0.8f, steel);
			Beam(o + new Vector3(-3, 40, 0), o + new Vector3(23, 31, 0), 0.24f, steel);
			Beam(o + new Vector3(-3, 40, 0), o + new Vector3(-24, 31, 0), 0.24f, steel);
			Beam(o + new Vector3(17, 31, 0), o + new Vector3(17, 12, 0), 0.13f, steel);
			Box(Transform3D.Identity, o + new Vector3(17, 12, 0), new Vector3(4.8f, 0.5f, 2.4f), orange);
		}
	}

	private void MountainLayer(float z, float height, Color color, int seed)
	{
		const int count = 78;
		var vertices = new Vector3[count * 2];
		var indices = new int[(count - 1) * 6];
		for (int i = 0; i < count; i++)
		{
			float x = -1000 + i * 2000f / (count - 1) + Mathf.Sin(i * 3.73f + seed) * 7f;
			float center = x + seed * 4f;
			float peak = height * (0.14f + 0.38f * Mathf.Exp(-Mathf.Pow((center + 380f) / 170f, 2))
				+ 0.52f * Mathf.Exp(-Mathf.Pow((center - 210f) / 240f, 2))
				+ 0.20f * Mathf.Exp(-Mathf.Pow((center + 830f) / 200f, 2)));
			peak += height * 0.028f * (Mathf.Sin(x * 0.043f + seed) + 0.45f * Mathf.Sin(x * 0.119f));
			vertices[i * 2] = new Vector3(x, -10, z);
			vertices[i * 2 + 1] = new Vector3(x, peak, z);
			if (i == count - 1) continue;
			int a = i * 6, v = i * 2;
			indices[a] = v; indices[a + 1] = v + 1; indices[a + 2] = v + 2;
			indices[a + 3] = v + 1; indices[a + 4] = v + 3; indices[a + 5] = v + 2;
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		AddChild(new MeshInstance3D
		{
			Name = "DistantHeadland", Mesh = mesh,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	private void FlushBatches()
	{
		foreach (Batch batch in _batches.Values)
		{
			var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = batch.Mesh, InstanceCount = batch.Transforms.Count };
			for (int i = 0; i < batch.Transforms.Count; i++) multi.SetInstanceTransform(i, batch.Transforms[i]);
			var instance = new MultiMeshInstance3D
			{
				Name = "BatchedHardware", Multimesh = multi, MaterialOverride = batch.Material,
				CastShadow = batch.CastShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
				VisibilityRangeEnd = batch.Transforms[0].Origin.Z < -140f ? 750f : 105f, VisibilityRangeEndMargin = 10f,
			};
			AddChild(instance);
		}
		_batches.Clear();
	}

	private static int StableHash(string value)
	{
		uint hash = 2166136261;
		foreach (char c in value) hash = (hash ^ c) * 16777619;
		return (int)(hash & 0x7fffffff);
	}
}
