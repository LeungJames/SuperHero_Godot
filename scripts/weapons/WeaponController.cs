using Godot;
using System;
using System.Collections.Generic;

/// <summary>换弹关键帧（与动作同帧触发）。</summary>
public enum ReloadCue
{
	MagOut,
	Seat,
	Slide,
}

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

	/// <summary>刀槽的第一人称模型（含手部与 Body / Hand 枢轴）。</summary>
	[Export] public PackedScene KnifeViewmodel { get; set; }

	/// <summary>刀握柄在视角模型空间的落点；刀模以这里为旋转轴心。</summary>
	[Export] public Vector3 KnifeGripOffset { get; set; } = new(0.05f, -0.08f, 0.12f);

	/// <summary>弹匣 / 备弹变化（HUD 订阅）。</summary>
	public event Action<int, int> AmmoChanged;

	/// <summary>每次真正打出一发后的解算结果（霰弹枪每颗弹丸各一次）。</summary>
	public event Action<HitscanHit> ShotResolved;

	/// <summary>开始换弹（HUD 用）。</summary>
	public event Action ReloadStarted;

	/// <summary>换弹关键帧：撤匣 / 顶到位 / 套筒复进，音效只在这几帧响。</summary>
	public event Action<ReloadCue> ReloadCuePlayed;

	/// <summary>空仓扣扳机。</summary>
	public event Action DryFired;

	/// <summary>弹匣打空且 0.5s 内没有换弹（评级"空仓次数"，§6.2）。</summary>
	public event Action EmptyMagFired;

	/// <summary>切换武器完成（模型与数值已生效）。</summary>
	public event Action<WeaponData> WeaponChanged;

	/// <summary>刀槽状态变化：true = 当前持刀，false = 当前持枪（HUD 切换显示）。</summary>
	public event Action<bool> KnifeStateChanged;

	private enum SwapPhase
	{
		Idle,
		Lowering,
		Raising,
	}

	/// <summary>当前武器数值；切枪时为正在收枪的那把。</summary>
	public WeaponData Data => _weapons.Count > 0 ? _weapons[Mathf.Clamp(ActiveIndex, 0, _weapons.Count - 1)] : null;

	public int ActiveIndex { get; private set; }
	public int WeaponCount => _weapons.Count + (_hasKnife ? 1 : 0);

	/// <summary>刀所在槽位；未携带刀时为 -1。刀固定排在枪之后（第二格）。</summary>
	public int KnifeIndex => _hasKnife ? _weapons.Count : -1;

	/// <summary>当前是否切换到刀：开火 / 换弹 / 开镜停用，左键改走近战挥砍。</summary>
	public bool IsKnifeActive => _hasKnife && ActiveIndex >= _weapons.Count;

	public bool IsSwapping => _phase != SwapPhase.Idle;
	public int MagAmmo => _mags != null && ActiveIndex < _mags.Length ? _mags[ActiveIndex] : 0;
	public int ReserveAmmo => _reserves != null && ActiveIndex < _reserves.Length ? _reserves[ActiveIndex] : 0;
	public bool IsReloading { get; private set; }
	public bool IsAiming { get; private set; }

	/// <summary>A5：刀格按住右键格挡窗口是否生效。</summary>
	public bool IsBlocking => _blockHeld && _blockWindow > 0f;

	/// <summary>当前散布角（度），准星按它张开（§7.2）。</summary>
	public float CurrentSpreadDegrees => Data != null ? GetSpreadDegrees() : 0f;

	/// <summary>近战挥动姿势 0~1，由近战系统写入；只影响第一人称持枪位移。</summary>
	public float MeleeSwing { get; set; }

	/// <summary>换弹进度 0~1，未换弹时为 0。</summary>
	public float ReloadProgress => IsReloading && Data != null
		? Mathf.Clamp(1f - _reloadTimer / Mathf.Max(ReloadDuration, 0.001f), 0f, 1f)
		: 0f;

	/// <summary>W1 后的实际弹匣容量（向上取整）。</summary>
	private int EffectiveMagazineSize =>
		Data != null ? Mathf.CeilToInt(Data.MagazineSize * TalentEffects.MagazineScale) : 0;

	/// <summary>W2 后的实际换弹时长。</summary>
	private float ReloadDuration =>
		Data != null ? Mathf.Max(Data.ReloadTime * TalentEffects.ReloadScale, 0.01f) : 0.01f;

	/// <summary>W3 后的切枪时长倍率。</summary>
	private float SwapDurationScale => TalentEffects.SwapScale;

	private const float MuzzleFlashEnergy = 1.2f;
	private const int MaxSlots = 5;

	private readonly List<WeaponData> _weapons = new();
	private bool _hasKnife;
	private int[] _mags = System.Array.Empty<int>();
	private int[] _reserves = System.Array.Empty<int>();

	private PlayerController _player;
	private PlayerCamera _cameraOwner;
	private Camera3D _camera;
	private PlayerHealth _health;
	private MeleeController _melee;
	private Marker3D _muzzle;
	private OmniLight3D _muzzleLight;
	private Godot.Collections.Array<Rid> _exclude;

	private Vector3 _basePosition;
	private Vector3 _baseRotationDegrees;
	private Vector3 _adsPosition;
	private Vector3 _adsRotationDegrees;

	private float _cooldown;
	private float _reloadTimer;
	private float _aimBlend;
	private float _flashTimer;
	private float _kickBack;
	private float _kickPitch;
	private float _recoilStack;
	private float _sinceLastShot;
	private float _swayPhase;
	private bool _fireQueued;
	private bool _emptyMagPending;
	private float _emptyMagTimer;
	private float _swapSpreadTimer;
	private float _blockWindow;
	private float _blockCooldown;
	private bool _blockHeld;
	private int _hitStreak;

	private SwapPhase _phase;
	private float _swapTimer;
	private int _swapTarget;

	private Node3D _model;
	private Node3D _knifeModel;
	private float _knifeIdlePhase;
	private float _knifeBlockPose;
	private Node3D _slidePivot;
	private Node3D _magPivot;
	private Node3D _handLPivot;
	private Node3D _handRPivot;
	private Vector3 _slideRest;
	private Vector3 _magRest;
	private Vector3 _handLRest;
	private Vector3 _handLRestRotation;
	private Vector3 _handRRest;
	private Vector3 _handRRestRotation;
	private Basis _gripRotationBasis = Basis.Identity;
	private Vector3 _gripContactLocal;
	private bool _magOutCued;
	private bool _seatCued;
	private bool _slideCued;
	private Vector3 _magPos;
	private float _magTilt;
	private float _slidePose;
	private float _handPose;
	private float _handRPose;

	private bool CanAct => _player != null && _player.InputEnabled && _health != null && !_health.IsDead && !IsSwapping;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("../..");
		_cameraOwner = GetNode<PlayerCamera>("../../..");
		_player = GetNode<PlayerController>("../../../..");
		_health = _player.GetNode<PlayerHealth>("Health");
		_melee = _player.GetNodeOrNull<MeleeController>("Melee");
		_muzzle = GetNodeOrNull<Marker3D>("Muzzle");
		_muzzleLight = GetNodeOrNull<OmniLight3D>("MuzzleLight");

		_basePosition = Position;
		_baseRotationDegrees = RotationDegrees;
		// 开镜时把枪摆到相机正前下方；长枪可在 WeaponData 里追加偏移，避免枪身挡住准星
		_adsPosition = new Vector3(0f, _basePosition.Y + 0.10f, _basePosition.Z + 0.06f);
		_adsRotationDegrees = _baseRotationDegrees;

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
		KnifeStateChanged?.Invoke(IsKnifeActive);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_player == null || !_player.InputEnabled || _health == null || _health.IsDead)
			return;

		// 半自动每次按键只触发一次；全自动在 _Process 里按住连发
		if (@event.IsActionPressed("fire"))
		{
			if (IsKnifeActive)
				_melee?.TrySwing();
			else if (Data is { Automatic: false })
				TryFire();
		}

		if (@event.IsActionPressed("reload") && !IsKnifeActive)
			StartReload();

		// A5：只有刀格右键才是格挡；枪格右键继续走 UpdateAim 开镜
		if (@event.IsActionPressed("aim") && IsKnifeActive)
		{
			_blockHeld = true;
			TryBlock();
		}
		else if (@event.IsActionReleased("aim"))
		{
			_blockHeld = false;
		}

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

		// 空仓计时：0.5s 内换弹则取消，否则记一次空仓
		if (_emptyMagPending)
		{
			_emptyMagTimer -= dt;
			if (_emptyMagTimer <= 0f)
			{
				_emptyMagPending = false;
				EmptyMagFired?.Invoke();
			}
		}

		_sinceLastShot += dt;
		_swapSpreadTimer = Mathf.Max(0f, _swapSpreadTimer - dt);
		_blockWindow = Mathf.Max(0f, _blockWindow - dt);
		_blockCooldown = Mathf.Max(0f, _blockCooldown - dt);
		if (_sinceLastShot > Data.RecoilStackResetTime)
			_recoilStack = 0f;

		UpdateSwap(dt);
		UpdateReload(dt);
		UpdateAim(dt);
		UpdateMuzzleFlash(dt);
		UpdateViewmodel(dt);
		UpdateKnifeViewmodel(dt);
		UpdateReloadBones(dt);
		UpdateMoveSpeed();

		if (!IsKnifeActive && Data.Automatic && CanAct && !IsReloading && Input.IsActionPressed("fire"))
			TryFire();

		TryAutoReload();

		// 半自动：换弹完成时扳机仍按住，补一发，不用松手重按
		if (_fireQueued && !IsKnifeActive && CanAct && !IsReloading && _cooldown <= 0f)
		{
			_fireQueued = false;
			TryFire();
		}
	}

	/// <summary>
	/// 弹匣打空自动换弹：有备弹才进；备弹为 0 时保持空仓，只由开火时的空仓提示音反馈。
	/// 换弹时长、移速与射击限制沿用各枪自己的配置。
	/// </summary>
	private void TryAutoReload()
	{
		if (IsKnifeActive || IsReloading || !CanAct || Data == null || MagAmmo > 0 || ReserveAmmo <= 0)
			return;

		StartReload();
	}

	/// <summary>开火入口。返回是否真的打出一发。</summary>
	public bool TryFire()
	{
		if (IsKnifeActive || !CanAct || Data == null || IsReloading || _cooldown > 0f)
			return false;

		// M6：滑铲中开火需要点出"滑铲延长"
		if (_player != null && _player.IsSliding && !TalentEffects.SlideExtended)
			return false;

		if (MagAmmo <= 0)
		{
			// 空仓：锁住射速并交给 AudioDirector 播"咔"
			_cooldown = Mathf.Max(1f / Data.ShotsPerSecond, Data.DryFireCooldown);
			DryFired?.Invoke();
			return false;
		}

		_mags[ActiveIndex]--;
		AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);

		if (MagAmmo == 0)
		{
			_emptyMagPending = true;
			_emptyMagTimer = 0.5f;
		}

		_cooldown = 1f / Data.ShotsPerSecond;
		_sinceLastShot = 0f;

		Fire();
		return true;
	}

	public bool StartReload()
	{
		if (IsKnifeActive || !CanAct || Data == null || IsReloading)
			return false;

		if (MagAmmo >= EffectiveMagazineSize || ReserveAmmo <= 0)
			return false;

		IsReloading = true;
		IsAiming = false;
		_emptyMagPending = false;
		_reloadTimer = ReloadDuration;
		_magOutCued = false;
		_seatCued = false;
		_slideCued = false;
		ReloadStarted?.Invoke();
		return true;
	}

	/// <summary>弹药包调用；备弹已满返回 false，弹药包留在原地（§4.6）。</summary>
	public bool TryAddAmmo(int rounds)
	{
		if (Data == null || _weapons.Count == 0)
			return false;

		// 刀不消耗也不接收弹药：始终补给最后一把枪
		int slot = Mathf.Clamp(ActiveIndex, 0, _weapons.Count - 1);
		if (_reserves[slot] >= Data.MaxReserve)
			return false;

		_reserves[slot] = Mathf.Min(Data.MaxReserve, _reserves[slot] + rounds);
		if (!IsKnifeActive)
			AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
		return true;
	}

	// ------------------------------------------------------------ 切枪

	/// <summary>章节结算前把未到 0.5s 的空仓计入（避免最后一发刚好在胜利时被漏掉）。</summary>
	public void FlushEmptyMag()
	{
		if (!_emptyMagPending)
			return;

		_emptyMagPending = false;
		EmptyMagFired?.Invoke();
	}

	/// <summary>切换到第 index 把可用武器（收枪 → 换模型 → 抬枪）。</summary>
	public bool Equip(int index)
	{
		if (Arsenal == null || index < 0 || index >= WeaponCount)
			return false;
		if (index == ActiveIndex || IsSwapping || !CanAct)
			return false;

		_swapTarget = index;
		_swapTimer = 0f;
		_phase = SwapPhase.Lowering;
		IsAiming = false;
		_fireQueued = false;
		_blockHeld = false;
		_blockWindow = 0f;
		CancelReload();
		return true;
	}

	/// <summary>
	/// 装配本章武器：有合法待选组合时按选择装配（刀固定第二格），
	/// 否则沿用章节默认（UnlockChapter 过滤）。港口恒为 P-9 + 刀，跨章武器不生效。
	/// </summary>
	private void BuildArsenal()
	{
		_weapons.Clear();
		_hasKnife = false;

		string chapterId = LoadoutSystem.ChapterIdFor(Arsenal.CurrentChapter);
		string[] loadout = LoadoutSystem.ResolveForChapter(chapterId);
		bool assembled = false;

		foreach (string id in loadout)
		{
			if (id == LoadoutSystem.KnifeId)
			{
				_hasKnife = true;
				continue;
			}

			WeaponData weapon = LoadoutSystem.FindWeapon(id);
			if (weapon == null || _weapons.Contains(weapon))
				continue;

			_weapons.Add(weapon);
			assembled = true;
		}

		if (!assembled)
		{
			foreach (WeaponData weapon in Arsenal.Weapons)
			{
				if (weapon != null && weapon.UnlockChapter <= Arsenal.CurrentChapter)
					_weapons.Add(weapon);
			}
		}

		// 港口固定多带一把刀（决策：任何入口进入港口恒为 P-9 + 刀）
		if (LoadoutSystem.ChapterNumber(chapterId) == 0)
			_hasKnife = true;

		_mags = new int[_weapons.Count];
		_reserves = new int[_weapons.Count];
		for (int i = 0; i < _weapons.Count; i++)
		{
			_mags[i] = Mathf.CeilToInt(_weapons[i].MagazineSize * TalentEffects.MagazineScale);
			_reserves[i] = _weapons[i].StartingReserve;
		}
	}

	/// <summary>切到刀：隐藏枪模型与枪口灯，换成刀的第一人称模型（§5.2）。</summary>
	private void HideWeaponModel()
	{
		if (_model != null)
			_model.Visible = false;
		if (_muzzleLight != null)
			_muzzleLight.Visible = false;
		_flashTimer = 0f;
	}

	/// <summary>刀模与手部：整组绕握柄旋转，挥砍 / 格挡都只动这一层。</summary>
	private void ShowKnifeModel()
	{
		if (_knifeModel == null)
		{
			if (KnifeViewmodel == null)
			{
				GD.PushWarning("WeaponController: 没有配置刀视角模型。");
				return;
			}

			if (KnifeViewmodel.Instantiate() is not Node3D visual)
			{
				GD.PushWarning("WeaponController: 刀视角模型不是 Node3D。");
				return;
			}

			var root = new Node3D { Name = "KnifeModel", Position = KnifeGripOffset };
			visual.Name = "Visual";
			// 模型本身按绝对握点摆放；根节点回到握柄，旋转才是绕手转而不是绕相机转
			visual.Position = -KnifeGripOffset;
			ApplyToonMaterials(visual);
			visual.AddChild(new ModelOutline { Overlay = ViewmodelOutline() });
			root.AddChild(visual);
			AddChild(root);
			_knifeModel = root;
			_knifeIdlePhase = 0f;
		}

		_knifeModel.Visible = true;
	}

	private void HideKnifeModel()
	{
		if (_knifeModel != null)
			_knifeModel.Visible = false;
	}

	private void UpdateSwap(float dt)
	{
		if (_phase == SwapPhase.Idle)
			return;

		float duration = Mathf.Max(
			(_phase == SwapPhase.Lowering ? Arsenal.SwapLowerTime : Arsenal.SwapRaiseTime) * SwapDurationScale, 0.01f);
		_swapTimer += dt;

		if (_phase == SwapPhase.Lowering && _swapTimer >= duration)
		{
			ActiveIndex = _swapTarget;

			if (IsKnifeActive)
			{
				HideWeaponModel();
				ShowKnifeModel();
			}
			else
			{
				HideKnifeModel();
				BuildModel(Data);
				ApplyData();
				_swapSpreadTimer = TalentEffects.SwapSpreadWindow;
				AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);
				WeaponChanged?.Invoke(Data);
			}

			KnifeStateChanged?.Invoke(IsKnifeActive);
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
			return Mathf.SmoothStep(0f, 1f, Mathf.Clamp(
				_swapTimer / Mathf.Max(Arsenal.SwapLowerTime * SwapDurationScale, 0.01f), 0f, 1f));
		if (_phase == SwapPhase.Raising)
			return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp(
				_swapTimer / Mathf.Max(Arsenal.SwapRaiseTime * SwapDurationScale, 0.01f), 0f, 1f));
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
			// 先摘出树再释放：同帧内新模型才能稳定占用 "Model" 这个名字，
			// 否则旧节点尚未真正删除，新节点会被引擎改名为 Model2 / Model3，外部按名查找会落空。
			RemoveChild(_model);
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
		_handRPivot = FindNode<Node3D>(model, "HandRPivot");

		if (_slidePivot != null)
			_slideRest = _slidePivot.Position;
		if (_magPivot != null)
			_magRest = _magPivot.Position;
		if (_handLPivot != null)
		{
			_handLRest = _handLPivot.Position;
			_handLRestRotation = _handLPivot.Rotation;
			Node3D contact = FindNode<Node3D>(_handLPivot, "GripContactL");
			_gripContactLocal = contact != null ? _handLPivot.ToLocal(contact.GlobalPosition) : Vector3.Zero;
		}
		if (_handRPivot != null)
		{
			_handRRest = _handRPivot.Position;
			_handRRestRotation = _handRPivot.Rotation;
		}

		// 抓握姿态相对弹匣的旋转偏移（两枢轴同为模型根的子节点，同一空间）
		Vector3 gripDegrees = Data != null ? Data.ReloadGripRotationDegrees : Vector3.Zero;
		_gripRotationBasis = Basis.FromEuler(new Vector3(
			Mathf.DegToRad(gripDegrees.X),
			Mathf.DegToRad(gripDegrees.Y),
			Mathf.DegToRad(gripDegrees.Z)));
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
		_outlineMaterial.SetShaderParameter("width", 0.00085f);
		return _outlineMaterial;
	}

	/// <summary>
	/// 逐 surface 把视角模型的纯色材质换成 toon 平涂，保留原模型的配色分件。
	/// 只在武器开启 ToonViewmodel 时调用，避免改动已定稿的其他枪。
	/// </summary>
	private static void ApplyToonMaterials(Node root)
	{
		ComicMaterials.Apply(root);
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
		_hitStreak = 0;
		_cameraOwner.RecoilRecovery = Data.RecoilRecovery * TalentEffects.RecoilRecoveryScale;
		_cameraOwner.SetAdsFov(Data.AdsFov);

		// 开镜姿势按武器数据重算（狙击枪需要把枪身推远，避免枪托顶住画面中心）
		_adsPosition = new Vector3(0f, -0.052f, _basePosition.Z + 0.06f) + Data.AdsPositionOffset;
		_adsRotationDegrees = _baseRotationDegrees + Data.AdsRotationDegrees;

		if (_muzzle != null)
			_muzzle.Position = Data.MuzzleOffset;
		if (_muzzleLight != null)
			_muzzleLight.Position = Data.MuzzleOffset + new Vector3(0f, 0f, -0.02f);
	}

	// ------------------------------------------------------------ 射击

	/// <summary>A5：刀格右键开启 0.25s 格挡窗口，CD 6s；枪格不受影响。</summary>
	private void TryBlock()
	{
		if (!IsKnifeActive || IsSwapping || !TalentEffects.CanBlock)
			return;
		if (_blockWindow > 0f || _blockCooldown > 0f)
			return;

		_blockWindow = TalentEffects.BlockWindow;
		_blockCooldown = TalentEffects.BlockCooldown;
	}

	/// <summary>W8 只覆盖 P-9 与 AR-5；全自动 ST-9、霰弹枪与狙击枪不享受。</summary>
	private bool IsBurstEligible => Data != null && (Data.Id == "p9" || Data.Id == "ar");

	private void Fire()
	{
		Vector3 origin = _camera.GlobalPosition;
		Vector3 muzzle = _muzzle != null ? _muzzle.GlobalPosition : origin;
		int pellets = Mathf.Max(1, Data.PelletsPerShot);

		// W8：连续命中 3 发后的下一发 +30%，按每枪一次的节奏结算
		float burstScale = 1f;
		if (TalentEffects.BurstFire && IsBurstEligible && _hitStreak >= TalentEffects.BurstHitCount)
		{
			burstScale = TalentEffects.BurstDamageScale;
			_hitStreak = 0;
		}

		for (int i = 0; i < pellets; i++)
		{
			Vector3 direction = HitscanResolver.ApplySpread(-_camera.GlobalTransform.Basis.Z, GetSpreadDegrees());

			if (TalentEffects.ArmorPierce)
			{
				FirePiercing(origin, muzzle, direction, burstScale);
				continue;
			}

			HitscanHit hit = HitscanResolver.Resolve(_player.GetWorld3D(), origin, direction, Data, _exclude, burstScale);

			// 视觉弹道从枪口出发，打向相机射线的命中点（§4.4）
			Vector3 endPoint = hit.HasHit ? hit.Point : origin + direction * Data.MaxRange;
			SpawnTracer(muzzle, endPoint);
			if (hit.HasHit)
				SpawnImpact(hit);

			ShotResolved?.Invoke(hit);
			_hitStreak = hit.HasHit && hit.Collider is Hitbox ? _hitStreak + 1 : 0;

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

	/// <summary>
	/// W5：穿甲弹沿射线穿过薄掩体与 1 名额外的敌人。
	/// 统计口径只认首个敌人命中（避免一枪两杀把命中率刷过 100%），其余命中只结算伤害与表现。
	/// </summary>
	private void FirePiercing(Vector3 origin, Vector3 muzzle, Vector3 direction, float burstScale)
	{
		System.Collections.Generic.List<HitscanHit> hits = HitscanResolver.ResolvePiercing(
			_player.GetWorld3D(), origin, direction, Data, _exclude, burstScale);

		if (hits.Count == 0)
		{
			SpawnTracer(muzzle, origin + direction * Data.MaxRange);
			ShotResolved?.Invoke(HitscanHit.Miss);
			_hitStreak = 0;
			return;
		}

		HitscanHit primary = hits[0];
		foreach (HitscanHit hit in hits)
		{
			SpawnImpact(hit);
			if (hit.Collider is IBulletReceiver receiver)
				receiver.OnBulletHit(hit);
			if (hit.Collider is Hitbox && primary.Collider is not Hitbox)
				primary = hit;
		}

		SpawnTracer(muzzle, hits[^1].Point);
		ShotResolved?.Invoke(primary);
		_hitStreak = primary.Collider is Hitbox ? _hitStreak + 1 : 0;
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

		// W3：切枪后的短暂首发散布减免
		if (_swapSpreadTimer > 0f)
			spread *= TalentEffects.SwapSpreadScale;

		return spread;
	}

	private void ApplyRecoil()
	{
		// 连射累加：首发 ×1.0，每发 +10%，上限 ×2.0（§4.2）
		float multiplier = 1f + _recoilStack;
		if (IsAiming)
			multiplier *= Data.AdsRecoilMultiplier;

		float pitch = Data.RecoilPitch * multiplier * TalentEffects.RecoilScale;
		float yaw = (float)GD.RandRange(-Data.RecoilYaw, Data.RecoilYaw) * multiplier * TalentEffects.RecoilScale;
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
		int needed = EffectiveMagazineSize - MagAmmo;
		int taken = Mathf.Min(needed, ReserveAmmo);
		_mags[ActiveIndex] += taken;
		_reserves[ActiveIndex] -= taken;
		AmmoChanged?.Invoke(MagAmmo, ReserveAmmo);

		if (!Data.Automatic && Input.IsActionPressed("fire"))
			_fireQueued = true;
	}

	private void UpdateAim(float dt)
	{
		bool wantAim = CanAct && !IsKnifeActive && !IsReloading && Input.IsActionPressed("aim");
		IsAiming = wantAim;

		float step = Data.AdsTime > 0f ? dt / (Data.AdsTime * TalentEffects.AdsTimeScale) : 1f;
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

		if (IsKnifeActive)
			_player.MoveSpeedMultiplier = 1f;
		else if (IsReloading)
			_player.MoveSpeedMultiplier = Data.ReloadMoveMultiplier;
		else if (IsAiming)
			_player.MoveSpeedMultiplier = TalentEffects.AdsMaster
				? Mathf.Max(Data.AdsMoveMultiplier, TalentEffects.AdsMoveScale)
				: Data.AdsMoveMultiplier;
		else
			_player.MoveSpeedMultiplier = 1f;
	}

	/// <summary>程序化持枪位移：行走摆动 + 后坐回弹 + 换弹下沉 + 切枪收放（§4.7）。</summary>
	private void UpdateViewmodel(float dt)
	{
		_kickBack = Mathf.MoveToward(_kickBack, 0f, dt / Mathf.Max(Data.FireKickRecoverTime, 0.001f));
		_kickPitch = Mathf.MoveToward(_kickPitch, 0f, dt / Mathf.Max(Data.FireKickPitchRecoverTime, 0.001f));

		float reloadPose = 0f;
		float impactDip = 0f;
		float impactPitch = 0f;
		float impactRoll = 0f;
		if (IsReloading && ReloadDuration > 0f)
		{
			float progress = Mathf.Clamp(1f - _reloadTimer / ReloadDuration, 0f, 1f);
			// 抬枪 + 向内滚转：把弹匣井翻到画面里；进 / 出都是斜坡，中段保持住
			reloadPose = PoseWindow(progress);

			// 换弹到位冲击：顶匣、复进各给一记短促下沉 + 回弹，重枪幅度更大
			float weight = Mathf.Clamp(ReloadDuration / 2f, 0.9f, 1.4f);
			float seat = ImpactPulse((progress - Data.ReloadMagInsertEnd) * ReloadDuration);
			float slide = ImpactPulse((progress - Data.ReloadSlideEnd) * ReloadDuration);
			float yank = ImpactPulse((progress - Data.ReloadMagDropEnd) * ReloadDuration);
			impactDip = -Data.ReloadImpactDip * (seat + slide * 0.55f + yank * 0.35f) * weight;
			impactPitch = Data.ReloadImpactPitchDegrees * (seat + slide * 0.5f + yank * 0.3f) * weight;
			impactRoll = Data.ReloadImpactRollDegrees * (seat - slide * 0.4f);
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
		float swing = Mathf.Clamp(IsKnifeActive ? 0f : MeleeSwing, 0f, 1f);
		float swap = Arsenal != null ? SwapBlend() : 0f;
		Position = basePosition + sway
			+ new Vector3(0f, Data.ReloadPoseLift * reloadPose + impactDip, Data.FireKickBack * _kickBack)
			+ new Vector3(-0.04f * swing, -0.02f * swing, 0.10f * swing)
			+ new Vector3(0f, -(Arsenal?.SwapLowerDrop ?? 0f) * swap, 0f);
		RotationDegrees = _baseRotationDegrees.Lerp(_adsRotationDegrees, _aimBlend)
			+ new Vector3(
				Data.FireKickPitch * _kickPitch - 34f * swing + impactPitch + Data.ReloadPosePitchDegrees * reloadPose,
				16f * swing,
				Data.ReloadPoseRollDegrees * reloadPose + impactRoll)
			+ new Vector3(-28f * swap, 0f, 0f);
	}

	/// <summary>
	/// 刀模动作：待机轻微呼吸摆动，挥砍沿握柄轴心向前下方抡出并回收；
	/// 格挡时稍微抬起护住正面。只动 KnifeModel 这一层，与枪的换弹 / 后坐互不影响。
	/// </summary>
	private void UpdateKnifeViewmodel(float dt)
	{
		if (_knifeModel == null || !_knifeModel.Visible)
			return;

		_knifeIdlePhase += dt;
		_knifeBlockPose = Mathf.MoveToward(_knifeBlockPose, IsBlocking ? 1f : 0f, dt * 12f);

		float idleX = Mathf.Sin(_knifeIdlePhase * 1.7f) * 0.004f;
		float idleY = Mathf.Cos(_knifeIdlePhase * 2.3f) * 0.005f;

		// 出刀快、收刀慢，中段保留一点惯性
		float swing = Mathf.Clamp(MeleeSwing, 0f, 1f);
		float arc = swing * swing * (3f - 2f * swing);

		Vector3 position = KnifeGripOffset + new Vector3(-0.035f, 0.035f, -0.035f)
			+ new Vector3(idleX, idleY, 0f)
			+ new Vector3(-0.03f * arc, 0.10f * arc, -0.04f * arc)
			+ new Vector3(0f, 0.018f * _knifeBlockPose, -0.008f * _knifeBlockPose);

		Vector3 rotation = new(
			-14f * arc - 9f * _knifeBlockPose,
			10f * arc - 16f * _knifeBlockPose,
			55f * arc);

		_knifeModel.Position = position;
		_knifeModel.RotationDegrees = rotation;
	}

	/// <summary>
	/// 换弹表演：左手从护木滑到弹匣抓握点，抓握期间位置与朝向直接由弹匣当前变换推算（恒定抓握偏移）；
	/// 拔匣加速甩出 + 惯性回落，插匣末段加速顶到位，套筒快拉快放。
	/// 只在换弹进度里求值，取消 / 完成时平滑归零，不会卡在半途。
	/// </summary>
	private void UpdateReloadBones(float dt)
	{
		if (Data == null || _slidePivot == null || _magPivot == null || _handLPivot == null || _handRPivot == null)
			return;

		float t = IsReloading && ReloadDuration > 0f
			? Mathf.Clamp(1f - _reloadTimer / ReloadDuration, 0f, 1f)
			: 0f;

		if (IsReloading)
			FireReloadCues(t);

		Vector3 outPoint = new(-Data.ReloadMagLateral, Data.ReloadMagVerticalOffset, Data.ReloadMagPullBack);
		Vector3 magPos = Vector3.Zero;
		float magTilt = 0f;

		if (IsReloading)
		{
			float dropStart = Data.ReloadMagDropStart;
			float dropEnd = Data.ReloadMagDropEnd;
			float insertStart = Data.ReloadMagInsertStart;
			float insertEnd = Data.ReloadMagInsertEnd;
			float overshoot = 1f + Data.ReloadMagOutOvershoot;

			if (t < dropStart)
			{
				magPos = Vector3.Zero;
			}
			else if (t < dropEnd)
			{
				// 拔匣：起步蓄力、末段甩出（x^1.8 让最后 20% 走完约 40% 位移）
				float x = Mathf.InverseLerp(dropStart, dropEnd, t);
				float pull = Mathf.Pow(x, 1.8f) * overshoot;
				magPos = outPoint * pull;
				magTilt = pull;
			}
			else if (t < insertStart)
			{
				// 惯性残留：冲过脱出点后回落，手一直握着匣
				float g = Mathf.InverseLerp(dropEnd, insertStart, t);
				float settle = 1f + Data.ReloadMagOutOvershoot * (1f - g) * (1f - g);
				magPos = outPoint * settle;
				magTilt = settle;
			}
			else
			{
				// 插匣：末段加速，最后 12% 一记顶到位；到位即锁死在弹匣井，之后不再位移
				float x = Mathf.Clamp(Mathf.InverseLerp(insertStart, insertEnd, t), 0f, 1f);
				float insert = x < 0.88f
					? 0.94f * Mathf.Pow(x / 0.88f, 2f)
					: 0.94f + 0.06f * Mathf.Pow((x - 0.88f) / 0.12f, 1.6f);
				insert = Mathf.Min(insert, 1f);
				magPos = outPoint * (1f - insert);
				magTilt = 1f - insert;
			}

			// 换弹中直接吃解析值，硬停顿感不被平滑抹掉
			_magPos = magPos;
			_magTilt = magTilt;
		}
		else
		{
			float k = 1f - Mathf.Exp(-Data.ReloadBoneBlendSpeed * dt);
			_magPos = _magPos.Lerp(Vector3.Zero, k);
			_magTilt = Mathf.Lerp(_magTilt, 0f, k);
		}

		Basis magBasis = Basis.FromEuler(new Vector3(
			Mathf.DegToRad(Data.ReloadMagTiltDegrees) * _magTilt,
			0f,
			Mathf.DegToRad(Data.ReloadMagTiltDegrees * 0.55f) * _magTilt));

		Vector3 magOrigin = _magRest + _magPos;
		_magPivot.Transform = new Transform3D(magBasis, magOrigin);

		// 套筒：快拉到底 → 短停 → 加速复进（复进末端即撞击帧）
		float slideTarget = 0f;
		if (IsReloading)
		{
			float a = Data.ReloadSlideStart;
			float b = Data.ReloadSlideEnd;
			if (t >= a && t < b && b > a)
			{
				float x = Mathf.InverseLerp(a, b, t);
				if (x < 0.42f)
					slideTarget = Mathf.Sqrt(x / 0.42f);
				else if (x < 0.55f)
					slideTarget = 1f;
				else
					slideTarget = 1f - Mathf.Pow((x - 0.55f) / 0.45f, 1.7f);
			}
		}

		float blendK = IsReloading ? 1f : 1f - Mathf.Exp(-Data.ReloadBoneBlendSpeed * dt);
		_slidePose = Mathf.Lerp(_slidePose, slideTarget, blendK);
		_slidePivot.Position = _slideRest + new Vector3(0f, 0f, _slidePose * Data.ReloadSlideTravel);

		// 左手：抓握区间内位置与朝向都由弹匣当前变换推算，相对变换恒定
		Basis gripBasis = magBasis * _gripRotationBasis;
		Transform3D grip = new(gripBasis,
			magOrigin + magBasis * Data.ReloadGripOffset - gripBasis * _gripContactLocal);
		float bind = 0f;
		if (IsReloading)
		{
			float grabStart = Data.ReloadHandLeaveEnd;
			float grabEnd = Data.ReloadMagInsertEnd;
			if (t >= grabStart && t <= grabEnd)
				bind = 1f;
			else if (t < grabStart)
				bind = Ease(Data.ReloadHandLeaveStart, grabStart, t);
			else
				bind = 1f - Ease(grabEnd, Data.ReloadHandReturnEnd, t);
		}

		_handPose = bind;
		Transform3D restPose = new(Basis.FromEuler(_handLRestRotation), _handLRest);
		Transform3D handPose = InterpolateTransforms(restPose, grip, bind);
		if (bind > 0f && bind < 1f)
		{
			// 伸手 / 收回时给一点向下的弧线，绕开枪身
			float arc = Mathf.Sin(bind * Mathf.Pi);
			handPose.Origin += new Vector3(0f, -0.03f * arc, 0.012f * arc);
		}
		_handLPivot.Transform = handPose;

		// The trigger hand holds the frame throughout; it must not follow the cycling slide.
		_handRPivot.Position = _handRRest;
		_handRPivot.Rotation = _handRRestRotation;
	}

	/// <summary>换弹关键帧只触发一次，且与弹匣 / 套筒到达该相位的同一帧。</summary>
	private void FireReloadCues(float t)
	{
		if (!_magOutCued && t >= Data.ReloadMagDropEnd)
		{
			_magOutCued = true;
			ReloadCuePlayed?.Invoke(ReloadCue.MagOut);
		}

		if (!_seatCued && t >= Data.ReloadMagInsertEnd)
		{
			_seatCued = true;
			ReloadCuePlayed?.Invoke(ReloadCue.Seat);
			_cameraOwner?.AddShake(Data.ReloadImpactShakeDegrees, Data.ReloadImpactShakeTime);
		}

		if (!_slideCued && t >= Data.ReloadSlideEnd)
		{
			_slideCued = true;
			ReloadCuePlayed?.Invoke(ReloadCue.Slide);
			_cameraOwner?.AddShake(Data.ReloadImpactShakeDegrees * 0.7f, Data.ReloadImpactShakeTime * 0.8f);
		}
	}

	private static Transform3D InterpolateTransforms(Transform3D from, Transform3D to, float weight)
	{
		if (weight <= 0f)
			return from;
		if (weight >= 1f)
			return to;

		Quaternion rotation = from.Basis.GetRotationQuaternion().Slerp(to.Basis.GetRotationQuaternion(), weight);
		return new Transform3D(new Basis(rotation), from.Origin.Lerp(to.Origin, weight));
	}

	/// <summary>到位冲击脉冲：0 秒时为 1，随后快速衰减并带一次回弹。</summary>
	private static float ImpactPulse(float seconds)
	{
		if (seconds < 0f)
			return 0f;

		return Mathf.Exp(-seconds * 22f) * Mathf.Cos(seconds * 62f);
	}

	/// <summary>换弹姿势权重：斜坡进、中段保持、斜坡出。</summary>
	private float PoseWindow(float t)
	{
		float enter = Ease(Data.ReloadPoseInStart, Data.ReloadPoseInEnd, t);
		float leave = Ease(Data.ReloadPoseOutStart, Data.ReloadPoseOutEnd, t);
		return enter * (1f - leave);
	}

	private void ResetPivots()
	{
		_slidePivot = null;
		_magPivot = null;
		_handLPivot = null;
		_handRPivot = null;
		_handLRestRotation = Vector3.Zero;
		_handRRestRotation = Vector3.Zero;
		_gripRotationBasis = Basis.Identity;
		_gripContactLocal = Vector3.Zero;
		_magPos = Vector3.Zero;
		_magTilt = 0f;
		_slidePose = 0f;
		_handPose = 0f;
		_handRPose = 0f;
		_magOutCued = false;
		_seatCued = false;
		_slideCued = false;
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
