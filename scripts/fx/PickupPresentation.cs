using Godot;

/// <summary>Modeled story artifacts and small diegetic ammunition tins.</summary>
public partial class PickupPresentation : Node3D
{
	public override void _Ready()
	{
		Node parent = GetParent();
		parent.GetNodeOrNull<Node3D>("Mesh")?.Hide();
		if (parent is CollectiblePickup collectible)
		{
			string path = $"res://art/models/collectibles/{collectible.Data?.Id}.glb";
			if (ResourceLoader.Exists(path))
			{
				Node3D model = GD.Load<PackedScene>(path).Instantiate<Node3D>();
				model.Name = "Artifact";
				ComicMaterials.Apply(model);
				var ink = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/outline.gdshader") };
				ink.SetShaderParameter("pixel_width", 0.85f);
				model.AddChild(new ModelOutline { Overlay = ink });
				AddChild(model);
			}
			else GD.PushError($"Missing collectible model: {path}");
			var glow = parent.GetNodeOrNull<OmniLight3D>("Glow");
			if (glow != null) { glow.LightEnergy = 0.18f; glow.OmniRange = 1.0f; }
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
