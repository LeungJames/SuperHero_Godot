using Godot;

/// <summary>Small diegetic supply tins and evidence folders share the printed UI art.</summary>
public partial class PickupPresentation : Node3D
{
	public override void _Ready()
	{
		Node parent = GetParent();
		parent.GetNodeOrNull<Node3D>("Mesh")?.Hide();
		if (parent is CollectiblePickup collectible)
		{
			Box(Vector3.Zero, new Vector3(0.28f, 0.04f, 0.36f), "c6a168");
			Box(new Vector3(-0.13f, 0.015f, 0), new Vector3(0.035f, 0.045f, 0.37f), "563d36");
			string path = $"res://art/ui/evidence/{collectible.Data?.Id}.svg";
			if (ResourceLoader.Exists(path))
				AddChild(new MeshInstance3D { Position = new Vector3(0.007f, 0.024f, 0),
					Mesh = new PlaneMesh { Size = new Vector2(0.23f, 0.30f) },
					MaterialOverride = new StandardMaterial3D { AlbedoTexture = GD.Load<Texture2D>(path), Roughness = 1f },
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
			var glow = parent.GetNodeOrNull<OmniLight3D>("Glow");
			if (glow != null) { glow.LightEnergy = 0.25f; glow.OmniRange = 1.2f; }
			var label = parent.GetNodeOrNull<Label3D>("Label");
			if (label != null) { label.Font = ComicTheme.Bold; label.Modulate = ComicTheme.Paper; label.OutlineModulate = ComicTheme.Ink; }
		}
		else
		{
			Box(Vector3.Zero, new Vector3(0.30f, 0.16f, 0.20f), "59604c");
			Box(new Vector3(0, 0.086f, 0), new Vector3(0.32f, 0.025f, 0.22f), "272e30");
			foreach (float x in new[] { -0.10f, 0.10f })
				Box(new Vector3(x, 0.015f, 0.106f), new Vector3(0.035f, 0.10f, 0.015f), "b6b3a2");
			Box(new Vector3(0, 0.13f, 0), new Vector3(0.13f, 0.022f, 0.025f), "272e30");
			foreach (float x in new[] { -0.055f, 0.055f })
				Box(new Vector3(x, 0.11f, 0), new Vector3(0.02f, 0.045f, 0.025f), "272e30");
			Box(new Vector3(0, 0, 0.107f), new Vector3(0.10f, 0.06f, 0.014f), "efc164");
		}
	}
	private void Box(Vector3 position, Vector3 size, string color) => AddChild(new MeshInstance3D {
		Position = position, Mesh = new BoxMesh { Size = size }, MaterialOverride = ComicMaterials.ForColor(new Color(color)) });
}
