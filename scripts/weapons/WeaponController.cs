using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 枪械控制器：把 <see cref="WeaponData"/> 描述的数值变成开火 / 换弹 / 散布 / 后坐 / 程序化持枪位移，
/// 并按 <see cref="ArsenalData"/> 做章节解锁与切枪（设计文档 §4、§5.2、§9.3）。
/// 同一实例在切枪时只替换视角模型，弹药与订阅关系按武器分别保存，外部无需重连事件。
/// </summary>
public partial class WeaponController : Node3D
{
	[Export] public ArsenalData Arsenal { get; set; }
	[Export] public PackedScene TracerScene { get; set; }
	[Export] public PackedScene ImpactScene { get; set; }

	/// <summary>弹匣 / 备弹变化（HUD 订阅）。</summary>
	public event Action<int, int> AmmoChanged;

	/// <summary>每次真正打出一发后的解算结果（霰弹枪每颗弹丸各一次）。</summary>
	public event Action<HitscanHit> ShotResolved;

	/// <summary>开始换弹（音效与 HUD 用）。</summary>
	public event Action ReloadStarted;

	/// <summary>空仓扣扳机。</summary>
	public event Action DryFired;

	/// <summary>切换武器完成（模型与数值已生效）。</summary>
	public event Action<WeaponData> WeaponChanged;

	private enum SwapPhase
	{
		Idle,
		Lowering,
		Raising,
	}

	/// <summary>当前武器数值；切枪时为正在收枪的那把。</summary>
	public WeaponData Data => _weapons.Count > 0 ? _weapons[Mathf.Clamp(ActiveIndex, 0, _weapons.Count - 1)] : null;

	public int ActiveIndex { get; private set; }
	public int WeaponCount => _weapons.Count;
	public bool IsSwapping => _phase != SwapPhase.Idle;
	public int MagAmmo => _mags != null && ActiveIndex < _mags.Length ? _mags[ActiveIndex] : 0;
	public int ReserveAmmo => _reserves != null && ActiveIndex < _reserves.Length ? _reserves[ActiveIndex] : 0;
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
	private const int MaxSlots = 5;

	private readonly List<WeaponData> _weapons = new();
	private int[] _mags = System.Array.Empty<int>();
	private int[] _reserves = System.Array.Empty<int>();

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

	private SwapPhase _phase;
	private float _swapTimer;
	private int _swapTarget;

	private Node3D _model;
	private Node3D _slidePivot;
	private Node3D _magPivot;
	private Node3D _handLPivot;
	private Vector3 _slideRest;
	private Vector3 _magRest;
	private Vector3 _magPos;
	private float _magScale = 1f;
	private float _magTilt;
	private float _slidePose;
	private float _handPose;

	private bool CanAct => _player != null && _player.InputEnabled && _health != null && !_health.IsDead && !IsSwapping;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("../..");
		_cameraOwner = GetNode<PlayerCamera>("../../..");
		_player = GetNode<PlayerController>("../../../..");
		_health = _player.GetNode<PlayerHealth>("Health");
		_muzzle = GetNodeOrNull<Marker3D>("Muzzle");
		_muzzleLight = GetNodeOrNull<OmniLight3D>("MuzzleLight");

		_basePosition = Position;
		_baseRotationDegrees = RotationDegrees;
		// 开镜时把枪摆到相机正前下方；两套姿势都由代码插值，场景里只存腰射基准姿势
		_adsPosition = new Vector3(0f, _basePosition.Y + 0.10f, _basePosition.Z + 0.06f);

