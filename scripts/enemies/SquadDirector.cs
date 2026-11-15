using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 小队调度：按关卡编制表建队、每 3s 评估槽位、处理共享感知与后撤（2.0 设计 §2.2–§2.5）。
/// 关卡里只放一个本节点 + 一份 <see cref="SquadRoster"/>，敌人的数量与摆位保持 1.0 不变。
/// </summary>
public partial class SquadDirector : Node
{
	[Export] public SquadRoster Roster { get; set; }
	/// <summary>调试可视化默认是否打开；游戏内 F3 随时切换。</summary>
	[Export] public bool DebugOverlayVisible { get; set; }

	public CombatDirector Combat { get; private set; }
	public IReadOnlyList<SquadRuntime> Squads => _squads;
	public bool OverlayOn => _overlay != null && _overlay.Visible;
	public SquadConfig Config => Roster?.Config ?? _fallbackConfig;

	private readonly List<SquadRuntime> _squads = new();
	private readonly SquadConfig _fallbackConfig = new();
	private PlayerController _player;
	private PlayerHealth _playerHealth;
	private Camera3D _camera;
	private HudController _hud;
	private ComicOverlay _comic;
	private AIDebugOverlay _overlay;
	private bool _built;
	private int _buildAttempts;

	private readonly HashSet<EnemyAgent> _helpFlagged = new();

	private float _flankCalloutCd;
	private float _retreatCalloutCd;
	private float _helpCalloutCd;
	private float _lastPlayerDamageTimer;

	public override void _Ready()
	{
		AddToGroup("squad_director");

		Combat = new CombatDirector { Config = Config, Name = "CombatDirector" };
		AddChild(Combat);

		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player != null)
			_playerHealth = _player.GetNodeOrNull<PlayerHealth>("Health");
		Combat.Bind(_playerHealth);

		_hud = GetTree().GetFirstNodeInGroup("hud") as HudController;

		_overlay = new AIDebugOverlay { Name = "AIDebugOverlay", Source = this };
		var layer = new CanvasLayer { Name = "AIDebugLayer", Layer = 20 };
		AddChild(layer);
		layer.AddChild(_overlay);
		_overlay.Visible = DebugOverlayVisible;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (!_built && _buildAttempts < 120)
		{
			// 玩家 / 敌人可能比本节点晚进树，前两秒内反复尝试建队
			_buildAttempts++;
			_built = BuildSquads();
		}

