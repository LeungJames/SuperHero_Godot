using Godot;

/// <summary>
/// 场地里的可破坏锚机（2.0 设计 §8.1、§8.4）：HP 见 <see cref="BossConfig.WinchHealth"/>。
/// 只做受击与断裂表现，不改变 Boss 招式或掉落，避免自创未裁决的机制。
/// </summary>
public partial class BossWinch : StaticBody3D, IDamageable
{
	public event System.Action Destroyed;

	[Export] public float MaxHealth { get; set; } = 300f;
	[Export] public PackedScene BreakScene { get; set; }

	public float Health { get; private set; }
	public bool IsDestroyed { get; private set; }

	private Node3D _visual;
	private Node _hitboxes;

	public override void _Ready()
	{
		Health = MaxHealth;
		_visual = GetNodeOrNull<Node3D>("Visual");
		_hitboxes = GetNodeOrNull<Node>("Hitboxes");
	}

	public void TakeDamage(in DamageInfo info)
	{
		if (IsDestroyed || info.Damage <= 0f)
			return;

		Health = Mathf.Max(0f, Health - info.Damage);
		if (Health > 0f)
			return;

		Destroy();
	}

	private void Destroy()
	{
		IsDestroyed = true;
		CollisionLayer = 0;

		if (_hitboxes != null)
		{
			foreach (Node child in _hitboxes.GetChildren())
			{
				if (child is Area3D area)
					area.CollisionLayer = 0;
			}
		}

		if (_visual != null)
			_visual.Rotation = new Vector3(0.35f, 0.2f, 0.5f);

		if (BreakScene?.Instantiate() is Node3D burst)
		{
			GetTree().CurrentScene?.AddChild(burst);
			burst.GlobalPosition = GlobalPosition + Vector3.Up * 1.2f;
		}

		AudioDirector.Instance?.PlayWorld(SfxKind.Kill, GlobalPosition, 2f, 0.7f);
		Destroyed?.Invoke();
	}
}
