using Godot;
using System.Collections.Generic;

public enum StealthAlertLevel
{
	None = 0,
	L0 = 1,
	L1 = 2,
	L2 = 3,
}

/// <summary>
/// 潜行总控（2.0 设计 §3.3–§3.7）：三级警戒迁移、全关搜索、尸体注册、队长死亡、
/// 脱战恢复、噪音分级与可读反馈。只做调度与判定，敌人行为仍在 EnemyAgent。
///
/// 硬约束：不修改玩家生命、不提供回血；不改武器解锁；不改击杀与胜利流程。
/// </summary>
public partial class StealthDirector : Node
{
	[Export] public string ChapterId { get; set; } = "port";
	[Export] public StealthConfig Config { get; set; }
	[Export] public StealthRoute Route { get; set; }
	[Export] public StealthPosts Posts { get; set; }
	/// <summary>调试路线层默认是否打开；游戏内 F4 随时切换。</summary>
	[Export] public bool DebugRouteVisible { get; set; }

	public StealthAlertLevel Level { get; private set; } = StealthAlertLevel.None;
	public float LevelTimeLeft { get; private set; }
	public Vector3 LastKnownPosition { get; private set; }
	public Vector3 SuspiciousPosition { get; private set; }
	public float PlayerUnseenStillTime => _unseenStillTime;
	public IReadOnlyList<CorpseMarker> Corpses => _corpses;
	public bool RouteOverlayOn => _routeOverlay != null && _routeOverlay.Visible;

	private static readonly Color Level0Color = new(0.95f, 0.83f, 0.28f);
	private static readonly Color Level1Color = new(0.95f, 0.55f, 0.22f);
	private static readonly Color Level2Color = new(0.88f, 0.22f, 0.18f);
	private static readonly Color RadioColor = new(0.92f, 0.55f, 0.25f);
	private static readonly Color IconSpotted = new(0.92f, 0.26f, 0.20f);
	private static readonly Color IconPuzzled = new(0.95f, 0.82f, 0.32f);

	private PlayerController _player;
	private PlayerHealth _playerHealth;
	private HudController _hud;
	private AlertDirector _alertDirector;
	private AudioDirector _audio;
	private StealthHud _hudOverlay;
	private StealthRouteOverlay _routeOverlay;

	private readonly List<EnemyAgent> _alive = new();
	private readonly List<CorpseMarker> _corpses = new();
	private readonly Dictionary<EnemyAgent, EnemyState> _lastStates = new();
	private readonly List<EnemyAgent> _captains = new();
	private readonly HashSet<EnemyAgent> _captainReported = new();
	private readonly Dictionary<string, EnemyAgent> _postAgents = new();
	private readonly Dictionary<string, StealthPostRole> _postRoles = new();

	private bool _wasSeen;
	private float _unseenStillTime;
	private float _buildRetry;

	// 藏尸
	private CorpseMarker _hideTarget;
	private float _hideTimer;
	private bool _hidePromptShown;

	public override void _Ready()
	{
		AddToGroup("stealth_director");

		if (Config == null)
			Config = new StealthConfig();
		StealthSystem.Set(Config);

		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player != null)
		{
			_playerHealth = _player.GetNodeOrNull<PlayerHealth>("Health");
			LastKnownPosition = _player.GlobalPosition;
		}

		_hud = GetTree().GetFirstNodeInGroup("hud") as HudController;
		_alertDirector = GetTree().CurrentScene?.FindChild("AlertDirector", true, false) as AlertDirector;
		if (_alertDirector != null)
			_alertDirector.AutoWaves = false;

		BuildPostTable();
		BuildOverlays();

		NoiseEmitter.Emitted += OnNoiseEmitted;

