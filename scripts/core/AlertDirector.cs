using Godot;
using System.Collections.Generic;

/// <summary>
/// 警报系统：敌人持续处于 Alert / Combat 后会呼叫增援，从关卡里标记的刷新点分波抵达。
/// 只做调度与提示，敌人行为仍在 EnemyAgent（设计文档 §5.3）。
/// </summary>
public partial class AlertDirector : Node
{
	[Export] public AlertData Data { get; set; }

	private readonly List<Node3D> _points = new();
	private HudController _hud;
	private float _alertTime;
	private float _waveCooldown;
	private float _spawnTimer;
	private int _waves;
	private int _pending;
	private int _nextPoint;

	public override void _Ready()
	{
		SetProcess(false);

		if (GetTree().GetFirstNodeInGroup("reinforce_points") is Node3D root)
		{
			foreach (Node child in root.GetChildren())
				if (child is Node3D point)
					_points.Add(point);
		}

		_hud = GetNodeOrNull<HudController>("../../UI/Hud");
		SetProcess(Data != null && Data.ReinforceScene != null && _points.Count > 0);

		if (!IsProcessing() && Data != null && Data.ReinforceScene != null && _points.Count == 0)
			GD.PushWarning("AlertDirector: 关卡里没有 reinforce_points 刷新点，增援已禁用。");
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		_waveCooldown = Mathf.Max(0f, _waveCooldown - dt);

		if (IsAlerted())
			_alertTime += dt;
		else
			_alertTime = Mathf.Max(0f, _alertTime - dt * 2f);

		if (_pending > 0)
		{
			_spawnTimer -= dt;
			if (_spawnTimer <= 0f)
				SpawnOne();
		}

		if (_alertTime >= Data.AlertHoldTime && _waves < Data.MaxWaves && _waveCooldown <= 0f && _pending == 0)
			CallWave();
	}

	/// <summary>只要还有敌人在警戒或交火，就认为警报未解除。</summary>
	private bool IsAlerted()
	{
		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyHealth health || health.IsDead)
				continue;
			if (health.GetParent() is not EnemyAgent agent)
				continue;

			if (agent.State == EnemyState.Alert || agent.State == EnemyState.Combat)
				return true;
		}

		return false;
	}

	private void CallWave()
	{
		_waves++;
		_pending = Mathf.Max(1, Data.WaveSize);
		_spawnTimer = 0f;
		_waveCooldown = Data.WaveInterval;
		_alertTime = 0f;
		_hud?.ShowBanner(Data.CallMessage);
	}

	private void SpawnOne()
	{
		_pending--;

		if (_points.Count == 0)
			return;

		Node3D point = _points[_nextPoint % _points.Count];
		_nextPoint++;

		Node spawn = Data.ReinforceScene.Instantiate();
		GetParent().GetParent().AddChild(spawn); // Managers -> MainScene
		if (spawn is Node3D node)
			node.GlobalPosition = point.GlobalPosition;

		_hud?.ShowMessage(Data.ArriveMessage, new Color(0.89f, 0.34f, 0.18f));
	}
}
