using Godot;
using System.Collections.Generic;

/// <summary>
/// 同屏开火令牌（2.0 设计 §2.6）。只有持有令牌的敌人才会真正扣扳机，
/// 其余做移动与掩护；令牌按时间强制交接，形成"压制—喘息"的节奏，
/// 而不是把所有敌人的射速一起调低。
/// </summary>
public partial class CombatDirector : Node
{
	public SquadConfig Config { get; set; }

	private readonly List<EnemyAgent> _holders = new();
	private readonly Dictionary<EnemyAgent, float> _holdTimers = new();
	private float _gapTimer;
	private float _waveTimer;
	private float _waveLeft;
	private float _playerCalmTimer;
	private PlayerHealth _playerHealth;

	/// <summary>当前实际可用的令牌数（含压力波临时 +1）。</summary>
	public int TokenBudget
	{
		get
		{
			int baseTokens = Config != null ? Config.FireTokens : 2;
			return baseTokens + (_waveLeft > 0f ? 1 : 0);
		}
	}

	public IReadOnlyList<EnemyAgent> Holders => _holders;
	public bool PressureWaveActive => _waveLeft > 0f;

	public void Bind(PlayerHealth health)
	{
		if (_playerHealth != null)
			_playerHealth.Damaged -= OnPlayerDamaged;

		_playerHealth = health;
		if (_playerHealth != null)
			_playerHealth.Damaged += OnPlayerDamaged;
	}

	public override void _ExitTree()
	{
		if (_playerHealth != null)
			_playerHealth.Damaged -= OnPlayerDamaged;
	}

	private void OnPlayerDamaged(float amount, DeathCause cause) => _playerCalmTimer = 0f;

	public bool HasToken(EnemyAgent agent) => _holders.Contains(agent);

	/// <summary>申请令牌；分配不到就返回 false，调用方转为移动 / 掩护。</summary>
	public bool Request(EnemyAgent agent)
	{
		if (_holders.Contains(agent))
			return true;

		if (_gapTimer > 0f || _holders.Count >= TokenBudget)
			return false;

		_holders.Add(agent);
		_holdTimers[agent] = Config != null ? Config.TokenHoldTime : 2.5f;
		return true;
	}

	public void Release(EnemyAgent agent)
	{
		if (!_holders.Remove(agent))
			return;

		_holdTimers.Remove(agent);
		_gapTimer = Config != null ? Config.TokenHandoverGap : 0.35f;
	}

	public void Tick(float dt)
	{
		_gapTimer = Mathf.Max(0f, _gapTimer - dt);
		_waveTimer += dt;
		_waveLeft = Mathf.Max(0f, _waveLeft - dt);
		_playerCalmTimer += dt;

		float interval = Config != null ? Config.PressureWaveInterval : 9f;
		float calm = Config != null ? Config.PressureWavePlayerCalmTime : 3.5f;
		// interval <= 0 表示关闭压力波，只保留基础令牌
		if (interval > 0f && _waveTimer >= interval)
		{
			_waveTimer = 0f;
			// 玩家刚挨打时不给压力波：先让玩家有掩体喘息，压上来要"讲道理"
			if (_playerCalmTimer >= calm && _holders.Count > 0)
				_waveLeft = Config != null ? Config.PressureWaveDuration : 2.5f;
		}

		// 持有时间到期强制交接：换人开火而不是同一人压到底
		for (int i = _holders.Count - 1; i >= 0; i--)
		{
			EnemyAgent holder = _holders[i];
			if (holder == null || !IsInstanceValid(holder) || holder.IsDead)
			{
				Release(holder);
				continue;
			}

			float left = _holdTimers[holder] - dt;
			_holdTimers[holder] = left;
			if (left <= 0f)
				Release(holder);
		}
	}

	/// <summary>清空令牌（换关 / 重开时用）。</summary>
	public void ResetTokens()
	{
		_holders.Clear();
		_holdTimers.Clear();
		_gapTimer = 0f;
	}
}
