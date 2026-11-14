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

	/// <summary>最后一名敌人死亡（胜负判定在这里触发）。</summary>
	public event Action AllCleared;

	public int AliveCount { get; private set; }
	public int TotalCount { get; private set; }
	public int Kills => TotalCount - AliveCount;

	private bool _cleared;

	public override void _Ready()
	{
		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is not EnemyHealth health)
				continue;

			TotalCount++;
			if (!health.IsDead)
				AliveCount++;

			health.Died += () => OnEnemyDied(health);
		}

		if (AliveCount == 0 && TotalCount > 0)
		{
			_cleared = true;
		}

		CountChanged?.Invoke(AliveCount, TotalCount);
	}

	private void OnEnemyDied(EnemyHealth enemy)
	{
		AliveCount = Mathf.Max(0, AliveCount - 1);
		CountChanged?.Invoke(AliveCount, TotalCount);

		if (AliveCount <= 0 && !_cleared)
		{
			_cleared = true;
			AllCleared?.Invoke();
		}
	}
}
