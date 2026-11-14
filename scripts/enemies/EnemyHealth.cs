using Godot;
using System;

/// <summary>
/// 敌人生命：受伤、硬直、死亡倒地淡出与掉落弹药包。
/// 不做 AI 决策（设计文档 §9.3）。
/// </summary>
public partial class EnemyHealth : Node
{
	public event Action Died;
	public event Action<float> Staggered;

	[Export] public PackedScene AmmoPickupScene { get; set; }

	public float Health { get; private set; }
	public float MaxHealth { get; private set; }
	public bool IsDead { get; private set; }

	private EnemyData _data;
	private EnemyAgent _agent;
	private Node3D _model;
	private MeshInstance3D[] _meshes = Array.Empty<MeshInstance3D>();

	private float _fallTimer;
	private float _fadeTimer;
	private bool _fading;

	public override void _Ready()
	{
		_agent = GetParent<EnemyAgent>();
		_data = _agent.Data;
		_model = _agent.GetNodeOrNull<Node3D>("Model");

		MaxHealth = _data != null ? _data.MaxHealth : 100f;
		Health = MaxHealth;
		SetProcess(false);
		AddToGroup("enemy");
	}

	public void TakeDamage(in DamageInfo info)
	{
		if (IsDead || info.Damage <= 0f)
			return;

		Health = Mathf.Max(0f, Health - info.Damage);
		GD.Print($"[EnemyHealth] {_agent.Name} 受到 {info.Damage:F0} 伤害，剩余 {Health:F0}");

		if (_data != null && info.Damage >= _data.StaggerThreshold)
			Staggered?.Invoke(_data.StaggerDuration);

		if (Health <= 0f)
			Die();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (_fallTimer > 0f)
		{
			_fallTimer -= dt;
			if (_model != null)
			{
				float fallDuration = Mathf.Max(_data != null ? _data.DeathFallTime : 0.35f, 0.01f);
				float target = Mathf.DegToRad(90f);
				_model.Rotation = new Vector3(0f, _model.Rotation.Y,
					Mathf.MoveToward(_model.Rotation.Z, target, target / fallDuration * dt));
			}

			if (_fallTimer > 0f)
				return;
		}

		if (!_fading)
		{
			_fading = true;
			_fadeTimer = _data != null ? _data.DeathFadeTime : 2f;
			_meshes = CollectMeshes(_agent);
			return;
		}

		_fadeTimer -= dt;
		float fadeDuration = Mathf.Max(_data != null ? _data.DeathFadeTime : 2f, 0.01f);
		float alpha = Mathf.Clamp(1f - _fadeTimer / fadeDuration, 0f, 1f);
		foreach (MeshInstance3D mesh in _meshes)
			mesh.Transparency = alpha;

		if (_fadeTimer <= 0f)
		{
			if (_model != null)
				_model.Visible = false;
			SetProcess(false);
		}
	}

	private void Die()
	{
		IsDead = true;
		GD.Print($"[EnemyHealth] {_agent.Name} 死亡。");

		// 死亡立即停碰撞，剩下交给倒地与淡出
		_agent.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0u);
		if (_agent.GetNodeOrNull<Node>("Hitboxes") is Node hitboxes)
		{
			foreach (Node child in hitboxes.GetChildren())
			{
				if (child is Area3D area)
					area.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0u);
			}
		}

		Died?.Invoke();
		DropAmmo();

		_fallTimer = _data != null ? _data.DeathFallTime : 0.35f;
		SetProcess(true);
	}

	private void DropAmmo()
	{
		if (AmmoPickupScene == null || _data == null)
			return;

		if (AmmoPickupScene.Instantiate() is not AmmoPickup pickup)
			return;

		pickup.Rounds = _data.AmmoDropRounds;
		GetTree().CurrentScene?.AddChild(pickup);
		pickup.GlobalPosition = _agent.GlobalPosition + Vector3.Up * 0.5f;
	}

	private static MeshInstance3D[] CollectMeshes(Node root)
	{
		Godot.Collections.Array<Node> found = root.FindChildren("*", "MeshInstance3D", true, false);
		var meshes = new MeshInstance3D[found.Count];
		for (int i = 0; i < found.Count; i++)
			meshes[i] = (MeshInstance3D)found[i];
		return meshes;
	}
}
