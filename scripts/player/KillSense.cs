using Godot;
using System.Collections.Generic;

/// <summary>
/// A8 杀气感知：击杀后 5s 内，8m 内的敌人轮廓高亮（设计文档 §5.2.3）。
/// 只替换敌人的描边叠层材质，不复制模型、不改碰撞；港口 / 工厂同样可用。
/// </summary>
public partial class KillSense : Node
{
	private static ShaderMaterial _highlight;

	private PlayerController _player;
	private EnemyRegistry _registry;
	private readonly Dictionary<MeshInstance3D, Material> _original = new();
	private float _timer;
	private float _refresh;

	public void Bind(PlayerController player, EnemyRegistry registry)
	{
		_player = player;
		_registry = registry;
		if (_registry != null)
			_registry.EnemyDied += OnEnemyDied;
	}

	public override void _ExitTree()
	{
		if (_registry != null)
			_registry.EnemyDied -= OnEnemyDied;
		RestoreAll();
	}

	public override void _Process(double delta)
	{
		if (_timer <= 0f)
		{
			if (_original.Count > 0)
				RestoreAll();
			return;
		}

		_timer -= (float)delta;
		_refresh -= (float)delta;
		if (_refresh > 0f)
			return;

		_refresh = 0.2f;
		Refresh();
	}

	private void OnEnemyDied(EnemyHealth enemy)
	{
		_timer = TalentEffects.SenseDuration;
		_refresh = 0f;
	}

	private void Refresh()
	{
		if (_player == null)
			return;

		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyAgent agent || !GodotObject.IsInstanceValid(agent))
				continue;

			bool near = !agent.IsDead &&
				agent.GlobalPosition.DistanceTo(_player.GlobalPosition) <= TalentEffects.SenseRadius;
			SetHighlighted(agent, near);
		}
	}

	private void SetHighlighted(EnemyAgent agent, bool highlighted)
	{
		Node3D model = agent.GetNodeOrNull<Node3D>("Model");
		if (model == null)
			return;

		foreach (MeshInstance3D mesh in Meshes(model))
		{
			if (highlighted)
			{
				if (_original.ContainsKey(mesh))
					continue;
				_original[mesh] = mesh.MaterialOverlay;
				mesh.MaterialOverlay = HighlightMaterial();
			}
			else if (_original.Remove(mesh, out Material original))
			{
				mesh.MaterialOverlay = original;
			}
		}
	}

	private void RestoreAll()
	{
		foreach (KeyValuePair<MeshInstance3D, Material> pair in _original)
		{
			if (GodotObject.IsInstanceValid(pair.Key))
				pair.Key.MaterialOverlay = pair.Value;
		}

		_original.Clear();
	}

	private static IEnumerable<MeshInstance3D> Meshes(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is MeshInstance3D mesh)
				yield return mesh;
			foreach (MeshInstance3D nested in Meshes(child))
				yield return nested;
		}
	}

	private static ShaderMaterial HighlightMaterial()
	{
		if (_highlight != null)
			return _highlight;

		_highlight = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/outline.gdshader"),
		};
		_highlight.SetShaderParameter("ink_color", new Color(0.95f, 0.24f, 0.14f, 1f));
		_highlight.SetShaderParameter("width", 0.06f);
		return _highlight;
	}
}
