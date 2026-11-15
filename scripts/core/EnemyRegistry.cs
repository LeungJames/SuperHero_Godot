using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 敌人登记：进树时收集关卡里的全部敌人，统计存活数并在全歼时广播。
/// 不做 AI（设计文档 §9.3、§9.4）。
/// </summary>
public partial class EnemyRegistry : Node
{
	/// <summary>存活 / 总数变化（HUD 与 GameManager 订阅）。</summary>
	public event Action<int, int> CountChanged;

	/// <summary>任意敌人死亡（评级统计订阅；先于计数变化触发）。</summary>
	public event Action<EnemyHealth> EnemyDied;

	/// <summary>最后一名敌人死亡（胜负判定在这里触发）。</summary>
	public event Action AllCleared;

	public int AliveCount { get; private set; }
	public int TotalCount { get; private set; }
	public int Kills => TotalCount - AliveCount;

	private bool _cleared;
	private readonly HashSet<EnemyHealth> _registered = new();
	private readonly HashSet<BossController> _registeredBosses = new();

	public override void _Ready()
	{
		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyHealth health)
				continue;

			Register(health);
		}

		RegisterBosses();

		if (AliveCount == 0 && TotalCount > 0)
		{
			_cleared = true;
		}

		CountChanged?.Invoke(AliveCount, TotalCount);

		// 增援是运行时生成的，同样要计入存活数
		EnemyHealth.Spawned += Register;
	}

	public override void _ExitTree()
	{
		EnemyHealth.Spawned -= Register;
	}

	private void Register(EnemyHealth health)
	{
		if (health == null || !_registered.Add(health))
			return;

		TotalCount++;
		if (!health.IsDead)
			AliveCount++;

		health.Died += () => OnEnemyDied(health);
		CountChanged?.Invoke(AliveCount, TotalCount);
	}

	private void OnEnemyDied(EnemyHealth enemy)
	{
		EnemyDied?.Invoke(enemy);
		AliveCount = Mathf.Max(0, AliveCount - 1);
		CountChanged?.Invoke(AliveCount, TotalCount);

		CheckCleared();
	}

	/// <summary>Boss 也计入全歼：只有 Boss 与召唤物都死亡，直升机流程才会被触发。</summary>
	private void RegisterBosses()
	{
		foreach (Node node in GetTree().GetNodesInGroup("boss"))
		{
			if (node is not BossController boss || !_registeredBosses.Add(boss))
				continue;

			TotalCount++;
			if (!boss.IsDefeated)
				AliveCount++;

			boss.Defeated += () => OnBossDefeated(boss);
			CountChanged?.Invoke(AliveCount, TotalCount);
		}
	}

	private void OnBossDefeated(BossController boss)
	{
		if (!_registeredBosses.Contains(boss))
			return;

		AliveCount = Mathf.Max(0, AliveCount - 1);
		CountChanged?.Invoke(AliveCount, TotalCount);
		CheckCleared();
	}

	private void CheckCleared()
	{
		if (AliveCount > 0 || _cleared)
			return;

		_cleared = true;
		AllCleared?.Invoke();
	}
}
