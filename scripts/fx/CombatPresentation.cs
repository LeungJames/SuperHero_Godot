using Godot;

/// <summary>Attaches the same visual shot language to existing actors and reinforcements.</summary>
public partial class CombatPresentation : Node
{
	public override void _Ready()
	{
		Visit(GetParent());
		GetTree().NodeAdded += OnAdded;
	}
	public override void _ExitTree() => GetTree().NodeAdded -= OnAdded;
	private void OnAdded(Node node)
	{
		if (node is OmniLight3D && node.Name == "MuzzleLight")
			Callable.From(() => { if (IsInstanceValid(node) && node.IsInsideTree()) Attach(node); }).CallDeferred();
	}
	private static void Attach(Node light)
	{
		if (!light.HasNode("InkFlash")) light.AddChild(new ComicMuzzleFlash { Name = "InkFlash" });
	}
	private static void Visit(Node node)
	{
		if (node is OmniLight3D && node.Name == "MuzzleLight") Attach(node);
		foreach (Node child in node.GetChildren()) Visit(child);
	}
}
