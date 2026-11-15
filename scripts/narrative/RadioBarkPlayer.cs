using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 无线电台词播放器（2.0 设计 §4.3）。按帮派加载 7 个语境，每类独立冷却，
/// 同一条台词 30s 内不重复；只播 HUD 文本与电台音，不影响任何玩法数值。
/// </summary>
public partial class RadioBarkPlayer : Node
{
	public const string ContextSpot = "spot";
	public const string ContextSuppress = "suppress";
	public const string ContextFlank = "flank";
	public const string ContextReinforce = "reinforce";
	public const string ContextRetreat = "retreat";
	public const string ContextAlertUp = "alert_up";
	public const string ContextClear = "clear";

	private static readonly string[] AllContexts =
	{
		ContextSpot, ContextSuppress, ContextFlank, ContextReinforce,
		ContextRetreat, ContextAlertUp, ContextClear,
	};

	private static readonly Color RadioColor = new(0.92f, 0.55f, 0.25f);
	private const float LineDedupeTime = 30f;
	private const float ClearCombatGap = 15f;

	[Export] public string GangId { get; set; } = "salt";
	[Export] public string RadioPathFormat { get; set; } = "res://data/narrative/radio/{0}_{1}.tres";

	public string LastContext { get; private set; } = string.Empty;
	public string LastLine { get; private set; } = string.Empty;
	public int BarkCount { get; private set; }
	/// <summary>成功加载的语境数量（正常为 7）。</summary>
	public int LoadedContexts => _sets.Count;

	public bool HasContext(string context) => _sets.ContainsKey(context);

	/// <summary>每次播音后触发，探针用于核对冷却与去重。</summary>
	public event Action<string, string, Vector3> Barked;

	private readonly Dictionary<string, RadioLineSet> _sets = new();
	private readonly Dictionary<string, float> _contextCooldown = new();
	private readonly Dictionary<string, float[]> _lineLastPlayed = new();
	private readonly HashSet<ulong> _subscribedEnemies = new();
	private readonly List<(EnemyHealth Health, Action Hit, EnemyAgent Agent, Action Alert)> _subscriptions = new();

	private HudController _hud;
	private AudioDirector _audio;
	private StealthDirector _stealth;
	private StealthAlertLevel _lastLevel = StealthAlertLevel.None;
	private float _clock;
	private float _clearTimer;
	private bool _combatSeen;

	public override void _Ready()
	{
		AddToGroup("radio_bark");
		LoadSets();

		foreach (string context in AllContexts)
			_contextCooldown[context] = 0f;
	}

	public override void _ExitTree()
	{
		foreach ((EnemyHealth health, Action hit, EnemyAgent agent, Action alert) in _subscriptions)
		{
			if (health != null && GodotObject.IsInstanceValid(health))
				health.Hit -= hit;
			if (agent != null && GodotObject.IsInstanceValid(agent))
				agent.Alerted -= alert;
		}
		_subscriptions.Clear();

		if (_stealth != null && GodotObject.IsInstanceValid(_stealth))
			_stealth.LevelChanged -= OnStealthLevelChanged;
	}

	public override void _Process(double delta)
	{
		if (GetTree().Paused)
			return;

		float dt = (float)delta;
		_clock += dt;

		foreach (string context in AllContexts)
			_contextCooldown[context] = Mathf.Max(0f, _contextCooldown[context] - dt);

		EnsureRefs();
		SubscribeEnemies();
		UpdateClearBark(dt);
	}

	// ---------------------------------------------------------------- 对外接口

	/// <summary>
	/// 尝试播一条该语境台词。冷却中 / 无数据 / 30s 内全播过时返回 false，
	/// 调用方可回退到旧提示，保证功能不依赖电台数据。
	/// </summary>
	public bool TryBark(string context, Vector3 position = default)
	{
		if (string.IsNullOrEmpty(context))
			return false;
		if (!_sets.TryGetValue(context, out RadioLineSet set) || set.Lines.Length == 0)
			return false;
		if (_contextCooldown.TryGetValue(context, out float left) && left > 0f)
			return false;

		int index = PickLine(context, set.Lines.Length);
		if (index < 0)
			return false;

		_contextCooldown[context] = CooldownFor(context);
		_lineLastPlayed[context][index] = _clock;

		string text = set.Lines[index];
		LastContext = context;
		LastLine = text;
		BarkCount++;

		_hud?.ShowMessage($"无线电：{text}", RadioColor);
		_audio?.PlayRadioCue();
		Barked?.Invoke(context, text, position);
		return true;
	}

