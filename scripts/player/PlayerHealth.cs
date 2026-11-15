using Godot;
using System;

public enum DeathCause
{
	Gunshot,
	Fall,
	Drown,
	Explosion,
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
	private WeaponController _weapon;
	private Camera3D _camera;
	private bool _airborne;
	private float _airbornePeakY;
	private float _damageReduction;
	private float _damageReductionTimer;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_data = _player.Data;
		_weapon = _player.GetNodeOrNull<WeaponController>("Head/Camera3D/ViewModel/Weapon");
		_camera = _player.GetNodeOrNull<Camera3D>("Head/Camera3D");

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

		// 蹬墙跑 / 滑铲落地按配置减半，高处直坠保持原样
		bool mobility = _player.LandedWithMobility;
		if (mobility)
			damage *= _data.FallDamageMobilityScale;

		GD.Print($"[PlayerHealth] 坠落 {drop:F2}m，摔伤 {damage:F0}{(mobility ? "（身法落地减半）" : string.Empty)}");
		TakeDamage(damage, DeathCause.Fall);
	}

	public override void _Process(double delta)
	{
		if (_damageReductionTimer <= 0f)
			return;

		_damageReductionTimer = Mathf.Max(0f, _damageReductionTimer - (float)delta);
		if (_damageReductionTimer <= 0f)
			_damageReduction = 0f;
	}

	/// <summary>A4 处决后的短暂减伤；只降低受伤，不涉及回血。</summary>
	public void ApplyDamageReduction(float fraction, float duration)
	{
		if (duration <= 0f || fraction <= 0f)
			return;

		_damageReduction = Mathf.Clamp(fraction, 0f, 1f);
		_damageReductionTimer = duration;
	}

	public void TakeDamage(float amount, DeathCause cause) => TakeDamage(amount, cause, null);

	/// <summary>带来源位置的伤害入口：A5 格挡只在正面来弹时生效（§5.2）。</summary>
	public void TakeDamage(float amount, DeathCause cause, Vector3? source)
	{
		if (IsDead || amount <= 0f)
			return;

		// A5：刀格右键的 0.25s 窗口内挡下正面子弹；摔伤 / 落水 / 爆炸不受影响
		if (cause == DeathCause.Gunshot && BlocksIncoming(source))
			return;

		amount *= 1f - _damageReduction;
		Health = Mathf.Max(0f, Health - amount);
		Damaged?.Invoke(amount, cause);
		HealthChanged?.Invoke(Health, MaxHealth);

		if (Health <= 0f)
			Die(cause);
	}

	private bool BlocksIncoming(Vector3? source)
	{
		if (_weapon == null || !_weapon.IsBlocking)
			return false;
		if (source == null)
			return true;

		Vector3 forward = _camera != null
			? -_camera.GlobalTransform.Basis.Z
			: -_player.GlobalTransform.Basis.Z;
		Vector3 toSource = source.Value - _player.GlobalPosition;
		forward.Y = 0f;
		toSource.Y = 0f;

		if (forward.LengthSquared() < 0.0001f || toSource.LengthSquared() < 0.0001f)
			return true;
		return forward.Normalized().Dot(toSource.Normalized()) > 0f;
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
		// M8 落地缓冲：起算高度 +4m，摔伤 ×0.7
		float safeHeight = _data.FallSafeHeight + TalentEffects.FallSafeHeightAdd;
		if (drop <= safeHeight)
			return 0f;

		return Mathf.Min(100f, (drop - safeHeight) * _data.FallDamagePerMeter) * TalentEffects.FallDamageScale;
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
