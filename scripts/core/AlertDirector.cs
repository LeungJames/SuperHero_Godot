using Godot;
using System.Collections.Generic;

/// <summary>
/// 警报系统：敌人持续处于 Alert / Combat 后会呼叫增援，从关卡里标记的刷新点分波抵达。
/// 只做调度与提示，敌人行为仍在 EnemyAgent（设计文档 §5.3）。
/// </summary>
public partial class AlertDirector : Node
{
	[Export] public AlertData Data { get; set; }

	/// <summary>寻路终点允许偏离玩家的水平距离；超过说明刷新点与玩家不在同一张连通网格。</summary>
	private const float ReachTolerance = 2.5f;
	/// <summary>寻路终点允许偏离玩家的垂直距离；用于区分"玩家在正上方的高层"这种假连通。</summary>
	private const float ReachHeightTolerance = 2.5f;
	/// <summary>没有可用刷新点时的重试间隔（秒）。玩家还在下落/换层时不要刷出够不着的敌人。</summary>
	private const float RetryDelay = 0.5f;

	private readonly List<Node3D> _points = new();
	private readonly Godot.Collections.Array<Rid> _rayExclude = new();
	private HudController _hud;
	private PlayerController _player;
	private float _alertTime;
	private float _waveCooldown;
	private float _spawnTimer;
	private int _waves;
	private int _pending;
	private bool _warnedNoReachable;
	private Node3D _lastSpawnPoint;

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
		if (!TryPickSpawnPosition(out Vector3 position))
		{
			// 玩家可能在跳跃段/半空，或所有标记都够不着：等下一次重试，绝不刷在不可达的高处
			_spawnTimer = RetryDelay;
			if (!_warnedNoReachable)
			{
				_warnedNoReachable = true;
				GD.Print("AlertDirector: 当前没有与玩家同层且可达的刷新点，增援等待玩家落到可达层。");
			}

			return;
		}

		_warnedNoReachable = false;
		_pending--;

		Node spawn = Data.ReinforceScene.Instantiate();
		GetParent().GetParent().AddChild(spawn); // Managers -> MainScene
		if (spawn is Node3D node)
			node.GlobalPosition = position;

		// 增援一落地就朝玩家方向压上，避免刷在被遮挡处后原地不动
		if (spawn is EnemyAgent agent && _player != null)
			agent.JoinAlert(_player.GlobalPosition);

		_hud?.ShowMessage(Data.ArriveMessage, new Color(0.89f, 0.34f, 0.18f));
	}

	/// <summary>
	/// 从关卡标记里挑一个玩家当前能走到、且不会贴脸/正对视线刷出的点。
	/// 过滤顺序：导航网格吸附 → 高度层 → 最小距离 → 寻路可达；最后优先没被玩家直接看到的点。
	/// </summary>
	private bool TryPickSpawnPosition(out Vector3 position)
	{
		position = Vector3.Zero;

		if (_points.Count == 0)
			return false;

		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null)
			return false;

		World3D world = GetViewport()?.FindWorld3D();
		if (world == null)
			return false;

		Rid map = world.NavigationMap;
		Vector3 playerPosition = _player.GlobalPosition;

		Node3D best = null;
		float bestScore = float.MaxValue;

		foreach (Node3D point in _points)
		{
			Vector3 snapped = NavigationServer3D.MapGetClosestPoint(map, point.GlobalPosition);
			if (IsNavMiss(snapped, point.GlobalPosition))
				continue;

			float heightDelta = Mathf.Abs(snapped.Y - playerPosition.Y);
			if (heightDelta > Data.HeightTolerance)
				continue;

			float distance = Flat(snapped - playerPosition).Length();
			if (distance < Data.MinSpawnDistance)
				continue;

			if (!CanReach(map, snapped, playerPosition))
				continue;

			// 被遮挡的点优先；同一波里避开刚用过的点；其余取最近的（已保证 ≥ MinSpawnDistance）
			float score = distance
				+ (IsVisible(playerPosition, snapped) ? 1000f : 0f)
				+ (point == _lastSpawnPoint ? 8f : 0f);
			if (score < bestScore)
			{
				bestScore = score;
				best = point;
				position = snapped + Vector3.Up * 0.05f;
			}
		}

		if (best != null)
			_lastSpawnPoint = best;

		return best != null;
	}

	/// <summary>导航网格未烘焙/点不在网格上时，MapGetClosestPoint 会返回原点。</summary>
	private static bool IsNavMiss(Vector3 snapped, Vector3 source)
	{
		return snapped == Vector3.Zero && source.LengthSquared() > 0.01f;
	}

	/// <summary>刷新点能不能自己走到玩家身边：路径终点必须真的落在玩家身上，而不是落在玩家正下方的另一层。</summary>
	private static bool CanReach(Rid map, Vector3 from, Vector3 to)
	{
		Vector3[] path = NavigationServer3D.MapGetPath(map, from, to, true);
		if (path.Length < 2)
			return false;

		Vector3 end = path[path.Length - 1];
		return Flat(end - to).Length() <= ReachTolerance
			&& Mathf.Abs(end.Y - to.Y) <= ReachHeightTolerance;
	}

	/// <summary>玩家眼位到刷新点是否无遮挡（无遮挡 = 会看见增援凭空出现，尽量避开）。</summary>
	private bool IsVisible(Vector3 playerPosition, Vector3 spawnPosition)
	{
		_rayExclude.Clear();
		_rayExclude.Add(_player.GetRid());

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
			playerPosition + Vector3.Up * 1.5f,
			spawnPosition + Vector3.Up * 1f,
		1u);
		query.Exclude = _rayExclude;

		return GetViewport().FindWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
	}

	private static Vector3 Flat(Vector3 value) => new(value.X, 0f, value.Z);
}
