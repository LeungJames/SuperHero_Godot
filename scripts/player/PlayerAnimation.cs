using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 玩家全身动画驱动（A 阶段）。
///
/// 状态机骨架由 Skeleton3D + AnimationTree 驱动，状态覆盖
/// idle / 走 / 跑 / 跳起 / 落地 / 瞄准 / 开火 / 换弹 / 受击 / 死亡（外加滑铲）。
/// 第一人称手臂与相机程序化位移不在这里，仍由 WeaponController / PlayerCamera 叠加。
///
/// 素材：BodyModel 下挂 LOD0/1/2 三个 glb 实例，骨架与节点名一致、各自带 10 个动作。
/// 交替使用三个 AnimationTree，播放名与 <see cref="PlayerAnimData"/> 的契约一致；
/// 走路 / 跑步按实际移速改写 AnimationNodeAnimation.TimelineLength，消除滑步。
/// </summary>
public partial class PlayerAnimation : Node
{
	[Export] public PlayerAnimData Data { get; set; }

	/// <summary>全身模型根节点，其子节点按顺序视为 LOD0 / LOD1 / LOD2。</summary>
	[Export] public Node3D BodyModel { get; set; }

	private enum BodyState
	{
		Idle,
		Walk,
		Run,
		Jump,
		Land,
		Aim,
		Fire,
		Reload,
		Hit,
		Death,
		Slide,
	}

	private sealed class Layer
	{
		public Node3D Root;
		public Skeleton3D Skeleton;
		public AnimationPlayer Player;
		public AnimationTree Tree;
		public AnimationNodeStateMachinePlayback Playback;
		public readonly Dictionary<string, AnimationNodeAnimation> Clips = new();
	}

	private PlayerController _player;
	private WeaponController _weapon;
	private PlayerHealth _health;
	private readonly List<Layer> _layers = new();

	private BodyState _state = (BodyState)(-1);
	private bool _ready;
	private bool _dead;
	private bool _airborne;
	private bool _layersActive;
	private float _fireTimer;
	private float _hitTimer;
	private float _landTimer;
	private float _locomotionScale = -1f;
	private float _reloadScale = -1f;

	/// <summary>当前状态使用的剪辑名（HUD / 自动化验证可读）。</summary>
	public string CurrentStateName => ClipName(_state);

	/// <summary>当前走路 / 跑步的播放速率倍率（未在移动状态时为 -1）。</summary>
	public float PlaybackScale => _locomotionScale;

	/// <summary>全身模型的视觉缩放（与 PlayerData.ModelScale 一致，1 = 素材原始身高）。</summary>
	private float ModelScale => _player?.Data != null ? Mathf.Max(_player.Data.ModelScale, 0.01f) : 1f;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		if (_player == null || Data == null)
		{
			GD.PushWarning("PlayerAnimation: 缺少 PlayerController 或 PlayerAnimData，动画关闭。");
			SetProcess(false);
			return;
		}

		if (BodyModel == null)
			BodyModel = FindBodyModel();
		if (BodyModel == null)
		{
			GD.PushWarning("PlayerAnimation: 找不到全身模型根节点，动画关闭。");
			SetProcess(false);
			return;
		}

		// 只缩外观，让可见身高与胶囊对齐；胶囊 / 相机 / 移动数值都不动
		BodyModel.Scale = Vector3.One * ModelScale;

		CollectLayers();
		if (_layers.Count == 0)
		{
			GD.PushWarning("PlayerAnimation: 模型里没有 Skeleton3D，动画关闭。");
			SetProcess(false);
			return;
		}

		foreach (Layer layer in _layers)
			PrepareLayer(layer);

		_weapon = _player.GetNodeOrNull<WeaponController>("Head/Camera3D/ViewModel/Weapon");
		_health = _player.GetNodeOrNull<PlayerHealth>("Health");

