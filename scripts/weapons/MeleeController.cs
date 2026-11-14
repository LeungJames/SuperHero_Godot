using Godot;
using System;

/// <summary>
/// 近战（枪托）：短距胶囊判定 + 第一人称挥动（设计文档 §5.2）。
/// 只负责判定与节奏，数值在 MeleeData；命中走 Hitbox，与子弹共用部位倍率。
/// </summary>
public partial class MeleeController : Node3D
{
	[Export] public MeleeData Data { get; set; }

	/// <summary>开始挥击（音效用）。</summary>
	public event Action Swung;

	/// <summary>结算结果：命中点（未命中为零向量）。</summary>
	public event Action<Vector3, bool> Resolved;

	private PlayerController _player;
	private PlayerHealth _health;
	private Camera3D _camera;
	private PistolController _pistol;
	private readonly Godot.Collections.Array<Rid> _exclude = new();

	private float _cooldown;
	private float _swingTimer;
	private bool _resolved;

	private bool CanAct => _player != null && _player.InputEnabled && _health != null && !_health.IsDead;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_health = _player.GetNodeOrNull<PlayerHealth>("Health");
		_camera = _player.GetNodeOrNull<Camera3D>("Head/Camera3D");
		_pistol = _player.GetNodeOrNull<PistolController>("Head/Camera3D/ViewModel/Pistol");
		_exclude.Add(_player.GetRid());
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("melee"))
			TrySwing();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_cooldown = Mathf.Max(0f, _cooldown - dt);

		if (_swingTimer <= 0f)
			return;

		_swingTimer -= dt;
		float progress = Mathf.Clamp(1f - _swingTimer / Mathf.Max(Data.SwingTime, 0.01f), 0f, 1f);
		if (_pistol != null)
			_pistol.MeleeSwing = SwingPose(progress);

		if (!_resolved && progress >= Data.HitTimeRatio)
		{
			_resolved = true;
			ResolveHit();
		}
	}

	public bool TrySwing()
	{
		if (!CanAct || Data == null || _cooldown > 0f || _swingTimer > 0f)
			return false;

		_cooldown = Data.Cooldown;
		_swingTimer = Data.SwingTime;
		_resolved = false;
		Swung?.Invoke();
		NoiseEmitter.Emit(_player.GlobalPosition, NoiseKind.Footstep);
		return true;
	}

	/// <summary>挥动姿势：前 35% 抡出去，后面收回。</summary>
	private static float SwingPose(float progress)
	{
		return progress < 0.35f
			? Mathf.SmoothStep(0f, 1f, progress / 0.35f)
			: Mathf.SmoothStep(1f, 0f, (progress - 0.35f) / 0.65f);
	}

	/// <summary>沿视线做一次胶囊重叠，取朝向上最近的命中盒；墙体会挡住近战。</summary>
	private void ResolveHit()
	{
		if (_camera == null)
			return;

		Vector3 origin = _camera.GlobalPosition;
		Vector3 forward = -_camera.GlobalTransform.Basis.Z;

		float wallDistance = Data.Range;
		PhysicsRayQueryParameters3D wallQuery =
			PhysicsRayQueryParameters3D.Create(origin, origin + forward * Data.Range, 1u);
		wallQuery.Exclude = _exclude;
		Godot.Collections.Dictionary wall = _player.GetWorld3D().DirectSpaceState.IntersectRay(wallQuery);
		if (wall.Count > 0)
			wallDistance = origin.DistanceTo(wall["position"].AsVector3());

		var capsule = new CapsuleShape3D { Radius = Data.HitRadius, Height = Data.Range };
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = capsule,
			Transform = new Transform3D(
				new Basis(new Quaternion(Vector3.Up, forward)),
				origin + forward * (Data.Range * 0.5f)),
			CollisionMask = 1u | 8u,
			CollideWithAreas = true,
		};
		query.Exclude = _exclude;

		Godot.Collections.Array<Godot.Collections.Dictionary> hits =
			_player.GetWorld3D().DirectSpaceState.IntersectShape(query, 16);

		Hitbox best = null;
		float bestLateral = float.MaxValue;
		float bestDistance = float.MaxValue;

		foreach (Godot.Collections.Dictionary hit in hits)
		{
			if (hit["collider"].As<GodotObject>() is not Hitbox hitbox)
				continue;

			Vector3 point = hitbox.GlobalPosition;
			Vector3 toPoint = point - origin;
			float distance = toPoint.Length();
			if (distance > wallDistance + 0.2f || distance > Data.Range)
				continue;

			if (Mathf.RadToDeg(forward.AngleTo(toPoint.Normalized())) > Data.ConeDegrees * 0.5f)
				continue;

			// 先看离准心有多近（瞄准哪打哪），再看远近
			float lateral = (toPoint - forward * toPoint.Dot(forward)).Length();
			if (lateral < bestLateral - 0.05f || (Mathf.Abs(lateral - bestLateral) <= 0.05f && distance < bestDistance))
			{
				best = hitbox;
				bestLateral = lateral;
				bestDistance = distance;
			}
		}

		if (best == null)
		{
			Resolved?.Invoke(Vector3.Zero, false);
			return;
		}

		Vector3 hitPoint = best.GlobalPosition;
		best.ApplyDamage(Data.Damage, hitPoint, -forward);
		Resolved?.Invoke(hitPoint, true);
	}
}