		if (Route != null)
		{
			GD.Print($"[Stealth] chapter={ChapterId} route={Route.Waypoints.Length} 点 " +
				$"entry={Fmt(Route.EntryPoint)} merge={Fmt(Route.MergePoint)}");
			if (!string.IsNullOrEmpty(Route.Note))
				GD.Print($"[Stealth] route_note={Route.Note}");
		}
		else
		{
			GD.PushWarning($"StealthDirector: 关卡 {ChapterId} 没有配置潜行支路。");
		}
	}

	public override void _ExitTree()
	{
		NoiseEmitter.Emitted -= OnNoiseEmitted;

		if (_player != null && GodotObject.IsInstanceValid(_player))
			_player.ExternalSpeedCap = float.MaxValue;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		EnsureRefs();
		CollectEnemies();
		ResolvePostsIfNeeded(dt);

		if (_player == null || _playerHealth == null || _playerHealth.IsDead)
			return;

		UpdateLevelTimer(dt);
		UpdateSeen(dt);
		PollIconTransitions();
		PollCaptains();
		UpdateCorpses(dt);
		UpdateHideInteraction(dt);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F4 } && _routeOverlay != null)
			_routeOverlay.Visible = !_routeOverlay.Visible;
	}

	// ---------------------------------------------------------------- 对外事件

	/// <summary>尸体被发现：提升到 L0（已在 L1/L2 时只记录位置）。</summary>
	public void NotifyCorpseFound(Vector3 position)
	{
		if (Config == null)
			return;

		_hud?.ShowMessage(Config.CorpseFoundMessage, Level1Color);
		PlayAlarm(position);

		if (Level >= StealthAlertLevel.L1)
		{
			LastKnownPosition = position;
			return;
		}

		SetLevel(StealthAlertLevel.L0, Config.L0Duration, false);
		SuspiciousPosition = position;
		SearchNearestSquad(position);
	}

	/// <summary>队长死亡：短暂提升全队警戒（§3.3）。</summary>
	public void NotifyCaptainDied(Vector3 position)
	{
		if (Config == null)
			return;

		_hud?.ShowMessage(Config.CaptainDeathMessage, Level1Color);
		PlayAlarm(position);

		if (Level < StealthAlertLevel.L1)
			SetLevel(StealthAlertLevel.L1, Config.L1Duration, false);

		BroadcastSearch(position);
	}

	/// <summary>处决 / 暗杀命中：留下尸体（环境击杀与枪杀不调用这里）。</summary>
	public void NotifyExecution(EnemyHealth target, bool unaware)
	{
		if (target == null || Config == null)
			return;

		EnemyAgent agent = target.GetParent() as EnemyAgent;
		Vector3 position = agent != null ? agent.GlobalPosition : _player?.GlobalPosition ?? Vector3.Zero;
		SpawnCorpse(position, agent);
	}

	// ---------------------------------------------------------------- 警戒等级

	private void UpdateLevelTimer(float dt)
	{
		if (Level == StealthAlertLevel.None)
			return;

		LevelTimeLeft = Mathf.Max(0f, LevelTimeLeft - dt);
		if (LevelTimeLeft > 0f)
			return;

		switch (Level)
		{
			case StealthAlertLevel.L0:
				SetLevel(StealthAlertLevel.None, 0f, false);
				break;
			case StealthAlertLevel.L1:
				// L1 到期回 L0 而不是直接平静，留一小段收尾搜索
				SetLevel(StealthAlertLevel.L0, Config.L0Duration, false);
				break;
			case StealthAlertLevel.L2:
				// §3.4：L2 结束后回到 L0，不永久锁定
				SetLevel(StealthAlertLevel.L0, Config.L0Duration, false);
				break;
		}
	}

	private void UpdateSeen(float dt)
	{
		bool seen = false;
		foreach (EnemyAgent agent in _alive)
		{
			if (agent == null || !GodotObject.IsInstanceValid(agent) || agent.IsDead)
				continue;
			if (agent.HasSight)
			{
				seen = true;
				break;
			}
		}

		if (seen)
		{
			if (Level < StealthAlertLevel.L1)
				OnPlayerRevealed(_player.GlobalPosition, Level == StealthAlertLevel.None);
			else if (!_wasSeen && Level == StealthAlertLevel.L1)
				OnPlayerRevealed(_player.GlobalPosition, false);
			else if (Level == StealthAlertLevel.L2)
				LevelTimeLeft = Config.L2Duration;
		}

		_wasSeen = seen;

		// 脱战：完全脱离视野并静止 10s，降低一级（§3.4）
		Vector3 horizontal = new(_player.Velocity.X, 0f, _player.Velocity.Z);
		bool still = horizontal.Length() <= Config.DeescalateSpeedThreshold;
		if (seen || !still)
		{
			_unseenStillTime = 0f;
			return;
		}

		_unseenStillTime += dt;
		if (_unseenStillTime < Config.DeescalateStationaryTime)
			return;

		_unseenStillTime = 0f;
		DropOneLevel();
	}

	private void OnPlayerRevealed(Vector3 position, bool announce)
	{
		LastKnownPosition = position;

		foreach (EnemyAgent agent in _alive)
		{
			if (agent != null && GodotObject.IsInstanceValid(agent) && agent.HasSight)
				_hudOverlay?.ShowIcon(agent.GlobalPosition, "!", IconSpotted);
		}

		if (Level == StealthAlertLevel.L1)
		{
			// L1 期间再次暴露 → L2 + 增援
			SetLevel(StealthAlertLevel.L2, Config.L2Duration, true);
			BroadcastSearch(position);
			RequestWave(L2WaveSize(), Config.L2Message);
			PlayAlarm(position);
			return;
		}

		if (Level < StealthAlertLevel.L1)
		{
			SetLevel(StealthAlertLevel.L1, Config.L1Duration, announce);
			BroadcastSearch(position);
			RequestWave(Mathf.Max(1, Config.L1ReinforceCount), Config.L1Message);
			PlayAlarm(position);
		}
	}

	private void DropOneLevel()
	{
		switch (Level)
		{
			case StealthAlertLevel.L2:
				SetLevel(StealthAlertLevel.L1, Config.L1Duration, false);
				break;
			case StealthAlertLevel.L1:
				SetLevel(StealthAlertLevel.L0, Config.L0Duration, false);
				break;
			case StealthAlertLevel.L0:
				SetLevel(StealthAlertLevel.None, 0f, false);
				break;
		}
	}

	private void SetLevel(StealthAlertLevel level, float duration, bool announce)
	{
		Level = level;
		LevelTimeLeft = Mathf.Max(0f, duration);
		_hudOverlay?.SetLevel(level);

		if (!announce)
			return;

		switch (level)
		{
			case StealthAlertLevel.L0:
				_hud?.ShowMessage(Config.L0Message, Level0Color);
				break;
			case StealthAlertLevel.L1:
				_hud?.ShowMessage(Config.L1Message, Level1Color);
				break;
			case StealthAlertLevel.L2:
				_hud?.ShowMessage(Config.L2Message, Level2Color);
				break;
		}
	}

	private void EnterSuspicion(Vector3 position, string message)
	{
		SuspiciousPosition = position;

		if (Level == StealthAlertLevel.L0)
		{
			LevelTimeLeft = Config.L0Duration;
		}
		else if (Level < StealthAlertLevel.L0)
		{
			SetLevel(StealthAlertLevel.L0, Config.L0Duration, false);
			_hud?.ShowMessage(message, Level0Color);
		}
		else
		{
			return;
		}

		SearchNearestSquad(position);
	}

	private void SearchNearestSquad(Vector3 position)
	{
		SquadDirector director = GetTree().GetFirstNodeInGroup("squad_director") as SquadDirector;
		if (director == null)
			return;

		SquadRuntime best = null;
		float bestDistance = float.MaxValue;

		foreach (SquadRuntime squad in director.Squads)
		{
			foreach (EnemyAgent member in squad.Alive)
			{
				float distance = FlatDistance(member.GlobalPosition, position);
				if (distance >= bestDistance)
					continue;
				bestDistance = distance;
				best = squad;
			}
		}

		if (best == null || bestDistance > Config.SuspiciousSearchRadius)
			return;

		foreach (EnemyAgent member in best.Alive)
			member.JoinAlert(position);
	}

	private void BroadcastSearch(Vector3 position)
	{
		foreach (EnemyAgent agent in _alive)
		{
			if (agent == null || !GodotObject.IsInstanceValid(agent) || agent.IsDead)
				continue;
			agent.JoinAlert(position);
		}
	}

	// ---------------------------------------------------------------- 噪音 / 提示

	private void OnNoiseEmitted(Vector3 position, NoiseKind kind)
	{
		if (Config == null || _playerHealth == null || _playerHealth.IsDead)
			return;
		if (Level >= StealthAlertLevel.L1)
			return;

		float radius = StealthSystem.NoiseRadius(kind);
		if (radius <= 0f)
			return;

		bool heard = false;
		foreach (EnemyAgent agent in _alive)
		{
			if (agent == null || !GodotObject.IsInstanceValid(agent) || agent.IsDead)
				continue;
			if (agent.State == EnemyState.Combat)
				continue;
			if (agent.GlobalPosition.DistanceTo(position) > radius)
				continue;
			heard = true;
			break;
		}

		if (heard)
			EnterSuspicion(position, Config.L0Message);
	}

	private void PollIconTransitions()
	{
		foreach (EnemyAgent agent in _alive)
		{
			if (agent == null || !GodotObject.IsInstanceValid(agent) || agent.IsDead)
				continue;

			if (!_lastStates.TryGetValue(agent, out EnemyState previous))
			{
				_lastStates[agent] = agent.State;
				continue;
			}

			if (previous != EnemyState.Combat && agent.State == EnemyState.Combat && agent.HasSight)
				_hudOverlay?.ShowIcon(agent.GlobalPosition, "!", IconSpotted);
			else if (previous == EnemyState.Patrol && agent.State == EnemyState.Alert && !agent.HasSight)
				_hudOverlay?.ShowIcon(agent.GlobalPosition, "?", IconPuzzled);

			_lastStates[agent] = agent.State;
		}
	}

	// ---------------------------------------------------------------- 尸体

	private void SpawnCorpse(Vector3 position, EnemyAgent source)
	{
		Node root = GetTree().CurrentScene;
		if (root == null)
			return;

		var corpse = new CorpseMarker { Name = $"Corpse_{_corpses.Count}" };
		root.AddChild(corpse);
		corpse.Setup(position, source, Config);
		_corpses.Add(corpse);
	}

	private void UpdateCorpses(float dt)
	{
		for (int i = _corpses.Count - 1; i >= 0; i--)
		{
			CorpseMarker corpse = _corpses[i];
			if (corpse == null || !GodotObject.IsInstanceValid(corpse))
			{
				_corpses.RemoveAt(i);
				continue;
			}

			if (corpse.TickNotice(dt, _alive))
				NotifyCorpseFound(corpse.GlobalPosition);
		}
	}

	private CorpseMarker FindNearestCorpse()
	{
		if (_player == null)
			return null;

		CorpseMarker best = null;
		float bestDistance = Config.CorpseInteractRadius;

		foreach (CorpseMarker corpse in _corpses)
		{
			if (corpse == null || !GodotObject.IsInstanceValid(corpse) || corpse.IsHidden)
				continue;

			float distance = FlatDistance(_player.GlobalPosition, corpse.GlobalPosition);
			if (distance >= bestDistance)
				continue;

			bestDistance = distance;
			best = corpse;
		}

		return best;
	}

	// ---------------------------------------------------------------- 藏尸

	private void UpdateHideInteraction(float dt)
	{
		if (_player == null || _playerHealth == null || _playerHealth.IsDead)
			return;

		if (_hideTarget != null)
		{
			if (!GodotObject.IsInstanceValid(_hideTarget) || _hideTarget.IsHidden)
			{
				CancelHide();
				return;
			}

			if (FlatDistance(_player.GlobalPosition, _hideTarget.GlobalPosition) > Config.CorpseInteractRadius + 2f)
			{
				CancelHide();
				_hud?.ShowMessage("离尸体太远，藏尸中断", Level1Color);
				return;
			}

			_hideTimer -= dt;
			if (_hideTimer <= 0f)
			{
				_hideTarget.HideCorpse();
				CancelHide();
				_hud?.ShowMessage(Config.HideDoneMessage, new Color(0.62f, 0.82f, 0.55f));
			}

			return;
		}

		CorpseMarker nearest = FindNearestCorpse();
		if (nearest == null)
		{
			ClearHidePrompt();
			return;
		}

		_hud?.SetPrompt(Config.HidePrompt);
		_hidePromptShown = true;

		if (!Input.IsActionJustPressed("execution"))
			return;

		_hideTarget = nearest;
		_hideTimer = Config.CorpseHideTime;
		_player.ExternalSpeedCap = Config.CorpseDragSpeed;
		_hud?.SetPrompt("藏尸中…");
	}

	private void CancelHide()
	{
		_hideTarget = null;
		_hideTimer = 0f;
		if (_player != null && GodotObject.IsInstanceValid(_player))
			_player.ExternalSpeedCap = float.MaxValue;
		ClearHidePrompt();
	}

	private void ClearHidePrompt()
	{
		if (!_hidePromptShown)
			return;

		_hidePromptShown = false;
		_hud?.SetPrompt(string.Empty);
	}

	// ---------------------------------------------------------------- 队长 / 参考

	private void PollCaptains()
	{
		foreach (EnemyAgent captain in _captains)
		{
			if (captain == null || !GodotObject.IsInstanceValid(captain))
				continue;
			if (!captain.IsDead || !_captainReported.Add(captain))
				continue;

			NotifyCaptainDied(captain.GlobalPosition);
		}
	}

	private void BuildPostTable()
	{
		if (Posts == null)
			return;

		foreach (StealthPostData post in Posts.Posts)
		{
			if (post == null || string.IsNullOrEmpty(post.EnemyName))
				continue;

			_postRoles[post.EnemyName] = post.Role;
		}
	}

	private void ResolvePostsIfNeeded(float dt)
	{
		if (_postRoles.Count == 0 || _postAgents.Count >= _postRoles.Count)
			return;

		_buildRetry -= dt;
		if (_buildRetry > 0f)
			return;
		_buildRetry = 1f;

		Node root = GetTree().CurrentScene;
		if (root == null)
			return;

		foreach (KeyValuePair<string, StealthPostRole> pair in _postRoles)
		{
			if (_postAgents.ContainsKey(pair.Key))
				continue;

			if (root.FindChild(pair.Key, true, false) is not EnemyAgent agent)
				continue;

			_postAgents[pair.Key] = agent;
			if (pair.Value == StealthPostRole.Captain && !_captains.Contains(agent))
				_captains.Add(agent);
		}
	}

	// ---------------------------------------------------------------- 基础设施

	private void EnsureRefs()
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player))
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_playerHealth == null && _player != null)
			_playerHealth = _player.GetNodeOrNull<PlayerHealth>("Health");
		if (_hud == null || !GodotObject.IsInstanceValid(_hud))
			_hud = GetTree().GetFirstNodeInGroup("hud") as HudController;
	}

	private void CollectEnemies()
	{
		_alive.Clear();

		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyHealth health || health.IsDead)
				continue;
			if (health.GetParent() is not EnemyAgent agent)
				continue;
			_alive.Add(agent);
		}
	}

	private int L2WaveSize()
	{
		if (Config.L2WaveSize > 0)
			return Config.L2WaveSize;
		return Mathf.Max(1, _alertDirector?.Data?.WaveSize ?? 2);
	}

	private void RequestWave(int size, string message)
	{
		_alertDirector?.RequestWave(Mathf.Max(1, size), message);
	}

	private void PlayAlarm(Vector3 position)
	{
		_audio ??= GetTree().CurrentScene?.FindChild("AudioDirector", true, false) as AudioDirector;
		_audio?.PlayWorld(SfxKind.Alert, position, -7f, 0.92f);
	}

	private void BuildOverlays()
	{
		var hudLayer = new CanvasLayer { Name = "StealthHudLayer", Layer = 18 };
		AddChild(hudLayer);

		_hudOverlay = new StealthHud { Name = "StealthHud", Source = this };
		hudLayer.AddChild(_hudOverlay);
		_hudOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_hudOverlay.SetLevel(StealthAlertLevel.None);

		var routeLayer = new CanvasLayer { Name = "StealthRouteLayer", Layer = 19 };
		AddChild(routeLayer);

		_routeOverlay = new StealthRouteOverlay { Name = "StealthRouteOverlay", Source = this };
		routeLayer.AddChild(_routeOverlay);
		_routeOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_routeOverlay.Visible = DebugRouteVisible;
	}

	/// <summary>F4 开关路线 / 哨位可视化；探针也可直接调用。</summary>
	public void SetRouteOverlayVisible(bool visible)
	{
		if (_routeOverlay != null)
			_routeOverlay.Visible = visible;
	}

	/// <summary>探针用：直接驱动警戒等级，记录迁移耗时。</summary>
	public void DebugSetLevel(StealthAlertLevel level, float duration)
	{
		SetLevel(level, duration, false);
	}

	private static float FlatDistance(Vector3 a, Vector3 b) =>
		new Vector2(a.X - b.X, a.Z - b.Z).Length();

	private static string Fmt(Vector3 v) => $"({v.X:F1},{v.Y:F1},{v.Z:F1})";

	// ---------------------------------------------------------------- 路线调试层

	private sealed partial class StealthRouteOverlay : Control
	{
		public StealthDirector Source { get; set; }

		public override void _Ready()
		{
			MouseFilter = MouseFilterEnum.Ignore;
			SetAnchorsPreset(LayoutPreset.FullRect);
		}

		public override void _Process(double delta)
		{
			if (Visible)
				QueueRedraw();
		}

		public override void _Draw()
		{
			if (Source?.Route == null)
				return;

			Camera3D camera = GetViewport()?.GetCamera3D();
			if (camera == null)
				return;

			Font font = ThemeDB.FallbackFont;
			StealthRoute route = Source.Route;

			DrawRect(new Rect2(8f, 200f, 430f, 96f), new Color(0f, 0f, 0f, 0.42f), true);
			DrawString(font, new Vector2(16f, 220f),
				$"STEALTH {Source.ChapterId}  level={Source.Level}  t={Source.LevelTimeLeft:F1}  " +
				$"still={Source.PlayerUnseenStillTime:F1}",
				HorizontalAlignment.Left, -1f, 14, Colors.White);
			DrawString(font, new Vector2(16f, 240f),
				$"route pts={route.Waypoints.Length}  corpses={Source._corpses.Count}  posts={Source._postAgents.Count}",
				HorizontalAlignment.Left, -1f, 12, new Color(0.82f, 0.86f, 0.9f));
			DrawString(font, new Vector2(16f, 258f),
				Source.Level switch
				{
					StealthAlertLevel.L2 => "L2 全关搜捕",
					StealthAlertLevel.L1 => "L1 搜索",
					StealthAlertLevel.L0 => "L0 局部警戒",
					_ => "L0 以下 · 平静",
				},
				HorizontalAlignment.Left, -1f, 12, new Color(0.95f, 0.72f, 0.35f));

			Vector3 previous = route.EntryPoint;
			foreach (Vector3 waypoint in route.Waypoints)
			{
				if (TryProject(camera, previous, out Vector2 a) && TryProject(camera, waypoint, out Vector2 b))
					DrawLine(a, b, new Color(0.35f, 0.85f, 0.95f, 0.8f), 2f);
				previous = waypoint;
			}

			if (TryProject(camera, route.EntryPoint, out Vector2 entry))
				DrawMarker(font, entry, "IN", new Color(0.35f, 0.85f, 0.95f));
			if (TryProject(camera, route.MergePoint, out Vector2 merge))
				DrawMarker(font, merge, "MERGE", new Color(0.95f, 0.78f, 0.30f));
			if (TryProject(camera, route.CoverPoint, out Vector2 cover))
				DrawMarker(font, cover, "COVER", new Color(0.45f, 0.9f, 0.5f));

			foreach (KeyValuePair<string, EnemyAgent> pair in Source._postAgents)
			{
				EnemyAgent agent = pair.Value;
				if (agent == null || !IsInstanceValid(agent) || agent.IsDead)
					continue;

				StealthPostRole role = Source._postRoles.TryGetValue(pair.Key, out StealthPostRole r)
					? r
					: StealthPostRole.Avoidable;
				Color color = role switch
				{
					StealthPostRole.Captain => new Color(0.92f, 0.35f, 0.28f),
					StealthPostRole.MustHandle => new Color(0.95f, 0.60f, 0.25f),
					_ => new Color(0.45f, 0.80f, 0.55f),
				};

				if (TryProject(camera, agent.GlobalPosition + Vector3.Up * 1.9f, out Vector2 point))
				{
					DrawCircle(point, 4f, color);
					DrawString(font, point + new Vector2(8f, 4f), role.ToString(),
						HorizontalAlignment.Left, -1f, 11, color);
				}
			}
		}

		private void DrawMarker(Font font, Vector2 point, string label, Color color)
		{
			DrawCircle(point, 6f, color);
			DrawString(font, point + new Vector2(10f, 4f), label, HorizontalAlignment.Left, -1f, 12, color);
		}

		private static bool TryProject(Camera3D camera, Vector3 world, out Vector2 screen)
		{
			if (camera.IsPositionBehind(world))
			{
				screen = Vector2.Zero;
				return false;
			}

			screen = camera.UnprojectPosition(world);
			return true;
		}
	}
}
