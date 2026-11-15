using Godot;

/// <summary>
/// 给模型实例下所有网格挂描边叠层（反向壳）。
/// 模型由多个分件网格组成（手臂 / 套筒 / 弹匣 / 四肢），在这里统一挂一次，
/// 场景里就不必为每个分件写一条 material_overlay 覆盖（§6.12 美漫描边）。
/// </summary>
[GlobalClass]
public partial class ModelOutline : Node
{
	[Export] public Material Overlay { get; set; }
	[Export] public bool UseComicMaterials { get; set; } = true;

	public override void _Ready()
	{
		Node target = GetChildCount() == 0 && GetParent() is Node3D ? GetParent() : this;
		if (Overlay != null)
			Apply(target);
		if (UseComicMaterials)
			ComicMaterials.Apply(target);
	}

	private void Apply(Node node)
	{
		if (node is MeshInstance3D selfMesh && selfMesh.MaterialOverlay == null)
			selfMesh.MaterialOverlay = Overlay;
		foreach (Node child in node.GetChildren())
		{
			if (child is MeshInstance3D mesh)
			{
				if (mesh.MaterialOverlay == null)
					mesh.MaterialOverlay = Overlay;
			}
			Apply(child);
		}
	}
}
