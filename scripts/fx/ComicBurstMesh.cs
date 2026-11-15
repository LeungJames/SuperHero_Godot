using Godot;
using System.Collections.Generic;

/// <summary>Small ink-edged meshes shared by muzzle flashes and impact accents.</summary>
public static class ComicBurstMesh
{
	private static ArrayMesh _star;
	private static StandardMaterial3D _billboard;
	public static ArrayMesh Star => _star ??= Build();
	public static StandardMaterial3D Billboard => _billboard ??= new StandardMaterial3D
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		VertexColorUseAsAlbedo = true,
		VertexColorIsSrgb = true,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
		BillboardKeepScale = true,
	};

	private static ArrayMesh Build()
	{
		var vertices = new List<Vector3>();
		var colors = new List<Color>();
		Color[] inks = { new("382438"), new("f5a52d"), new("fff6d5") };
		float[] sizes = { 1f, 0.86f, 0.36f };
		for (int layer = 0; layer < 3; layer++)
			for (int i = 0; i < 16; i++)
			{
				float a = i * Mathf.Tau / 16f, b = (i + 1) * Mathf.Tau / 16f;
				float r0 = (i % 2 == 0 ? 0.85f + 0.15f * Mathf.Sin(i * 5f) : 0.26f) * sizes[layer];
				float r1 = ((i + 1) % 2 == 0 ? 0.85f + 0.15f * Mathf.Sin((i + 1) * 5f) : 0.26f) * sizes[layer];
				vertices.Add(new Vector3(0, 0, layer * 0.003f));
				vertices.Add(new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, layer * 0.003f));
				vertices.Add(new Vector3(Mathf.Cos(b) * r1, Mathf.Sin(b) * r1, layer * 0.003f));
				for (int j = 0; j < 3; j++) colors.Add(inks[layer]);
			}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}
}