		if (_weapon != null)
			_weapon.ShotResolved += OnShotResolved;
		if (_health != null)
		{
			_health.Damaged += OnDamaged;
			_health.Died += OnDied;
		}

		_ready = true;
		SetLayersActive(BodyModel.Visible);
		Request(BodyState.Idle, true);
	}

	public override void _Process(double delta)
	{
		if (!_ready)
			return;

		float dt = (float)delta;
		_fireTimer = Mathf.Max(0f, _fireTimer - dt);
		_hitTimer = Mathf.Max(0f, _hitTimer - dt);
		_landTimer = Mathf.Max(0f, _landTimer - dt);

		bool bodyVisible = BodyModel.Visible;
		UpdateLod(bodyVisible);

		// 第一人称下全身不可见，停掉三套动画树，省掉 3 × 301 条轨道的求值。
		if (!bodyVisible)
		{
			SetLayersActive(false);
			return;
		}

		if (!_layersActive)
		{
			SetLayersActive(true);
			_state = (BodyState)(-1);
		}

		if (_dead)
			return;

		// 优先级：受击 > 换弹 > 开火 > 空中 / 落地 > 滑铲 > 瞄准 > 走跑
		if (_hitTimer > 0f)
		{
			Request(BodyState.Hit);
			return;
		}

		if (_weapon != null && _weapon.IsReloading)
		{
			Request(BodyState.Reload);
			return;
		}

		if (_fireTimer > 0f)
		{
			Request(BodyState.Fire);
			return;
		}

		if (!_player.IsOnFloor())
		{
			_airborne = true;
			Request(BodyState.Jump);
			return;
		}

		if (_airborne)
		{
			_airborne = false;
			_landTimer = Data.LandHoldTime;
		}

		if (_landTimer > 0f)
		{
			Request(BodyState.Land);
			return;
		}

		if (_player.IsSliding)
		{
			Request(BodyState.Slide);
			return;
		}

		Vector3 velocity = _player.Velocity;
		float speed = new Vector2(velocity.X, velocity.Z).Length();

		// 瞄准只用在几乎站定时：素材没有"瞄准 + 移动"的分层动作，移动中切回走跑避免脚底打滑。
		if (_weapon != null && _weapon.IsAiming && speed < 0.5f)
		{
			Request(BodyState.Aim);
			return;
		}

		if (speed > Data.RunSpeedThreshold)
			Request(BodyState.Run);
		else if (speed > Data.WalkSpeedThreshold)
			Request(BodyState.Walk);
		else
			Request(BodyState.Idle);

		UpdatePlaybackRate(speed);
	}

	// ---------------------------------------------------------------- 事件

	/// <summary>处决演出：素材没有单独的处决动作，借开火剪辑做一次短促的挥击。</summary>
	public void PlayExecutionStrike()
	{
		if (!_ready || _dead)
			return;

		_fireTimer = Data.FireHoldTime;
	}

	private void OnShotResolved(HitscanHit hit)
	{
		_fireTimer = Data.FireHoldTime;
	}

	private void OnDamaged(float amount, DeathCause cause)
	{
		_hitTimer = Data.HitHoldTime;
	}

	private void OnDied(DeathCause cause)
	{
		_dead = true;
		Request(BodyState.Death, true);
	}

	private void Request(BodyState state, bool restart = false)
	{
		if (_state == state && !restart)
			return;

		_state = state;
		StringName clip = ClipName(state);
		if (clip.IsEmpty)
			return;

		foreach (Layer layer in _layers)
		{
			if (layer.Playback == null)
				continue;
			if (restart)
				layer.Playback.Start(clip);
			else
				layer.Playback.Travel(clip);
		}

		_locomotionScale = -1f;
		_reloadScale = -1f;
	}

	private StringName ClipName(BodyState state)
	{
		return state switch
		{
			BodyState.Walk => Data.WalkClip,
			BodyState.Run => Data.RunClip,
			BodyState.Jump => Data.JumpClip,
			BodyState.Land => Data.LandClip,
			BodyState.Aim => Data.AimClip,
			BodyState.Fire => Data.FireClip,
			BodyState.Reload => Data.ReloadClip,
			BodyState.Hit => Data.HitClip,
			BodyState.Death => Data.DeathClip,
			// 素材没有滑铲剪辑时，沿用落地动作的蹲伏姿势
			BodyState.Slide => string.IsNullOrEmpty(Data.SlideClip) ? Data.LandClip : Data.SlideClip,
			_ => Data.IdleClip,
		};
	}

	/// <summary>
	/// 按实际水平速度改写走路 / 跑步剪辑的一周期时长，让脚面线速度与位移一致（消滑步）。
	/// 换弹动作按当前武器的 ReloadTime 拉伸，保证手部动作与弹药结算同时结束。
	/// </summary>
	private void UpdatePlaybackRate(float speed)
	{
		if (_state == BodyState.Walk || _state == BodyState.Run)
		{
			bool run = _state == BodyState.Run;
			// 剪辑原速是素材原始身高下的数值，模型缩放后步幅同比缩短，自然速度也要乘缩放
			float natural = (run ? Data.RunClipSpeed : Data.WalkClipSpeed) * ModelScale;
			float scale = Mathf.Clamp(speed / Mathf.Max(natural, 0.01f), Data.MinPlaybackScale, Data.MaxPlaybackScale);
			if (Mathf.Abs(scale - _locomotionScale) < 0.01f)
				return;

			_locomotionScale = scale;
			ApplyTimeline(run ? Data.RunClip : Data.WalkClip, scale);
			return;
		}

		if (_state == BodyState.Reload && _weapon?.Data != null)
		{
			float length = ClipLength(Data.ReloadClip);
			float scale = length / Mathf.Max(_weapon.Data.ReloadTime, 0.05f);
			if (Mathf.Abs(scale - _reloadScale) < 0.01f)
				return;

			_reloadScale = scale;
			ApplyTimeline(Data.ReloadClip, scale);
		}
	}

	private void ApplyTimeline(string clip, float scale)
	{
		if (string.IsNullOrEmpty(clip))
			return;

		float length = ClipLength(clip);
		if (length <= 0.01f)
			return;

		float timeline = length / Mathf.Max(scale, 0.01f);
		foreach (Layer layer in _layers)
		{
			if (layer.Clips.TryGetValue(clip, out AnimationNodeAnimation node))
				node.TimelineLength = timeline;
		}
	}

	private float ClipLength(string clip)
	{
		foreach (Layer layer in _layers)
		{
			if (layer.Player.HasAnimation(clip))
				return (float)layer.Player.GetAnimation(clip).Length;
		}
		return 0f;
	}

	// ---------------------------------------------------------------- LOD / 可见性

	/// <summary>按相机距离切换 LOD0/1/2（10m / 25m），第一人称时整体隐藏。</summary>
	private void UpdateLod(bool bodyVisible)
	{
		if (_layers.Count == 1)
		{
			_layers[0].Root.Visible = bodyVisible;
			return;
		}

		Camera3D camera = GetViewport()?.GetCamera3D();
		float distance = camera != null ? camera.GlobalPosition.DistanceTo(_player.GlobalPosition) : 0f;
		float lod0 = Data.Lod0Distance * ModelScale;
		float lod1 = Data.Lod1Distance * ModelScale;
		for (int i = 0; i < _layers.Count; i++)
		{
			bool visible = bodyVisible && (i == 0
				? distance < lod0
				: i == 1
					? distance >= lod0 && distance < lod1
					: distance >= lod1);
			_layers[i].Root.Visible = visible;
		}
	}

	private void SetLayersActive(bool active)
	{
		if (_layersActive == active)
			return;

		_layersActive = active;
		foreach (Layer layer in _layers)
		{
			if (layer.Tree != null)
				layer.Tree.Active = active;
		}
	}

	// ---------------------------------------------------------------- 初始化

	private void CollectLayers()
	{
		if (Find<Skeleton3D>(BodyModel) != null && !HasLodChildren())
		{
			AddLayer(BodyModel);
			return;
		}

		foreach (Node child in BodyModel.GetChildren())
		{
			if (child is Node3D node && Find<Skeleton3D>(node) != null)
				AddLayer(node);
		}
	}

	private bool HasLodChildren()
	{
		foreach (Node child in BodyModel.GetChildren())
		{
			if (child is Node3D node && Find<Skeleton3D>(node) != null)
				return true;
		}
		return false;
	}

	private void AddLayer(Node3D root)
	{
		_layers.Add(new Layer
		{
			Root = root,
			Skeleton = Find<Skeleton3D>(root),
			Player = Find<AnimationPlayer>(root),
		});
	}

	private void PrepareLayer(Layer layer)
	{
		if (layer.Player == null)
		{
			layer.Player = new AnimationPlayer { Name = "AnimationPlayer" };
			layer.Root.AddChild(layer.Player);
		}

		if (!HasAllClips(layer) && Data.GeneratePlaceholderClips)
			GeneratePlaceholderClips(layer);

		// glTF 不带循环标记：按素材说明把四个循环动作设成 LOOP_LINEAR。
		foreach (string clip in new[] { Data.IdleClip, Data.WalkClip, Data.RunClip, Data.AimClip })
		{
			if (string.IsNullOrEmpty(clip) || !layer.Player.HasAnimation(clip))
				continue;
			layer.Player.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
		}

		BuildStateMachine(layer);
	}

	private bool HasAllClips(Layer layer)
	{
		foreach (string clip in AllClips())
		{
			if (!string.IsNullOrEmpty(clip) && !layer.Player.HasAnimation(clip))
				return false;
		}
		return true;
	}

	private string[] AllClips()
	{
		return new[]
		{
			Data.IdleClip, Data.WalkClip, Data.RunClip, Data.JumpClip, Data.LandClip,
			Data.AimClip, Data.FireClip, Data.ReloadClip, Data.HitClip, Data.DeathClip,
			Data.SlideClip,
		};
	}

	/// <summary>
	/// 按剪辑名搭一个全连接状态机。循环状态用 AnimationNodeAnimation 的
	/// UseCustomTimeline + LoopMode 打开循环，并把一周期时长暴露给滑步修正。
	/// </summary>
	private void BuildStateMachine(Layer layer)
	{
		layer.Tree = new AnimationTree { Name = "PlayerAnimationTree" };
		layer.Root.AddChild(layer.Tree);

		var machine = new AnimationNodeStateMachine();
		string[] clips = AllClips();
		var loopClips = new HashSet<string> { Data.IdleClip, Data.WalkClip, Data.RunClip, Data.AimClip };

		foreach (string clip in clips)
		{
			if (string.IsNullOrEmpty(clip))
				continue;

			var node = new AnimationNodeAnimation
			{
				Animation = clip,
				UseCustomTimeline = true,
				StretchTimeScale = true,
				TimelineLength = layer.Player.GetAnimation(clip).Length,
				LoopMode = loopClips.Contains(clip) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None,
			};
			machine.AddNode(clip, node);
			layer.Clips[clip] = node;
		}

		foreach (string from in clips)
		{
			foreach (string to in clips)
			{
				if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to)
					continue;
				machine.AddTransition(from, to,
					new AnimationNodeStateMachineTransition { XfadeTime = Mathf.Max(Data.StateBlendTime, 0.01f) });
			}
		}

		layer.Tree.TreeRoot = machine;
		layer.Tree.AnimPlayer = layer.Tree.GetPathTo(layer.Player);
		layer.Tree.Active = true;
		layer.Playback = layer.Tree.Get("parameters/playback").As<AnimationNodeStateMachinePlayback>();
	}

	private Node3D FindBodyModel()
	{
		foreach (Node child in GetParent().GetChildren())
		{
			if (child == this || child is not Node3D node)
				continue;
			if (Find<Skeleton3D>(node) != null)
				return node;
		}
		return null;
	}

	// ---------------------------------------------------------------- 占位骨架动作

	private void GeneratePlaceholderClips(Layer layer)
	{
		var library = new AnimationLibrary();
		float hipsRest = layer.Skeleton.FindBone("hips") >= 0
			? layer.Skeleton.GetBoneRest(layer.Skeleton.FindBone("hips")).Origin.Y
			: 0.95f;

		library.AddAnimation(Data.IdleClip, BuildIdle());
		library.AddAnimation(Data.WalkClip, BuildWalk(hipsRest));
		library.AddAnimation(Data.RunClip, BuildRun(hipsRest));
		library.AddAnimation(Data.JumpClip, BuildJump());
		library.AddAnimation(Data.LandClip, BuildLand(hipsRest));
		library.AddAnimation(Data.AimClip, BuildAim());
		library.AddAnimation(Data.FireClip, BuildFire());
		library.AddAnimation(Data.ReloadClip, BuildReload());
		library.AddAnimation(Data.HitClip, BuildHit());
		library.AddAnimation(Data.DeathClip, BuildDeath(hipsRest));
		if (!string.IsNullOrEmpty(Data.SlideClip))
			library.AddAnimation(Data.SlideClip, BuildSlide(hipsRest));

		layer.Player.AddAnimationLibrary("placeholder", library);
	}

	private static Animation NewClip(float length, bool loop)
	{
		return new Animation
		{
			Length = Mathf.Max(length, 0.05f),
			LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None,
		};
	}

	private static int RotTrack(Animation clip, string bone)
	{
		int track = clip.AddTrack(Animation.TrackType.Rotation3D);
		clip.TrackSetPath(track, new NodePath("Skeleton3D:" + bone));
		return track;
	}

	private static int PosTrack(Animation clip, string bone)
	{
		int track = clip.AddTrack(Animation.TrackType.Position3D);
		clip.TrackSetPath(track, new NodePath("Skeleton3D:" + bone));
		return track;
	}

	private static Quaternion Rot(float x, float y = 0f, float z = 0f)
	{
		return Quaternion.FromEuler(new Vector3(Mathf.DegToRad(x), Mathf.DegToRad(y), Mathf.DegToRad(z)));
	}

	private static void Key(Animation clip, int track, float time, float x, float y = 0f, float z = 0f)
	{
		clip.RotationTrackInsertKey(track, time, Rot(x, y, z));
	}

	private Animation BuildIdle()
	{
		Animation clip = NewClip(2.4f, true);
		int chest = RotTrack(clip, "chest");
		Key(clip, chest, 0f, 0f);
		Key(clip, chest, 1.2f, -1.6f);
		Key(clip, chest, 2.4f, 0f);
		int armL = RotTrack(clip, "upperarm_l");
		Key(clip, armL, 0f, 1.5f, 0f, 2.5f);
		Key(clip, armL, 1.2f, -1f, 0f, 1f);
		Key(clip, armL, 2.4f, 1.5f, 0f, 2.5f);
		int armR = RotTrack(clip, "upperarm_r");
		Key(clip, armR, 0f, 1.5f, 0f, -2.5f);
		Key(clip, armR, 1.2f, -1f, 0f, -1f);
		Key(clip, armR, 2.4f, 1.5f, 0f, -2.5f);
		return clip;
	}

	private Animation BuildWalk(float hipsRest)
	{
		float length = Mathf.Max(Data.PlaceholderWalkCycle, 0.3f);
		float half = length * 0.5f;
		Animation clip = NewClip(length, true);

		int thighL = RotTrack(clip, "thigh_l");
		Key(clip, thighL, 0f, 26f);
		Key(clip, thighL, half, -24f);
		Key(clip, thighL, length, 26f);
		int thighR = RotTrack(clip, "thigh_r");
		Key(clip, thighR, 0f, -24f);
		Key(clip, thighR, half, 26f);
		Key(clip, thighR, length, -24f);

		int shinL = RotTrack(clip, "shin_l");
		Key(clip, shinL, 0f, -8f);
		Key(clip, shinL, length * 0.25f, -12f);
		Key(clip, shinL, half, -10f);
		Key(clip, shinL, length * 0.75f, -44f);
		Key(clip, shinL, length, -8f);
		int shinR = RotTrack(clip, "shin_r");
		Key(clip, shinR, 0f, -10f);
		Key(clip, shinR, length * 0.25f, -44f);
		Key(clip, shinR, half, -8f);
		Key(clip, shinR, length * 0.75f, -12f);
		Key(clip, shinR, length, -10f);

		int armL = RotTrack(clip, "upperarm_l");
		Key(clip, armL, 0f, -18f, 0f, 3f);
		Key(clip, armL, half, 18f, 0f, 3f);
		Key(clip, armL, length, -18f, 0f, 3f);
		int armR = RotTrack(clip, "upperarm_r");
		Key(clip, armR, 0f, 18f, 0f, -3f);
		Key(clip, armR, half, -18f, 0f, -3f);
		Key(clip, armR, length, 18f, 0f, -3f);

		int chest = RotTrack(clip, "chest");
		Key(clip, chest, 0f, -4f, 4f);
		Key(clip, chest, half, -4f, -4f);
		Key(clip, chest, length, -4f, 4f);

		Vector3 rest = new(0f, hipsRest, 0f);
		int hips = PosTrack(clip, "hips");
		clip.PositionTrackInsertKey(hips, 0f, rest);
		clip.PositionTrackInsertKey(hips, length * 0.25f, rest - new Vector3(0f, 0.02f, 0f));
		clip.PositionTrackInsertKey(hips, half, rest);
		clip.PositionTrackInsertKey(hips, length * 0.75f, rest - new Vector3(0f, 0.02f, 0f));
		clip.PositionTrackInsertKey(hips, length, rest);
		return clip;
	}

	private Animation BuildRun(float hipsRest)
	{
		float length = Mathf.Max(Data.PlaceholderRunCycle, 0.3f);
		float half = length * 0.5f;
		Animation clip = NewClip(length, true);

		int thighL = RotTrack(clip, "thigh_l");
		Key(clip, thighL, 0f, 42f);
		Key(clip, thighL, half, -34f);
		Key(clip, thighL, length, 42f);
		int thighR = RotTrack(clip, "thigh_r");
		Key(clip, thighR, 0f, -34f);
		Key(clip, thighR, half, 42f);
		Key(clip, thighR, length, -34f);

		int shinL = RotTrack(clip, "shin_l");
		Key(clip, shinL, 0f, -16f);
		Key(clip, shinL, length * 0.25f, -20f);
		Key(clip, shinL, half, -18f);
		Key(clip, shinL, length * 0.75f, -68f);
		Key(clip, shinL, length, -16f);
		int shinR = RotTrack(clip, "shin_r");
		Key(clip, shinR, 0f, -18f);
		Key(clip, shinR, length * 0.25f, -68f);
		Key(clip, shinR, half, -16f);
		Key(clip, shinR, length * 0.75f, -20f);
		Key(clip, shinR, length, -18f);

		int armL = RotTrack(clip, "upperarm_l");
		Key(clip, armL, 0f, -32f, 0f, 5f);
		Key(clip, armL, half, 32f, 0f, 5f);
		Key(clip, armL, length, -32f, 0f, 5f);
		int armR = RotTrack(clip, "upperarm_r");
		Key(clip, armR, 0f, 32f, 0f, -5f);
		Key(clip, armR, half, -32f, 0f, -5f);
		Key(clip, armR, length, 32f, 0f, -5f);

		int chest = RotTrack(clip, "chest");
		Key(clip, chest, 0f, -13f, 5f);
		Key(clip, chest, half, -13f, -5f);
		Key(clip, chest, length, -13f, 5f);

		Vector3 rest = new(0f, hipsRest - 0.04f, 0f);
		int hips = PosTrack(clip, "hips");
		clip.PositionTrackInsertKey(hips, 0f, rest);
		clip.PositionTrackInsertKey(hips, length * 0.25f, rest - new Vector3(0f, 0.03f, 0f));
		clip.PositionTrackInsertKey(hips, half, rest);
		clip.PositionTrackInsertKey(hips, length * 0.75f, rest - new Vector3(0f, 0.03f, 0f));
		clip.PositionTrackInsertKey(hips, length, rest);
		return clip;
	}

	private Animation BuildJump()
	{
		Animation clip = NewClip(0.35f, false);
		int thighL = RotTrack(clip, "thigh_l");
		Key(clip, thighL, 0f, 12f);
		Key(clip, thighL, 0.1f, 40f);
		Key(clip, thighL, 0.35f, 28f);
		int thighR = RotTrack(clip, "thigh_r");
		Key(clip, thighR, 0f, 8f);
		Key(clip, thighR, 0.1f, 34f);
		Key(clip, thighR, 0.35f, 24f);
		int shinL = RotTrack(clip, "shin_l");
		Key(clip, shinL, 0f, -12f);
		Key(clip, shinL, 0.1f, -62f);
		Key(clip, shinL, 0.35f, -44f);
		int shinR = RotTrack(clip, "shin_r");
		Key(clip, shinR, 0f, -12f);
		Key(clip, shinR, 0.1f, -56f);
		Key(clip, shinR, 0.35f, -40f);
		return clip;
	}

	private Animation BuildLand(float hipsRest)
	{
		Animation clip = NewClip(0.3f, false);
		int thighL = RotTrack(clip, "thigh_l");
		Key(clip, thighL, 0f, 30f);
		Key(clip, thighL, 0.16f, 10f);
		Key(clip, thighL, 0.3f, 0f);
		int thighR = RotTrack(clip, "thigh_r");
		Key(clip, thighR, 0f, 28f);
		Key(clip, thighR, 0.16f, 9f);
		Key(clip, thighR, 0.3f, 0f);
		int shinL = RotTrack(clip, "shin_l");
		Key(clip, shinL, 0f, -54f);
		Key(clip, shinL, 0.16f, -16f);
		Key(clip, shinL, 0.3f, 0f);
		int shinR = RotTrack(clip, "shin_r");
		Key(clip, shinR, 0f, -50f);
		Key(clip, shinR, 0.16f, -15f);
		Key(clip, shinR, 0.3f, 0f);

		Vector3 rest = new(0f, hipsRest, 0f);
		int hips = PosTrack(clip, "hips");
		clip.PositionTrackInsertKey(hips, 0f, rest - new Vector3(0f, 0.1f, 0f));
		clip.PositionTrackInsertKey(hips, 0.16f, rest - new Vector3(0f, 0.02f, 0f));
		clip.PositionTrackInsertKey(hips, 0.3f, rest);
		return clip;
	}

	private Animation BuildAim()
	{
		Animation clip = NewClip(0.5f, true);
		int armR = RotTrack(clip, "upperarm_r");
		Key(clip, armR, 0f, 68f, 0f, -10f);
		Key(clip, armR, 0.5f, 70f, 0f, -10f);
		int armL = RotTrack(clip, "upperarm_l");
		Key(clip, armL, 0f, 60f, 0f, 14f);
		Key(clip, armL, 0.5f, 62f, 0f, 14f);
		int elbowL = RotTrack(clip, "forearm_l");
		Key(clip, elbowL, 0f, 26f);
		Key(clip, elbowL, 0.5f, 26f);
		int elbowR = RotTrack(clip, "forearm_r");
		Key(clip, elbowR, 0f, 14f);
		Key(clip, elbowR, 0.5f, 14f);
		return clip;
	}

	private Animation BuildFire()
	{
		Animation clip = NewClip(0.12f, false);
		int chest = RotTrack(clip, "chest");
		Key(clip, chest, 0f, -2f);
		Key(clip, chest, 0.03f, 3f);
		Key(clip, chest, 0.12f, -2f);
		return clip;
	}

	private Animation BuildReload()
	{
		Animation clip = NewClip(1f, false);
		int armL = RotTrack(clip, "upperarm_l");
		Key(clip, armL, 0f, 0f, 0f, 4f);
		Key(clip, armL, 0.25f, -40f, 0f, 12f);
		Key(clip, armL, 0.5f, -52f, 0f, 16f);
		Key(clip, armL, 0.8f, -12f, 0f, 6f);
		Key(clip, armL, 1f, 0f, 0f, 4f);
		int elbowL = RotTrack(clip, "forearm_l");
		Key(clip, elbowL, 0f, 0f);
		Key(clip, elbowL, 0.25f, 58f);
		Key(clip, elbowL, 0.5f, 74f);
		Key(clip, elbowL, 0.8f, 20f);
		Key(clip, elbowL, 1f, 0f);
		return clip;
	}

	private Animation BuildHit()
	{
		Animation clip = NewClip(0.3f, false);
		int chest = RotTrack(clip, "chest");
		Key(clip, chest, 0f, 0f);
		Key(clip, chest, 0.06f, 12f);
		Key(clip, chest, 0.3f, 0f);
		int head = RotTrack(clip, "head");
		Key(clip, head, 0f, 0f);
		Key(clip, head, 0.06f, 10f);
		Key(clip, head, 0.3f, 0f);
		return clip;
	}

	private Animation BuildDeath(float hipsRest)
	{
		Animation clip = NewClip(0.9f, false);
		int hipsRot = RotTrack(clip, "hips");
		Key(clip, hipsRot, 0f, 0f);
		Key(clip, hipsRot, 0.25f, -12f);
		Key(clip, hipsRot, 0.9f, 74f);

		Vector3 rest = new(0f, hipsRest, 0f);
		int hipsPos = PosTrack(clip, "hips");
		clip.PositionTrackInsertKey(hipsPos, 0f, rest);
		clip.PositionTrackInsertKey(hipsPos, 0.25f, rest - new Vector3(0f, 0.18f, 0f));
		clip.PositionTrackInsertKey(hipsPos, 0.9f, rest - new Vector3(0f, 0.62f, 0f));
		return clip;
	}

	private Animation BuildSlide(float hipsRest)
	{
		Animation clip = NewClip(0.6f, true);
		Vector3 low = new(0f, hipsRest - 0.3f, 0f);
		int hipsPos = PosTrack(clip, "hips");
		clip.PositionTrackInsertKey(hipsPos, 0f, low);
		clip.PositionTrackInsertKey(hipsPos, 0.6f, low);
		int thighL = RotTrack(clip, "thigh_l");
		Key(clip, thighL, 0f, 65f);
		Key(clip, thighL, 0.6f, 65f);
		int thighR = RotTrack(clip, "thigh_r");
		Key(clip, thighR, 0f, 58f);
		Key(clip, thighR, 0.6f, 58f);
		int shinL = RotTrack(clip, "shin_l");
		Key(clip, shinL, 0f, -70f);
		Key(clip, shinL, 0.6f, -70f);
		int shinR = RotTrack(clip, "shin_r");
		Key(clip, shinR, 0f, -62f);
		Key(clip, shinR, 0.6f, -62f);
		return clip;
	}

	private static T Find<T>(Node root) where T : Node
	{
		if (root == null)
			return null;
		if (root is T typed)
			return typed;
		foreach (Node child in root.GetChildren())
		{
			T found = Find<T>(child);
			if (found != null)
				return found;
		}
		return null;
	}
}
