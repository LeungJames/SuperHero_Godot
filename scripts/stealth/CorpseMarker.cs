using Godot;
using System.Collections.Generic;

/// <summary>
/// 尸体标记（2.0 设计 §3.5）：暗杀 / 处决留下，4m 内敌人可见，迟疑 3s 后提升警戒；
/// 玩家可花 1.5s 藏尸，藏好后不再参与判定。只做判定与提示，不改敌人 AI。
/// </summary>
public partial class CorpseMarker : Node3D
{
	private const uint WorldMask = 1;

	public bool IsHidden { get; private set; }
	public bool IsFound { get; private set; }
	public EnemyAgent Source { get; private set; }
	public StealthConfig Config { get; private set; }

	private float _noticeTimer;

	public void Setup(Vector3 position, EnemyAgent source, StealthConfig config)
	{
		GlobalPosition = position;
		Source = source;
		Config = config;
		AddToGroup("corpse");
		BuildVisual();
	}

	/// <summary>敌人发现计时。返回 true 表示刚好到达迟疑时间，由 StealthDirector 提升警戒。</summary>
	public bool TickNotice(float dt, IReadOnlyList<EnemyAgent> enemies)
	{
		if (IsHidden || IsFound || Config == null)
			return false;

		foreach (EnemyAgent enemy in enemies)
		{
			if (enemy == null || !IsInstanceValid(enemy) || enemy.IsDead)
				continue;

			if (FlatDistance(enemy.GlobalPosition, GlobalPosition) > Config.CorpseVisibleRadius)
				continue;

			Vector3 eye = enemy.GlobalPosition + Vector3.Up * 1.5f;
			Vector3 target = GlobalPosition + Vector3.Up * 0.35f;
			if (!HasLineOfSight(enemy, eye, target))
				continue;

			_noticeTimer += dt;
			if (_noticeTimer < Config.CorpseNoticeDelay)
				return false;

			IsFound = true;
			return true;
		}

		// 没人在看时缓缓忘记，避免远远瞥一眼就永久记住
		_noticeTimer = Mathf.Max(0f, _noticeTimer - dt * 2f);
		return false;
	}

	/// <summary>藏尸完成：隐藏外观并退出尸体判定。</summary>
	public void HideCorpse()
	{
		IsHidden = true;
		Visible = false;
		RemoveFromGroup("corpse");
	}

	private void BuildVisual()
	{
		var mesh = new MeshInstance3D { Name = "Body" };
		var capsule = new CapsuleMesh
		{
			Radius = 0.26f,
			Height = 1.65f,
			RadialSegments = 8,
			Rings = 2,
		};
		mesh.Mesh = capsule;
		mesh.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.17f, 0.14f, 0.13f),
			Roughness = 0.95f,
		};
		// 躺平：绕 X 转 90°，再抬到半身高度
		mesh.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
		mesh.Position = new Vector3(0f, 0.26f, 0f);
		AddChild(mesh);
	}

	private bool HasLineOfSight(EnemyAgent enemy, Vector3 from, Vector3 to)
	{
		// 射线从敌人眼睛出发，必须排除敌人自己的身体，否则永远打在自己身上
		var exclude = new Godot.Collections.Array<Rid> { enemy.GetRid() };
		PhysicsRayQueryParameters3D query =
			PhysicsRayQueryParameters3D.Create(from, to, WorldMask, exclude);
		return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
	}

	private static float FlatDistance(Vector3 a, Vector3 b) =>
		new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