		_exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };

		if (Arsenal == null)
		{
			GD.PushError("WeaponController: 没有配置 ArsenalData。");
			SetProcess(false);
			return;
		}

		BuildArsenal();

		if (_weapons.Count == 0)
		{
			GD.PushWarning($"WeaponController: 当前章节 {Arsenal.CurrentChapter} 没有可用武器。");
			SetProcess(false);
			return;
		}

		ActiveIndex = 0;
		BuildModel(Data);
		ApplyData();

		AddToGroup("weapon");
		AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
		WeaponChanged?.Invoke(Data);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_player == null || !_player.InputEnabled || _health == null || _health.IsDead)
			return;

		// 半自动每次按键只触发一次；全自动在 _Process 里按住连发
		if (@event.IsActionPressed("fire") && Data is { Automatic: false })
			TryFire();

		if (@event.IsActionPressed("reload"))
			StartReload();

		if (@event.IsActionPressed("weapon_next"))
			Equip((ActiveIndex + 1) % Mathf.Max(WeaponCount, 1));
		else if (@event.IsActionPressed("weapon_prev"))
			Equip((ActiveIndex - 1 + WeaponCount) % Mathf.Max(WeaponCount, 1));

		for (int slot = 0; slot < MaxSlots; slot++)
			if (@event.IsActionPressed($"weapon_{slot + 1}"))
				Equip(slot);
	}

	public override void _Process(double delta)
	{
		if (Data == null)
			return;

		float dt = (float)delta;

		_cooldown = Mathf.Max(0f, _cooldown - dt);
		_sinceLastShot += dt;
		if (_sinceLastShot > Data.RecoilStackResetTime)
			_recoilStack = 0f;

		UpdateSwap(dt);
		UpdateReload(dt);
		UpdateAim(dt);
		UpdateMuzzleFlash(dt);
		UpdateViewmodel(dt);
		UpdateReloadBones(dt);
		UpdateMoveSpeed();

		if (Data.Automatic && CanAct && !IsReloading && Input.IsActionPressed("fire"))
			TryFire();
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

		_mags[ActiveIndex]--;
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

		_reserves[ActiveIndex] = Mathf.Min(Data.MaxReserve, ReserveAmmo + rounds);
		AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
		return true;
	}

	// ------------------------------------------------------------ 切枪

	/// <summary>切换到第 index 把可用武器（收枪 → 换模型 → 抬枪）。</summary>
	public bool Equip(int index)
	{
		if (Arsenal == null || index < 0 || index >= _weapons.Count)
			return false;
		if (index == ActiveIndex || IsSwapping || !CanAct)
			return false;

		_swapTarget = index;
		_swapTimer = 0f;
		_phase = SwapPhase.Lowering;
		IsAiming = false;
		CancelReload();
		return true;
	}

	private void BuildArsenal()
	{
		_weapons.Clear();
		foreach (WeaponData weapon in Arsenal.Weapons)
		{
			if (weapon == null || weapon.UnlockChapter > Arsenal.CurrentChapter)
				continue;
			_weapons.Add(weapon);
		}

		_mags = new int[_weapons.Count];
		_reserves = new int[_weapons.Count];
		for (int i = 0; i < _weapons.Count; i++)
		{
			_mags[i] = _weapons[i].MagazineSize;
			_reserves[i] = _weapons[i].StartingReserve;
		}
	}

	private void UpdateSwap(float dt)
	{
		if (_phase == SwapPhase.Idle)
			return;

		float duration = Mathf.Max(_phase == SwapPhase.Lowering ? Arsenal.SwapLowerTime : Arsenal.SwapRaiseTime, 0.01f);
		_swapTimer += dt;

		if (_phase == SwapPhase.Lowering && _swapTimer >= duration)
		{
			ActiveIndex = _swapTarget;
			BuildModel(Data);
			ApplyData();
			AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
			WeaponChanged?.Invoke(Data);
			_phase = SwapPhase.Raising;
			_swapTimer = 0f;
		}
		else if (_phase == SwapPhase.Raising && _swapTimer >= duration)
		{
			_phase = SwapPhase.Idle;
			_swapTimer = 0f;
		}
	}

	/// <summary>切枪的收 / 抬姿势权重 0~1（1 = 完全收起）。</summary>
	private float SwapBlend()
	{
		if (_phase == SwapPhase.Lowering)
			return Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_swapTimer / Mathf.Max(Arsenal.SwapLowerTime, 0.01f), 0f, 1f));
		if (_phase == SwapPhase.Raising)
			return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_swapTimer / Mathf.Max(Arsenal.SwapRaiseTime, 0.01f), 0f, 1f));
		return 0f;
	}

	private void CancelReload()
	{
		IsReloading = false;
		_reloadTimer = 0f;
	}

	// ------------------------------------------------------------ 模型

	/// <summary>实例化视角模型并挂描边；枢轴按名字查找（Slide / Mag / HandL）。</summary>
	private void BuildModel(WeaponData data)
	{
		ResetPivots();

		if (_model != null)
		{
			_model.QueueFree();
			_model = null;
		}

		if (data?.Viewmodel == null)
		{
			GD.PushWarning($"WeaponController: '{data?.DisplayName}' 没有配置视角模型。");
			return;
		}

		if (data.Viewmodel.Instantiate() is not Node3D model)
		{
			GD.PushWarning($"WeaponController: '{data.DisplayName}' 的 Viewmodel 不是 Node3D。");
			return;
		}

		model.Name = "Model";
		model.AddChild(new ModelOutline { Overlay = ViewmodelOutline() });
		AddChild(model);
		_model = model;

		if (data.ToonViewmodel)
			ApplyToonMaterials(model);

		_slidePivot = FindNode<Node3D>(model, "SlidePivot");
		_magPivot = FindNode<Node3D>(model, "MagPivot");
		_handLPivot = FindNode<Node3D>(model, "HandLPivot");

		if (_slidePivot != null)
			_slideRest = _slidePivot.Position;
		if (_magPivot != null)
			_magRest = _magPivot.Position;
	}

	/// <summary>视角模型描边叠层（反向壳），全局共用一个材质。</summary>
	private static ShaderMaterial _outlineMaterial;
	private static ShaderMaterial ViewmodelOutline()
	{
		if (_outlineMaterial != null)
			return _outlineMaterial;

		_outlineMaterial = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/outline.gdshader"),
		};
		_outlineMaterial.SetShaderParameter("width", 0.0025f);
		return _outlineMaterial;
	}

	/// <summary>
	/// 逐 surface 把视角模型的纯色材质换成 toon 平涂，保留原模型的配色分件。
	/// 只在武器开启 ToonViewmodel 时调用，避免改动已定稿的其他枪。
	/// </summary>
	private static void ApplyToonMaterials(Node root)
	{
		if (root is MeshInstance3D mesh && mesh.Mesh != null)
		{
			for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
			{
				Material source = mesh.GetActiveMaterial(i);
				Color color = source is StandardMaterial3D standard ? standard.AlbedoColor : new Color(0.8f, 0.8f, 0.8f);
				mesh.SetSurfaceOverrideMaterial(i, ToonMaterial(color));
			}
		}

		foreach (Node child in root.GetChildren())
			ApplyToonMaterials(child);
	}

	private static readonly System.Collections.Generic.Dictionary<Color, ShaderMaterial> ToonCache = new();

	private static ShaderMaterial ToonMaterial(Color color)
	{
		if (ToonCache.TryGetValue(color, out ShaderMaterial cached))
			return cached;

		var material = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/toon.gdshader"),
		};
		material.SetShaderParameter("albedo_color", color);
		ToonCache[color] = material;
		return material;
	}

	/// <summary>把当前武器的数值同步到相机与枪口标记。</summary>
	private void ApplyData()
	{
		_cameraOwner.RecoilRecovery = Data.RecoilRecovery;
		_cameraOwner.SetAdsFov(Data.AdsFov);

		if (_muzzle != null)
			_muzzle.Position = Data.MuzzleOffset;
		if (_muzzleLight != null)
			_muzzleLight.Position = Data.MuzzleOffset + new Vector3(0f, 0f, -0.02f);
	}

	// ------------------------------------------------------------ 射击

	private void Fire()
	{
		Vector3 origin = _camera.GlobalPosition;
		Vector3 muzzle = _muzzle != null ? _muzzle.GlobalPosition : origin;
		int pellets = Mathf.Max(1, Data.PelletsPerShot);

		for (int i = 0; i < pellets; i++)
		{
			Vector3 direction = HitscanResolver.ApplySpread(-_camera.GlobalTransform.Basis.Z, GetSpreadDegrees());
			HitscanHit hit = HitscanResolver.Resolve(_player.GetWorld3D(), origin, direction, Data, _exclude);

			// 视觉弹道从枪口出发，打向相机射线的命中点（§4.4）
			Vector3 endPoint = hit.HasHit ? hit.Point : origin + direction * Data.MaxRange;
			SpawnTracer(muzzle, endPoint);
			if (hit.HasHit)
				SpawnImpact(hit);

			ShotResolved?.Invoke(hit);

			if (hit.HasHit && hit.Collider is IBulletReceiver receiver)
				receiver.OnBulletHit(hit);
		}

		// 枪声让 30m 内的敌人进入 Alert（§5.4）
		NoiseEmitter.Emit(GlobalPosition, NoiseKind.Gunshot);

		ApplyRecoil();
		_kickBack = 1f;
		_kickPitch = 1f;
		_flashTimer = Data.MuzzleFlashTime;
		if (_muzzleLight != null)
			_muzzleLight.Visible = true;
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
		_mags[ActiveIndex] += taken;
		_reserves[ActiveIndex] -= taken;
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

	/// <summary>程序化持枪位移：行走摆动 + 后坐回弹 + 换弹下沉 + 切枪收放（§4.7）。</summary>
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
		float swap = Arsenal != null ? SwapBlend() : 0f;
		Position = basePosition + sway
			+ new Vector3(0f, -Data.ReloadDrop * reloadPose, Data.FireKickBack * _kickBack)
			+ new Vector3(-0.04f * swing, -0.02f * swing, 0.10f * swing)
			+ new Vector3(0f, -(Arsenal?.SwapLowerDrop ?? 0f) * swap, 0f);
		RotationDegrees = _baseRotationDegrees
			+ new Vector3(Data.FireKickPitch * _kickPitch - 34f * swing, 16f * swing, Data.ReloadRoll * reloadPose)
			+ new Vector3(-28f * swap, 0f, 0f);
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

		float slideTarget = 0f;
		float handTarget = 0f;

		// 弹匣按"空弹匣拔走 → 丢掉 → 新弹匣从下方推回"三段驱动，
		// 位置与缩放分开缓存，丢掉的那段缩到 0 藏住模型跳变。
		Vector3 magPosTarget = Vector3.Zero;
		float magScaleTarget = 1f;
		float magTiltTarget = 0f;

		Vector3 outOffset = new(-Data.ReloadMagLateral, Data.ReloadMagRise, Data.ReloadMagPullBack);
		Vector3 inOffset = new(-Data.ReloadMagLateral * 0.9f, -Data.ReloadMagInsertDrop, Data.ReloadMagPullBack * 0.5f);

		if (IsReloading && Data.ReloadTime > 0f)
		{
			float t = Mathf.Clamp(1f - _reloadTimer / Data.ReloadTime, 0f, 1f);

			if (t < Data.ReloadMagDropEnd)
			{
				// 空弹匣离开握把：向屏幕中心抬出并前倾，保证整只弹匣落在画面内
				float phase = Ease(Data.ReloadMagDropStart, Data.ReloadMagDropEnd, t);
				magPosTarget = outOffset * phase;
				magTiltTarget = phase;
			}
			else if (t < Data.ReloadMagInsertStart)
			{
				// 空弹匣被继续带走并淡出，为"换上的新弹匣"腾出干净的一拍
				float phase = Ease(Data.ReloadMagDropEnd, Data.ReloadMagInsertStart, t);
				magPosTarget = outOffset * (1f + 0.3f * phase);
				magTiltTarget = 1f;
				magScaleTarget = 1f - phase;
			}
			else
			{
				// 新弹匣从握把下方升起推到位，起始位置由不可见（scale 0）遮住跳变
				float phase = Ease(Data.ReloadMagInsertStart, Data.ReloadMagInsertEnd, t);
				magPosTarget = inOffset * (1f - phase);
				magTiltTarget = (1f - phase) * 0.4f;
				magScaleTarget = Mathf.Clamp(phase * 3f, 0f, 1f);
			}

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
		_slidePose = Mathf.Lerp(_slidePose, slideTarget, k);
		_handPose = Mathf.Lerp(_handPose, handTarget, k);

		if (IsReloading)
		{
			// 换弹中直接吃目标值：阶段切换处的跳变由 scale 0 遮住，
			// 平滑会把它拉成一段穿模的长位移
			_magPos = magPosTarget;
			_magScale = magScaleTarget;
			_magTilt = magTiltTarget;
		}
		else
		{
			// 取消 / 结束时平滑回位，保证不会卡在半途
			_magPos = _magPos.Lerp(Vector3.Zero, k);
			_magScale = Mathf.Lerp(_magScale, 1f, k);
			_magTilt = Mathf.Lerp(_magTilt, 0f, k);
		}

		if (_magPivot != null)
		{
			_magPivot.Position = _magRest + _magPos;
			_magPivot.Rotation = new Vector3(
				Mathf.DegToRad(Data.ReloadMagTiltDegrees) * _magTilt,
				0f,
				Mathf.DegToRad(Data.ReloadMagTiltDegrees * 0.6f) * _magTilt);
			_magPivot.Scale = Vector3.One * Mathf.Max(_magScale, 0.001f);
		}

		if (_slidePivot != null)
			_slidePivot.Position = _slideRest + new Vector3(0f, 0f, _slidePose * Data.ReloadSlideTravel);

		if (_handLPivot != null)
			_handLPivot.Rotation = new Vector3(Mathf.DegToRad(Data.ReloadHandDropDegrees) * _handPose, 0f, 0f);
	}

	private void ResetPivots()
	{
		_slidePivot = null;
		_magPivot = null;
		_handLPivot = null;
		_magPos = Vector3.Zero;
		_magScale = 1f;
		_magTilt = 0f;
		_slidePose = 0f;
		_handPose = 0f;
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
			GD.PushWarning("WeaponController: TracerScene 不是 Tracer。");
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
			GD.PushWarning("WeaponController: ImpactScene 不是 ImpactEffect。");
			return;
		}

		GetTree().CurrentScene?.AddChild(impact);
		impact.Play(hit.Point, hit.Normal);
	}
}
