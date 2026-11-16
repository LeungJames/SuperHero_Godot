using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 单局评级采集（设计文档 §6.7）：只统计，不计算评级、不改玩法。
/// 由 GameManager 在关卡开始时创建并绑定各组件，胜利结算时交给 RatingSystem。
/// </summary>
public partial class RunStatsCollector : Node
{
	/// <summary>同一动作 1s 内只计一次（§6.2）。</summary>
	private const float MobilityDedupeWindow = 1f;

	private readonly Dictionary<MobilityAction, float> _lastMobility = new();

	private PlayerHealth _health;
	private WeaponController _weapon;
	private EnemyRegistry _registry;
	private StealthDirector _stealth;

	private int _lastAlertRank;

	public int MobilityActions { get; private set; }
	/// <summary>Boss 战期间为 true：动作仍可执行，但不计入机动动作密度（§12.2）。</summary>
	public bool MobilitySuspended { get; set; }
	public int Falls { get; private set; }
	public int EmptyMags { get; private set; }
	public int StealthKills { get; private set; }
	public int AlertEscalations { get; private set; }
	public int Assassinations { get; private set; }

	public void Bind(PlayerController player, PlayerHealth health, WeaponController weapon, EnemyRegistry registry)
	{
		_health = health;
		_weapon = weapon;
		_registry = registry;

		if (player != null)
			player.MobilityPerformed += OnMobility;
		if (health != null)
			health.Damaged += OnDamaged;
		if (weapon != null)
			weapon.EmptyMagFired += OnEmptyMag;
		if (registry != null)
			registry.EnemyDied += OnEnemyDied;
	}

	/// <summary>StealthDirector 可能晚于 GameManager 进树，由调用方延迟绑定。</summary>
	public void BindStealth(StealthDirector stealth)
	{
		_stealth = stealth;
		if (_stealth == null)
			return;

		_lastAlertRank = Rank(_stealth.Level);
		_stealth.LevelChanged += OnAlertChanged;
		_stealth.ExecutionNoticed += OnExecution;
	}

	/// <summary>胜利结算前冻结指标（含未到 0.5s 的空仓）。</summary>
	public RunStats Build(float time, int kills)
	{
		_weapon?.FlushEmptyMag();

		return new RunStats
		{
			Time = time,
			MobilityActions = MobilityActions,
			Falls = Falls,
			EmptyMags = EmptyMags,
			Kills = kills,
			StealthKills = StealthKills,
			AlertEscalations = AlertEscalations,
			Executions = Assassinations,
		};
	}

	private void OnMobility(MobilityAction action)
	{
		if (MobilitySuspended)
			return;

		float now = Time.GetTicksMsec() / 1000f;
		if (_lastMobility.TryGetValue(action, out float last) && now - last < MobilityDedupeWindow)
			return;

		_lastMobility[action] = now;
		MobilityActions++;
	}

	private void OnDamaged(float amount, DeathCause cause)
	{
		if (cause == DeathCause.Fall)
			Falls++;
	}

	private void OnEmptyMag() => EmptyMags++;

	private void OnEnemyDied(EnemyHealth enemy)
	{
		if (enemy != null && !enemy.DiedInCombat)
			StealthKills++;
	}

	private void OnAlertChanged(StealthAlertLevel level)
	{
		int rank = Rank(level);
		if (rank > _lastAlertRank)
			AlertEscalations++;
		_lastAlertRank = rank;
	}

	private void OnExecution(EnemyHealth target, bool unaware) => Assassinations++;

	private static int Rank(StealthAlertLevel level) => level switch
	{
		StealthAlertLevel.L0 => 1,
		StealthAlertLevel.L1 => 2,
		StealthAlertLevel.L2 => 3,
		_ => 0,
	};
}