		if (_player == null)
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_playerHealth == null && _player != null)
		{
			_playerHealth = _player.GetNodeOrNull<PlayerHealth>("Health");
			Combat.Bind(_playerHealth);
		}
		// HUD 在 Managers 之后进树，_Ready 时可能还取不到，这里补查
		if (_hud == null || !GodotObject.IsInstanceValid(_hud))
			_hud = GetTree().GetFirstNodeInGroup("hud") as HudController;
		if (_comic == null || !GodotObject.IsInstanceValid(_comic))
			_comic = GetTree().GetFirstNodeInGroup("comic_overlay") as ComicOverlay;
		if (_camera == null)
			_camera = GetViewport()?.GetCamera3D();

		_flankCalloutCd = Mathf.Max(0f, _flankCalloutCd - dt);
		_retreatCalloutCd = Mathf.Max(0f, _retreatCalloutCd - dt);
		_helpCalloutCd = Mathf.Max(0f, _helpCalloutCd - dt);

		UpdateLookPressure(dt);

		foreach (SquadRuntime squad in _squads)
			squad.Tick(dt, this);

		UpdateRetreats(dt);
		Combat.Tick(dt);
	}

	/// <summary>F3 开关可视化（设计 §2.9）；探针 / 演示也可直接调用。</summary>
	public void SetOverlayVisible(bool visible)
	{
		if (_overlay != null)
			_overlay.Visible = visible;
	}

	/// <summary>F3 开关可视化（设计 §2.9）。</summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key)
			return;

		if (key.Keycode == Key.F3 && _overlay != null)
			_overlay.Visible = !_overlay.Visible;
	}

	private bool BuildSquads()
	{
		if (Roster == null || Roster.Squads.Count == 0)
			return true;

		// 关卡里的敌人挂在 World/Level/Enemies 下，本节点一般在 World 上；
		// 用当前场景根兜底，避免因挂载层级不同而搜不到人。
		Node levelRoot = GetTree().CurrentScene ?? GetParent();
		if (levelRoot == null)
			return false;

		var byName = new Dictionary<string, EnemyAgent>();
		CollectEnemies(levelRoot, byName);
		if (byName.Count == 0)
			return false;

		_squads.Clear();

		foreach (SquadEntry entry in Roster.Squads)
		{
			if (entry == null || string.IsNullOrEmpty(entry.Id))
				continue;

			EnemyAgent leader = Lookup(byName, entry.Leader);
			if (leader == null)
			{
				GD.PushWarning($"SquadDirector: 找不到队长 {entry.Leader}（小队 {entry.Id}）");
				continue;
			}

			var squad = new SquadRuntime(entry.Id, Config, leader) { Director = this };
			foreach (string memberName in entry.Members)
			{
				EnemyAgent member = Lookup(byName, memberName);
				if (member == null)
				{
					GD.PushWarning($"SquadDirector: 找不到成员 {memberName}（小队 {entry.Id}）");
					continue;
				}

				squad.AddMember(member);
			}

			foreach (EnemyAgent member in squad.Members)
				member.Squad = squad;

			_squads.Add(squad);
		}

		// 建队后立刻分一次槽位，避免开场几秒无分工
		foreach (SquadRuntime squad in _squads)
			ReviewSlots(squad);

		GD.Print($"[SquadDirector] 建队完成：{_squads.Count} 队，共 {_squads.Sum(s => s.Members.Count)} 人。");
		return true;
	}

	private static void CollectEnemies(Node root, Dictionary<string, EnemyAgent> into)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is EnemyAgent agent)
				into[agent.Name] = agent;
			CollectEnemies(child, into);
		}
	}

	private static EnemyAgent Lookup(Dictionary<string, EnemyAgent> map, string name)
	{
		if (string.IsNullOrEmpty(name))
			return null;
		return map.TryGetValue(name, out EnemyAgent agent) ? agent : null;
	}

	// ------------------------------------------------------------ 槽位

	/// <summary>每 3s 的槽位评估（§2.3 的五条规则）。</summary>
	public void ReviewSlots(SquadRuntime squad)
	{
		List<EnemyAgent> alive = squad.Alive.ToList();
		if (alive.Count == 0)
			return;

		if (squad.Broken)
		{
			// 队长阵亡：降级为独立防守，不再包抄
			foreach (EnemyAgent m in alive)
			{
				m.HoldPost = true;
				m.SetSlot(SquadSlot.Suppress);
				m.SlotTarget = m.GlobalPosition;
			}
			return;
		}

		// 没有任何接触时不分工：小队该巡逻就巡逻，避免"没看见人也在包抄"
		if (!squad.AnyoneHasSight() && !squad.HasSharedContact)
			return;

		List<EnemyAgent> pool = alive.Where(m => !m.IsRetreating).ToList();
		if (pool.Count == 0)
			return;

		int size = pool.Count;
		int flankBudget = size >= 2 ? Mathf.Min(Mathf.Max(squad.Config.FlankMax, 0), 1) : 0;
		if (flankBudget > 0 && pool.All(m => m.FlankBlocked))
			flankBudget = 0;

		// 1) 压制：优先沿用当前压制手（有视线且在合理距离），避免每次评估都换人
		Vector3 playerPos = _player?.GlobalPosition ?? squad.Leader?.GlobalPosition ?? Vector3.Zero;
		EnemyAgent suppress = pool.FirstOrDefault(m =>
			m.Slot == SquadSlot.Suppress && m.HasSight &&
			m.DistanceToPlayer >= squad.Config.SuppressMinRange * 0.6f &&
			m.DistanceToPlayer <= squad.Config.SuppressMaxRange * 1.4f);

		// 没有合适的现任时：看得见且落在压制距离内的优先；都没有视线就让最近的去
		suppress ??= pool
			.Where(m => m.HasSight && m.DistanceToPlayer >= squad.Config.SuppressMinRange * 0.75f)
			.OrderBy(m => Mathf.Abs(m.DistanceToPlayer - squad.Config.SuppressMaxRange * 0.7f))
			.FirstOrDefault();

		suppress ??= squad.Leader != null && pool.Contains(squad.Leader)
			? squad.Leader
			: pool.OrderBy(m => m.DistanceToPlayer).First();

		// 2) 包抄：沿用当前包抄手；只有失效（被盯住 / 目标点失效 / 脱离范围）才换人
		EnemyAgent flank = null;
		if (flankBudget > 0)
		{
			EnemyAgent current = pool.FirstOrDefault(m => m.Slot == SquadSlot.Flank && m != suppress);
			bool stillValid = current != null && !current.FlankBlocked && current.SlotTarget != Vector3.Zero
				&& FlatDistance(current.SlotTarget, playerPos) <= squad.Config.FlankMaxRange + 4f
				&& current.DistanceToPlayer <= squad.Config.FlankMaxRange + 4f;

			if (stillValid)
			{
				flank = current;
			}
			else
			{
				flank = pool
					.Where(m => m != suppress && !m.FlankBlocked)
					.OrderBy(m => m.LookedAtTime)
					.ThenBy(m => m.DistanceToPlayer)
					.FirstOrDefault();

				if (flank != null && !TryAssignFlankPoint(squad, flank))
					flank = null;
			}
		}

		foreach (EnemyAgent m in pool)
		{
			SquadSlot slot = m == suppress ? SquadSlot.Suppress
				: m == flank ? SquadSlot.Flank
				: SquadSlot.Support;

			bool becameFlank = slot == SquadSlot.Flank && m.Slot != SquadSlot.Flank;
			m.SetSlot(slot);

			if (slot == SquadSlot.Suppress)
			{
				// 维持压制距离：太远压上去，太近退一点
				Vector3 dir = m.GlobalPosition - playerPos;
				dir.Y = 0f;
				Vector3 dirNormalized = dir.LengthSquared() > 0.01f ? dir.Normalized() : Vector3.Forward;
				float desired = Mathf.Clamp(m.DistanceToPlayer, squad.Config.SuppressMinRange, squad.Config.SuppressMaxRange);
				Vector3 point = playerPos + dirNormalized * desired;
				// 压制点与敌人同层，避免把目标点落到楼下或屋顶
				point.Y = m.GlobalPosition.Y;
				m.SlotTarget = point;
			}

			if (becameFlank)
				CalloutFlank(m);
		}
	}

	/// <summary>被玩家直视的时长：用于"绕后被看见就换侧翼"（§2.3 规则 4）。</summary>
	private void UpdateLookPressure(float dt)
	{
		if (_player == null || _camera == null)
			return;

		Vector3 eye = _camera.GlobalPosition;
		Vector3 forward = -_camera.GlobalTransform.Basis.Z;

		foreach (SquadRuntime squad in _squads)
		{
			foreach (EnemyAgent m in squad.Alive)
			{
				Vector3 to = m.GlobalPosition + Vector3.Up * 1.1f - eye;
				float distance = to.Length();
				if (distance < 0.01f)
					continue;

				float angle = Mathf.RadToDeg(forward.AngleTo(to / distance));
				bool looked = angle < 28f && distance < 30f && HasLineOfSight(eye, m.GlobalPosition + Vector3.Up * 1.1f);
				m.LookedAtTime = looked ? m.LookedAtTime + dt : Mathf.Max(0f, m.LookedAtTime - dt * 2f);

				// 直视超过阈值且没有掩体（能看见 = 没掩体）→ 这个成员暂时不适合包抄
				m.FlankBlocked = m.LookedAtTime > squad.Config.FlankAbortLookTime && looked;
			}
		}
	}

	/// <summary>给包抄成员找一个绕后的导航点；玩家视线的另一侧优先。</summary>
	private bool TryAssignFlankPoint(SquadRuntime squad, EnemyAgent flanker)
	{
		if (_player == null)
			return false;

		Vector3 playerPos = _player.GlobalPosition;
		Vector3 forward = -_player.GlobalTransform.Basis.Z;
		forward.Y = 0f;
		forward = forward.LengthSquared() > 0.001f ? forward.Normalized() : Vector3.Forward;

		Vector3 right = new(-forward.Z, 0f, forward.X);
		float sideSign = Mathf.Sign(right.Dot(flanker.GlobalPosition - playerPos));
		if (Mathf.Abs(sideSign) < 0.01f)
			sideSign = 1f;

		// 稳定在包抄距离环上：取包抄距离中值（8m），不随敌人靠近而不断缩圈
		float ring = (squad.Config.FlankMinRange + squad.Config.FlankMaxRange) * 0.5f;
		float distance = Mathf.Clamp(
			(flanker.GlobalPosition - playerPos).Length(),
			squad.Config.FlankMinRange + 2f,
			ring);

		// 优先侧后方 130°：略深于验收的 120°，跑到位时角度落在 120° 附近
		Vector3 back120 = (-forward * 0.643f + right * (0.766f * sideSign)).Normalized();
		Vector3[] directions =
		{
			back120,
			(-forward * 0.643f + right * (-0.766f * sideSign)).Normalized(),
			(-forward * 0.5f + right * (0.5f * sideSign)).Normalized(),
			(right * sideSign).Normalized(),
		};

		Vector3[] candidates = new Vector3[directions.Length];
		for (int i = 0; i < directions.Length; i++)
			candidates[i] = playerPos + directions[i] * distance;

		Rid map = _player.GetWorld3D().NavigationMap;
		// 建队可能发生在导航地图同步之前；未同步时本轮不分包抄，等下个评估周期
		if (NavigationServer3D.MapGetIterationId(map) == 0)
			return false;

		foreach (Vector3 candidate in candidates)
		{
			Vector3 snapped = NavigationServer3D.MapGetClosestPoint(map, candidate);
			if (snapped == Vector3.Zero && candidate.LengthSquared() > 0.01f)
				continue;

			float heightGap = Mathf.Abs(snapped.Y - playerPos.Y);
			if (heightGap > 3f)
				continue;

			if (!HasNavigablePath(flanker.GlobalPosition, snapped))
				continue;

			flanker.SlotTarget = snapped;
			return true;
		}

		return false;
	}

	private bool HasNavigablePath(Vector3 from, Vector3 to)
	{
		Vector3[] path = NavigationServer3D.MapGetPath(_player.GetWorld3D().NavigationMap, from, to, true);
		if (path.Length < 2)
			return false;

		Vector3 end = path[path.Length - 1];
		return new Vector2(end.X - to.X, end.Z - to.Z).Length() <= 2.5f;
	}

	// ------------------------------------------------------------ 后撤

	private void UpdateRetreats(float dt)
	{
		foreach (SquadRuntime squad in _squads)
		{
			foreach (EnemyAgent m in squad.Alive)
			{
				m.RetreatCooldownLeft = Mathf.Max(0f, m.RetreatCooldownLeft - dt);

				if (m.IsRetreating)
				{
					m.RetreatTimeLeft -= dt;
					if (m.RetreatTimeLeft <= 0f)
						m.EndRetreat();
					continue;
				}

				if (squad.Broken || m.RetreatCooldownLeft > 0f)
					continue;
				if (m.HealthRatio > squad.Config.RetreatHealthRatio)
					continue;

				// 8m 内没有能断开玩家视线的掩体就死战（不回血）
				if (!TryFindRetreatPoint(squad, m, out Vector3 cover))
					continue;

				m.BeginRetreat(cover, squad.Config.RetreatDuration, squad.Config.RetreatCooldown);
				CalloutRetreat(m);
				CalloutHelp();

				// 立刻重评：让同伴接替火力
				ReviewSlots(squad);
			}

			// 队友阵亡也要喊一次求援（每个成员只报一次，其余交给冷却）
			foreach (EnemyAgent member in squad.Members)
			{
				if (member == null || !GodotObject.IsInstanceValid(member) || !member.IsDead)
					continue;
				if (_helpFlagged.Add(member))
					CalloutHelp();
			}
		}
	}

	private bool TryFindRetreatPoint(SquadRuntime squad, EnemyAgent agent, out Vector3 point)
	{
		point = Vector3.Zero;
		if (_player == null)
			return false;

		Vector3 playerEye = _player.GlobalPosition + Vector3.Up * 1.4f;
		Vector3 away = agent.GlobalPosition - _player.GlobalPosition;
		away.Y = 0f;
		Vector3 back = away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Forward;

		float radius = squad.Config.RetreatCoverRadius;
		// 由近到远采样：正后方、左右 45°、左右 90°
		float[] angles = { 0f, 45f, -45f, 90f, -90f };
		float[] distances = { 2.5f, 4f, 6f };

		Rid map = _player.GetWorld3D().NavigationMap;
		if (NavigationServer3D.MapGetIterationId(map) == 0)
			return false;

		foreach (float distance in distances)
		{
			foreach (float angle in angles)
			{
				if (distance > radius)
					continue;

				Vector3 dir = back.Rotated(Vector3.Up, Mathf.DegToRad(angle));
				Vector3 candidate = agent.GlobalPosition + dir * distance;
				Vector3 snapped = NavigationServer3D.MapGetClosestPoint(map, candidate);
				if (snapped == Vector3.Zero && candidate.LengthSquared() > 0.01f)
					continue;

				if (Mathf.Abs(snapped.Y - agent.GlobalPosition.Y) > 2.5f)
					continue;

				if (HasLineOfSight(playerEye, snapped + Vector3.Up * 1.2f))
					continue;

				if (!HasNavigablePath(agent.GlobalPosition, snapped))
					continue;

				point = snapped;
				return true;
			}
		}

		return false;
	}

	private bool HasLineOfSight(Vector3 from, Vector3 to)
	{
		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to, 1u);
		return _player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
	}

	private static float FlatDistance(Vector3 a, Vector3 b) =>
		new Vector2(a.X - b.X, a.Z - b.Z).Length();

	// ------------------------------------------------------------ 无线电提示

	private void CalloutFlank(EnemyAgent flanker)
	{
		if (_flankCalloutCd > 0f)
			return;
		_flankCalloutCd = Config.FlankCalloutCooldown;
		_hud?.ShowMessage("无线电：有人绕后。", new Color(0.92f, 0.55f, 0.25f));
		_comic?.ShowWorld("TCH!", flanker.GlobalPosition, new Color(0.95f, 0.72f, 0.30f), 0.7f);
	}

	private void CalloutRetreat(EnemyAgent agent)
	{
		if (_retreatCalloutCd > 0f)
			return;
		_retreatCalloutCd = Config.RetreatCalloutCooldown;
		_comic?.ShowWorld("TCH!", agent.GlobalPosition, new Color(0.72f, 0.75f, 0.80f), 0.7f);
	}

	/// <summary>小队求援：成员重伤或队长阵亡时由 HUD 播报。</summary>
	public void CalloutHelp()
	{
		if (_helpCalloutCd > 0f)
			return;
		_helpCalloutCd = Config.HelpCalloutCooldown;
		_hud?.ShowMessage("无线电：需要支援！", new Color(0.89f, 0.34f, 0.18f));
	}
}
