using Godot;
using System;

/// <summary>
/// 手枪 P-9：半自动开火门控、ADS、换弹、散布与后坐、程序化持枪位移。
/// 不做移动、不做切枪（设计文档 §4、§9.3）。
/// </summary>
public partial class PistolController : Node3D
{
	[Export] public PistolData Data { get; set; }
	[Export] public PackedScene TracerScene { get; set; }
	[Export] public PackedScene ImpactScene { get; set; }

	/// <summary>弹匣 / 备弹变化（HUD 在 M5 订阅）。</summary>
	public event Action<int, int> AmmoChanged;

	/// <summary>每次真正打出一发后的解算结果（命中标记与统计用）。</summary>
	public event Action<HitscanHit> ShotResolved;

	/// <summary>开始换弹（音效与 HUD 用）。</summary>
	public event Action ReloadStarted;

	/// <summary>空仓扣扳机。</summary>
	public event Action DryFired;

	public int MagAmmo { get; private set; }
	public int ReserveAmmo { get; private set; }
	public bool IsReloading { get; private set; }
	public bool IsAiming { get; private set; }

	/// <summary>当前散布角（度），准星按它张开（§7.2）。</summary>
	public float CurrentSpreadDegrees => Data != null ? GetSpreadDegrees() : 0f;

	/// <summary>近战挥动姿势 0~1，由近战系统写入；只影响第一人称持枪位移。</summary>
	public float MeleeSwing { get; set; }

	/// <summary>换弹进度 0~1，未换弹时为 0。</summary>
	public float ReloadProgress => IsReloading && Data != null
		? Mathf.Clamp(1f - _reloadTimer / Mathf.Max(Data.ReloadTime, 0.001f), 0f, 1f)
		: 0f;

	private const float MuzzleFlashEnergy = 1.2f;

	private PlayerController _player;
	private PlayerCamera _cameraOwner;
	private Camera3D _camera;
	private PlayerHealth _health;
	private Marker3D _muzzle;
	private OmniLight3D _muzzleLight;
	private Godot.Collections.Array<Rid> _exclude;

	private Vector3 _basePosition;
	private Vector3 _baseRotationDegrees;
	private Vector3 _adsPosition;

	private float _cooldown;
	private float _reloadTimer;
	private float _aimBlend;
	private float _flashTimer;
	private float _kickBack;
	private float _kickPitch;
	private float _recoilStack;
	private float _sinceLastShot;
	private float _swayPhase;

	private Node3D _slidePivot;
	private Node3D _magPivot;
	private Node3D _handLPivot;
	private Vector3 _slideRest;
	private Vector3 _magRest;
	private float _magPose;
	private float _slidePose;
	private float _handPose;

	private bool CanAct => _player != null && _player.InputEnabled && _health != null && !_health.IsDead;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("../..");
		_cameraOwner = GetNode<PlayerCamera>("../../..");
		_player = GetNode<PlayerController>("../../../..");
		_health = _player.GetNode<PlayerHealth>("Health");
		_muzzle = GetNodeOrNull<Marker3D>("Muzzle");
		_muzzleLight = GetNodeOrNull<OmniLight3D>("MuzzleLight");

		// 换弹动作由 Blender 导出的枢轴节点驱动：套筒 / 弹匣 / 左手，姿势按相位解算
		Node model = GetNodeOrNull<Node>("Model");
		_slidePivot = FindNode<Node3D>(model, "SlidePivot");
		_magPivot = FindNode<Node3D>(model, "MagPivot");
		_handLPivot = FindNode<Node3D>(model, "HandLPivot");
		if (_slidePivot != null)
			_slideRest = _slidePivot.Position;
		if (_magPivot != null)
			_magRest = _magPivot.Position;

		if (Data == null)
		{
			GD.PushError("PistolController: 没有配置 PistolData。");
			return;
		}

		_basePosition = Position;
		_baseRotationDegrees = RotationDegrees;
		// 开镜时把枪摆到相机正前下方；两套姿势都由代码插值，场景里只存腰射基准姿势
		_adsPosition = new Vector3(0f, _basePosition.Y + 0.10f, _basePosition.Z + 0.06f);

		MagAmmo = Data.MagazineSize;
		ReserveAmmo = Data.StartingReserve;

