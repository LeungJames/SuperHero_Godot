using Godot;
using System.Collections.Generic;

/// <summary>Shared paint response for imported characters and equipment.</summary>
public static class ComicMaterials
{
	private static readonly Dictionary<Color, ShaderMaterial> Paint = new();
	private static Shader _shader;

	public static ShaderMaterial ForColor(Color color)
	{
		if (Paint.TryGetValue(color, out var cached)) return cached;
		_shader ??= GD.Load<Shader>("res://shaders/toon.gdshader");
		var material = new ShaderMaterial { Shader = _shader };
		material.SetShaderParameter("albedo_color", color);
		Paint[color] = material;
		return material;
	}

	public static void Apply(Node root)
	{
		if (root is MeshInstance3D mesh && mesh.Mesh != null)
		{
			if (mesh.MaterialOverride is StandardMaterial3D whole && !whole.EmissionEnabled)
				mesh.MaterialOverride = ForColor(whole.AlbedoColor);
			else if (mesh.MaterialOverride == null)
				for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
					if (mesh.GetActiveMaterial(i) is StandardMaterial3D source &&
						!source.EmissionEnabled && source.Transparency == BaseMaterial3D.TransparencyEnum.Disabled)
						mesh.SetSurfaceOverrideMaterial(i, ForColor(source.AlbedoColor));
		}
		foreach (Node child in root.GetChildren()) Apply(child);
	}
}