	public float CooldownLeft(string context) =>
		_contextCooldown.TryGetValue(context, out float left) ? left : 0f;

	/// <summary>探针 / 调试：清空冷却，便于逐类验证。</summary>
	public void DebugResetCooldowns()
	{
		foreach (string context in AllContexts)
			_contextCooldown[context] = 0f;
	}

	// ---------------------------------------------------------------- 事件源

	private void EnsureRefs()
	{
		if (_hud == null || !GodotObject.IsInstanceValid(_hud))
			_hud = GetTree().GetFirstNodeInGroup("hud") as HudController;
		if (_audio == null || !GodotObject.IsInstanceValid(_audio))
			_audio = GetTree().CurrentScene?.FindChild("AudioDirector", true, false) as AudioDirector;

		if (_stealth == null || !GodotObject.IsInstanceValid(_stealth))
		{
			_stealth = GetTree().GetFirstNodeInGroup("stealth_director") as StealthDirector;
			if (_stealth != null)
			{
				_lastLevel = _stealth.Level;
				_stealth.LevelChanged += OnStealthLevelChanged;
			}
		}
	}

	private void SubscribeEnemies()
	{
		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyHealth health)
				continue;
			if (health.GetParent() is not EnemyAgent agent)
				continue;
			if (!_subscribedEnemies.Add(agent.GetInstanceId()))
				continue;

			Action alert = () => TryBark(ContextSpot, agent.GlobalPosition);
			Action hit = () => OnEnemyHit(agent);
			agent.Alerted += alert;
			health.Hit += hit;
			_subscriptions.Add((health, hit, agent, alert));
		}
	}

	private void OnEnemyHit(EnemyAgent agent)
	{
		if (agent == null || !GodotObject.IsInstanceValid(agent) || agent.IsDead)
			return;
		TryBark(ContextReinforce, agent.GlobalPosition);
	}

	private void OnStealthLevelChanged(StealthAlertLevel level)
	{
		bool escalated = level > _lastLevel;
		_lastLevel = level;
		if (!escalated || level == StealthAlertLevel.None)
			return;

		TryBark(ContextAlertUp, _stealth?.LastKnownPosition ?? Vector3.Zero);
	}

	private void UpdateClearBark(float dt)
	{
		bool combat = false;
		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyHealth health || health.IsDead)
				continue;
			if (health.GetParent() is EnemyAgent agent && agent.State == EnemyState.Combat)
			{
				combat = true;
				break;
			}
		}

		if (combat)
		{
			_combatSeen = true;
			_clearTimer = 0f;
			return;
		}

		if (!_combatSeen)
			return;

		_clearTimer += dt;
		if (_clearTimer < ClearCombatGap)
			return;

		if (TryBark(ContextClear, _stealth?.LastKnownPosition ?? Vector3.Zero))
		{
			_combatSeen = false;
			_clearTimer = 0f;
		}
	}

	// ---------------------------------------------------------------- 数据

	private void LoadSets()
	{
		foreach (string context in AllContexts)
		{
			string path = string.Format(RadioPathFormat, GangId, context);
			if (!ResourceLoader.Exists(path))
			{
				GD.PushWarning($"RadioBarkPlayer: 缺少电台数据 {path}");
				continue;
			}

			var set = GD.Load<RadioLineSet>(path);
			if (set == null || set.Lines.Length == 0)
				continue;

			_sets[context] = set;
			_lineLastPlayed[context] = new float[set.Lines.Length];
			for (int i = 0; i < _lineLastPlayed[context].Length; i++)
				_lineLastPlayed[context][i] = -LineDedupeTime;
		}

		GD.Print($"[Radio] gang={GangId} contexts={_sets.Count}");
	}

	private int PickLine(string context, int count)
	{
		if (!_lineLastPlayed.TryGetValue(context, out float[] last))
			return -1;

		var candidates = new List<int>();
		for (int i = 0; i < count; i++)
		{
			if (_clock - last[i] >= LineDedupeTime)
				candidates.Add(i);
		}

		if (candidates.Count == 0)
			return -1;

		return candidates[(int)(GD.Randi() % (uint)candidates.Count)];
	}

	private static float CooldownFor(string context) => context switch
	{
		ContextSpot => 20f,
		ContextSuppress => 12f,
		ContextFlank => 15f,
		ContextReinforce => 18f,
		ContextRetreat => 20f,
		ContextAlertUp => 60f,
		ContextClear => 30f,
		_ => 20f,
	};
}
