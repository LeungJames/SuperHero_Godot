using Godot;
using System;

public enum DeathCause
{
	Gunshot,
	Fall,
	Drown,
}

/// <summary>
/// 玩家生命：枪伤 / 摔伤 / 落水，死亡广播。没有回血接口（设计文档 §3.4–§3.6）。
/// 摔伤所需的离地落差也在这里统计（§9.2 的 FallTracker）。
/// </summary>
public partial class PlayerHealth : Node
{
	public event Action<float, float> HealthChanged;
	public event Action<float, DeathCause> Damaged;
	public event Action<DeathCause> Died;

	public float Health { get; private set; }
	public float MaxHealth { get; private set; }
	public bool IsDead { get; private set; }

	private PlayerController _player;
	private PlayerData _data;
	private bool _airborne;
	private float _airbornePeakY;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_data = _player.Data;

		if (_data == null)
		{
			GD.PushError("PlayerHealth: Player 节点没有配置 PlayerData。");
			return;
		}

		MaxHealth = _data.MaxHealth;
		Health = MaxHealth;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsDead || _data == null)
			return;

		float y = _player.GlobalPosition.Y;

		if (!_player.IsOnFloor())
		{
			// 记录离地后到达过的最高点，落地时用它计算本段落差
			if (!_airborne)
			{
				_airborne = true;
				_airbornePeakY = y;
			}
			else
			{
				_airbornePeakY = Mathf.Max(_airbornePeakY, y);
			}

			return;
		}

		if (!_airborne)
			return;

		_airborne = false;
		float drop = _airbornePeakY - y;
		float damage = CalculateFallDamage(drop);
		if (damage <= 0f)
			return;

		GD.Print($"[PlayerHealth] 坠落 {drop:F2}m，摔伤 {damage:F0}");
		TakeDamage(damage, DeathCause.Fall);
	}

	public void TakeDamage(float amount, DeathCause cause)
	{
		if (IsDead || amount <= 0f)
			return;

		Health = Mathf.Max(0f, Health - amount);
		Damaged?.Invoke(amount, cause);
		HealthChanged?.Invoke(Health, MaxHealth);

		if (Health <= 0f)
			Die(cause);
	}

	public void Kill(DeathCause cause)
	{
		if (IsDead)
			return;

		Health = 0f;
		HealthChanged?.Invoke(Health, MaxHealth);
		Die(cause);
	}

	private float CalculateFallDamage(float drop)
	{
		if (drop <= _data.FallSafeHeight)
			return 0f;

		return Mathf.Min(100f, (drop - _data.FallSafeHeight) * _data.FallDamagePerMeter);
	}

	private void Die(DeathCause cause)
	{
		IsDead = true;
		_player.InputEnabled = false;
		_player.GetNode<PlayerCamera>("Head").LookEnabled = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		GD.Print($"[PlayerHealth] 死亡，死因 {cause}");
		Died?.Invoke(cause);
	}
}