		_cameraOwner.RecoilRecovery = Data.RecoilRecovery;
		_exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };

		AddToGroup("pistol");
		AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!CanAct)
			return;

		// 半自动：每次物理按键只触发一次，连点仍受射速门控限制
		if (@event.IsActionPressed("fire"))
			TryFire();

		if (@event.IsActionPressed("reload"))
			StartReload();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		_cooldown = Mathf.Max(0f, _cooldown - dt);
		_sinceLastShot += dt;
		if (_sinceLastShot > Data.RecoilStackResetTime)
			_recoilStack = 0f;

		UpdateReload(dt);
		UpdateAim(dt);
		UpdateMuzzleFlash(dt);
		UpdateViewmodel(dt);
		UpdateReloadBones(dt);
		UpdateMoveSpeed();
	}

	/// <summary>开火入口。返回是否真的打出一发。</summary>
	public bool TryFire()
	{
		if (!CanAct || Data == null || IsReloading || _cooldown > 0f)
			return false;

		if (MagAmmo <= 0)
		{
			// 空仓：锁住射速并交给 AudioDirector 播"咔"
			_cooldown = 1f / Data.ShotsPerSecond;
			DryFired?.Invoke();
			return false;
		}

		MagAmmo--;
		AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
		_cooldown = 1f / Data.ShotsPerSecond;
		_sinceLastShot = 0f;

		Fire();
		return true;
	}

	public bool StartReload()
	{
		if (!CanAct || Data == null || IsReloading)
			return false;

		if (MagAmmo >= Data.MagazineSize || ReserveAmmo <= 0)
			return false;

		IsReloading = true;
		IsAiming = false;
		_reloadTimer = Data.ReloadTime;
		ReloadStarted?.Invoke();
		return true;
	}

	/// <summary>弹药包调用；备弹已满返回 false，弹药包留在原地（§4.6）。</summary>
	public bool TryAddAmmo(int rounds)
	{
		if (Data == null || ReserveAmmo >= Data.MaxReserve)
			return false;

		ReserveAmmo = Mathf.Min(Data.MaxReserve, ReserveAmmo + rounds);
		AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
		return true;
	}

	private void Fire()
	{
		Vector3 origin = _camera.GlobalPosition;
		Vector3 direction = -_camera.GlobalTransform.Basis.Z;
		direction = HitscanResolver.ApplySpread(direction, GetSpreadDegrees());

		HitscanHit hit = HitscanResolver.Resolve(_player.GetWorld3D(), origin, direction, Data, _exclude);

		// 枪声让 30m 内的敌人进入 Alert（§5.4）
		NoiseEmitter.Emit(GlobalPosition, NoiseKind.Gunshot);

		// 视觉弹道从枪口出发，打向相机射线的命中点（§4.4）
		Vector3 muzzle = _muzzle != null ? _muzzle.GlobalPosition : origin;
		Vector3 endPoint = hit.HasHit ? hit.Point : origin + direction * Data.MaxRange;
		SpawnTracer(muzzle, endPoint);
		if (hit.HasHit)
			SpawnImpact(hit);

		ApplyRecoil();
		_kickBack = 1f;
		_kickPitch = 1f;
		_flashTimer = Data.MuzzleFlashTime;
		if (_muzzleLight != null)
			_muzzleLight.Visible = true;

		ShotResolved?.Invoke(hit);

		if (hit.HasHit && hit.Collider is IBulletReceiver receiver)
			receiver.OnBulletHit(hit);
	}

	private float GetSpreadDegrees()
	{
		float spread = IsAiming ? Data.AdsSpread : Data.HipSpread;

		Vector3 horizontal = new(_player.Velocity.X, 0f, _player.Velocity.Z);
		if (horizontal.Length() > 0.3f)
			spread += Data.MovingSpreadPenalty;

		if (!_player.IsOnFloor())
			spread += Data.AirSpreadPenalty;

		if (_player.IsSliding)
			spread += Data.SlideSpreadPenalty;

		return spread;
	}

	private void ApplyRecoil()
	{
		// 连射累加：首发 ×1.0，每发 +10%，上限 ×2.0（§4.2）
		float multiplier = 1f + _recoilStack;
		if (IsAiming)
			multiplier *= Data.AdsRecoilMultiplier;

		float pitch = Data.RecoilPitch * multiplier;
		float yaw = (float)GD.RandRange(-Data.RecoilYaw, Data.RecoilYaw) * multiplier;
		_cameraOwner.AddRecoil(pitch, yaw);

		_recoilStack = Mathf.Min(_recoilStack + Data.RecoilStackPerShot, Data.RecoilStackMax - 1f);
	}

	private void UpdateReload(float dt)
	{
		if (!IsReloading)
			return;

		_reloadTimer -= dt;
		if (_reloadTimer > 0f)
			return;

		// 换弹完成时才扣备弹（§4.6）
		IsReloading = false;
		int needed = Data.MagazineSize - MagAmmo;
		int taken = Mathf.Min(needed, ReserveAmmo);
		MagAmmo += taken;
		ReserveAmmo -= taken;
		AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
	}

	private void UpdateAim(float dt)
	{
		bool wantAim = CanAct && !IsReloading && Input.IsActionPressed("aim");
		IsAiming = wantAim;

		float step = Data.AdsTime > 0f ? dt / Data.AdsTime : 1f;
		_aimBlend = Mathf.MoveToward(_aimBlend, wantAim ? 1f : 0f, step);
		_cameraOwner.SetAimBlend(_aimBlend);
	}

	private void UpdateMuzzleFlash(float dt)
	{
		if (_flashTimer <= 0f)
			return;

		_flashTimer -= dt;
		if (_flashTimer <= 0f && _muzzleLight != null)
			_muzzleLight.Visible = false;
	}

	private void UpdateMoveSpeed()
	{
		if (_player == null || Data == null)
			return;

		if (IsReloading)
			_player.MoveSpeedMultiplier = Data.ReloadMoveMultiplier;
		else if (IsAiming)
			_player.MoveSpeedMultiplier = Data.AdsMoveMultiplier;
		else
			_player.MoveSpeedMultiplier = 1f;
	}

	/// <summary>程序化持枪位移：行走摆动 + 后坐回弹 + 换弹下沉（§4.7）。</summary>
	private void UpdateViewmodel(float dt)
	{
		_kickBack = Mathf.MoveToward(_kickBack, 0f, dt / Mathf.Max(Data.FireKickRecoverTime, 0.001f));
		_kickPitch = Mathf.MoveToward(_kickPitch, 0f, dt / Mathf.Max(Data.FireKickPitchRecoverTime, 0.001f));

		float reloadPose = 0f;
		if (IsReloading && Data.ReloadTime > 0f)
		{
			float progress = Mathf.Clamp(1f - _reloadTimer / Data.ReloadTime, 0f, 1f);
			reloadPose = Mathf.Sin(progress * Mathf.Pi);
		}

		Vector3 horizontal = new(_player.Velocity.X, 0f, _player.Velocity.Z);
		float speedRatio = Mathf.Clamp(horizontal.Length() / 4.8f, 0f, 1.5f);
		float swayScale = Mathf.Lerp(1f, Data.AdsSwayScale, _aimBlend);
		float swayRate = Mathf.Lerp(1f, Data.AdsSwayRateScale, _aimBlend);
		_swayPhase += dt * Mathf.Tau * 2f * swayRate * Mathf.Max(speedRatio, 0.15f);

		Vector3 sway = new(
			Mathf.Sin(_swayPhase) * Data.SwayAmountX * swayScale * speedRatio,
			Mathf.Cos(_swayPhase * 2f) * Data.SwayAmountY * swayScale * speedRatio,
			0f);

		Vector3 basePosition = _basePosition.Lerp(_adsPosition, _aimBlend);
		float swing = Mathf.Clamp(MeleeSwing, 0f, 1f);
		Position = basePosition + sway
			+ new Vector3(0f, -Data.ReloadDrop * reloadPose, Data.FireKickBack * _kickBack)
			+ new Vector3(-0.04f * swing, -0.02f * swing, 0.10f * swing);
		RotationDegrees = _baseRotationDegrees
			+ new Vector3(Data.FireKickPitch * _kickPitch - 34f * swing, 16f * swing, Data.ReloadRoll * reloadPose);
	}

	/// <summary>
	/// 换弹动作：弹匣掉落 → 新弹匣插入 → 拉套筒复进，左手同时离开再回到握把。
	/// 姿势只由换弹进度决定，并用指数跟随平滑；换弹结束或被打断时目标归零，
	/// 因此任何时刻取消都会连续回到普通持枪姿势，不会卡在半途。
	/// </summary>
	private void UpdateReloadBones(float dt)
	{
		if (Data == null || _slidePivot == null || _magPivot == null || _handLPivot == null)
			return;

		float magTarget = 0f;
		float slideTarget = 0f;
		float handTarget = 0f;

		if (IsReloading && Data.ReloadTime > 0f)
		{
			float t = Mathf.Clamp(1f - _reloadTimer / Data.ReloadTime, 0f, 1f);

			// 弹匣：掉出 → 空窗 → 插回（插回末段带一点过冲）
			if (t < Data.ReloadMagDropEnd)
				magTarget = Ease(Data.ReloadMagDropStart, Data.ReloadMagDropEnd, t);
			else if (t < Data.ReloadMagInsertStart)
				magTarget = 1f;
			else
				magTarget = 1f - Ease(Data.ReloadMagInsertStart, Data.ReloadMagInsertEnd, t);

			// 套筒：拉到底再瞬间复进（复进段更短，读作弹簧回位）
			if (t >= Data.ReloadSlideStart && t < Data.ReloadSlideEnd)
			{
				float mid = (Data.ReloadSlideStart + Data.ReloadSlideEnd) * 0.5f;
				slideTarget = t < mid
					? Ease(Data.ReloadSlideStart, mid, t)
					: 1f - Ease(mid, Data.ReloadSlideEnd, t);
			}

			// 左手：垂下让出握把 → 换弹末段回到握把准备拉套筒
			if (t < Data.ReloadHandLeaveStart)
				handTarget = 0f;
			else if (t < Data.ReloadHandLeaveEnd)
				handTarget = Ease(Data.ReloadHandLeaveStart, Data.ReloadHandLeaveEnd, t);
			else if (t < Data.ReloadHandReturnStart)
				handTarget = 1f;
			else
				handTarget = 1f - Ease(Data.ReloadHandReturnStart, Data.ReloadHandReturnEnd, t);
		}

		float k = 1f - Mathf.Exp(-Data.ReloadBoneBlendSpeed * dt);
		_magPose = Mathf.Lerp(_magPose, magTarget, k);
		_slidePose = Mathf.Lerp(_slidePose, slideTarget, k);
		_handPose = Mathf.Lerp(_handPose, handTarget, k);

		if (_magPivot != null)
			_magPivot.Position = _magRest + new Vector3(0f, -_magPose * Data.ReloadMagDropDistance, 0f);

		if (_slidePivot != null)
			_slidePivot.Position = _slideRest + new Vector3(0f, 0f, _slidePose * Data.ReloadSlideTravel);

		if (_handLPivot != null)
			_handLPivot.Rotation = new Vector3(Mathf.DegToRad(Data.ReloadHandDropDegrees) * _handPose, 0f, 0f);
	}

	/// <summary>把 [from,to] 映射成 0~1 的平滑值。</summary>
	private static float Ease(float from, float to, float t)
	{
		if (to <= from)
			return t >= to ? 1f : 0f;
		return Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - from) / (to - from), 0f, 1f));
	}

	private static T FindNode<T>(Node root, string name) where T : Node
	{
		if (root == null)
			return null;
		if (root.Name == name && root is T typed)
			return typed;
		foreach (Node child in root.GetChildren())
		{
			T found = FindNode<T>(child, name);
			if (found != null)
				return found;
		}
		return null;
	}

	private void SpawnTracer(Vector3 from, Vector3 to)
	{
		if (TracerScene == null)
			return;

		if (TracerScene.Instantiate() is not Tracer tracer)
		{
			GD.PushWarning("PistolController: TracerScene 不是 Tracer。");
			return;
		}

		GetTree().CurrentScene?.AddChild(tracer);
		tracer.Fire(from, to);
	}

	private void SpawnImpact(HitscanHit hit)
	{
		if (ImpactScene == null)
			return;

		if (ImpactScene.Instantiate() is not ImpactEffect impact)
		{
			GD.PushWarning("PistolController: ImpactScene 不是 ImpactEffect。");
			return;
		}

		GetTree().CurrentScene?.AddChild(impact);
		impact.Play(hit.Point, hit.Normal);
	}
}
