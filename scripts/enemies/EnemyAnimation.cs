using Godot;
using System.Collections.Generic;

/// <summary>
/// 敌人骨架动画（B 阶段）：用 Skeleton3D + AnimationPlayer 播放蒙皮动作，
/// 取代原来直接旋转枢轴节点的程序化步态。
///
/// 状态覆盖 idle / walk / run / aim / aim_walk / fire / hit / death；
/// 走、跑与持枪走的播放速率按实际移速缩放（速度 / 剪辑原始速度），脚不滑。
/// 三档 LOD 按相机距离互斥显隐，只有可见那档的播放器在跑，
/// 切换时把当前剪辑与播放进度同步过去，避免跳帧。
/// </summary>
public partial class EnemyAnimation : Node
{
	[Export] public EnemyData Data { get; set; }

	/// <summary>全身模型根节点，其子节点按顺序视为 LOD0 / LOD1 / LOD2；留空时自动查找同级带 Skeleton3D 的节点。</summary>
	[Export] public Node Model { get; set; }

	private const string IdleClip = "idle";
	private const string WalkClip = "walk";
	private const string RunClip = "run";
	private const string AimClip = "aim";
	private const string AimWalkClip = "aim_walk";
	private const string FireClip = "fire";
	private const string HitClip = "hit";
	private const string DeathClip = "death";

	private const float WalkThreshold = 0.15f;
	private const float RunThreshold = 2.4f;

	private sealed class Layer
	{
		public Node3D Root;
		public Skeleton3D Skeleton;
		public AnimationPlayer Player;
	}

	private readonly List<Layer> _layers = new();

	private float _speed;
	private float _fireTimer;
	private float _hitTimer;
	private float _fireLength = 0.4f;
	private float _hitLength = 0.5f;
	private float _playbackScale = -1f;
	private string _clip = string.Empty;
	private bool _aiming;
	private bool _dead;
	private bool _ready;
	private bool _restartRequested;
	private int _activeLayer = -1;

	/// <summary>死亡剪辑时长（秒）：EnemyHealth 据此等倒地播完再开始淡出。</summary>
	public float DeathDuration { get; private set; } = 1.5f;

	/// <summary>当前正在驱动的那档 LOD 的骨架（武器挂点用）。</summary>
	public Skeleton3D ActiveSkeleton => _activeLayer >= 0 && _activeLayer < _layers.Count
		? _layers[_activeLayer].Skeleton
		: null;

	public override void _Ready()
	{
		if (Data == null)
		{
			GD.PushWarning($"{GetParent().Name}: 敌人没有配置 EnemyData，动画关闭。");
			SetProcess(false);
			return;
		}

		if (Model == null)
			Model = FindModel();
		if (Model == null)
		{
			GD.PushWarning($"{GetParent().Name}: 找不到敌人模型根节点，动画关闭。");
			SetProcess(false);
			return;
		}

		CollectLayers();
		if (_layers.Count == 0)
		{
			GD.PushWarning($"{GetParent().Name}: 模型里没有 Skeleton3D，敌人动画关闭。");
			SetProcess(false);
			return;
		}

		foreach (Layer layer in _layers)
		{
			// 只缩外观：可见身高对齐各自胶囊，胶囊 / AI / 伤害不动
			layer.Root.Scale = Vector3.One * Mathf.Max(Data.ModelScale, 0.01f);
			PrepareLayer(layer);
		}

		_ready = true;
		SetActiveLayer(_layers.Count == 1 ? 0 : -1);
		Play(IdleClip, 0f, true);
	}

	public override void _Process(double delta)
	{
		if (!_ready)
			return;

		float dt = (float)delta;
		_fireTimer = Mathf.Max(0f, _fireTimer - dt);
		_hitTimer = Mathf.Max(0f, _hitTimer - dt);

		UpdateLod();

		if (_dead)
			return;

		// 受击 / 开火需要"从头再播"，但只在事件那一帧请求一次，不能每帧重启
		bool restart = _restartRequested;
		_restartRequested = false;

		// 优先级：受击 > 开火 > 瞄准中移动 / 站定瞄准 > 跑 > 走 > 站立
		if (_hitTimer > 0f)
		{
			Play(HitClip, Data.StateBlendTime, restart);
			return;
		}

		if (_fireTimer > 0f)
		{
			Play(FireClip, Data.StateBlendTime * 0.4f, restart);
			return;
		}

		float speed = _speed;
		if (_aiming)
		{
			Play(speed > WalkThreshold ? AimWalkClip : AimClip, Data.StateBlendTime);
			UpdatePlaybackScale(speed);
			return;
		}

		if (speed > RunThreshold)
			Play(RunClip, Data.StateBlendTime);
		else if (speed > WalkThreshold)
			Play(WalkClip, Data.StateBlendTime);
		else
			Play(IdleClip, Data.StateBlendTime);

		UpdatePlaybackScale(speed);
	}

	// ---------------------------------------------------------------- 外部驱动

	/// <summary>由 EnemyAgent 每物理帧写入根节点的水平速度。</summary>
	public void SetSpeed(float speed) => _speed = Mathf.Max(speed, 0f);

	/// <summary>进入交战（或警戒）时把上身切成持枪动作。</summary>
	public void SetAiming(bool aiming) => _aiming = aiming;

	public void PlayFire()
	{
		_fireTimer = Mathf.Max(_fireLength * 0.8f, 0.1f);
		_restartRequested = true;
	}

	public void PlayHit()
	{
		_hitTimer = Mathf.Max(_hitLength * 0.8f, 0.1f);
		_restartRequested = true;
	}

	/// <summary>死亡：停掉状态切换，交给 death 剪辑倒地（素材保证末帧贴地）。</summary>
	public void SetDead()
	{
		_dead = true;
		_speed = 0f;
		_restartRequested = false;
		Play(DeathClip, 0.12f, true);
	}

	// ---------------------------------------------------------------- 播放

	private void Play(string clip, float blend, bool restart = false)
	{
		if (string.IsNullOrEmpty(clip) || _clip == clip && !restart)
			return;

		_clip = clip;
		foreach (Layer layer in _layers)
		{
			if (!layer.Player.HasAnimation(clip))
				continue;
			layer.Player.Play(clip, blend);
			if (restart)
				layer.Player.Seek(0f, true);
		}

		_playbackScale = -1f;
	}

	/// <summary>按实际速度 / 剪辑原始速度改写播放速率，脚面线速度跟上位移。</summary>
	private void UpdatePlaybackScale(float speed)
	{
		float natural = _clip switch
		{
			WalkClip => Data.WalkClipSpeed,
			RunClip => Data.RunClipSpeed,
			AimWalkClip => Data.AimWalkClipSpeed,
			_ => 0f,
		};
		// 剪辑原速是素材原始身高下的数值，模型缩放后步幅同比缩短
		natural *= Data.ModelScale;

		float scale = natural > 0.01f
			? Mathf.Clamp(speed / natural, Data.MinPlaybackScale, Data.MaxPlaybackScale)
			: 1f;

		if (Mathf.Abs(scale - _playbackScale) < 0.01f)
			return;

		_playbackScale = scale;
		foreach (Layer layer in _layers)
			layer.Player.SpeedScale = scale;
	}

	// ---------------------------------------------------------------- LOD

	/// <summary>按相机距离切换 LOD0/1/2（10m / 25m），只让可见那档的播放器推进。</summary>
	private void UpdateLod()
	{
		if (_layers.Count == 1)
		{
			SetActiveLayer(0);
			return;
		}

		Camera3D camera = GetViewport()?.GetCamera3D();
		float distance = camera != null ? camera.GlobalPosition.DistanceTo(GetParent<Node3D>().GlobalPosition) : 0f;
		float lod0 = Data.Lod0Distance * Data.ModelScale;
		float lod1 = Data.Lod1Distance * Data.ModelScale;
		int index = distance < lod0 ? 0 : distance < lod1 ? 1 : 2;
		SetActiveLayer(index);
	}

	private void SetActiveLayer(int index)
	{
		if (index == _activeLayer || index < 0 || index >= _layers.Count)
			return;

		float position = _activeLayer >= 0 ? (float)_layers[_activeLayer].Player.CurrentAnimationPosition : 0f;
		_activeLayer = index;

		for (int i = 0; i < _layers.Count; i++)
		{
			Layer layer = _layers[i];
			bool visible = i == index;
			layer.Root.Visible = visible;
			layer.Player.Active = visible;

			if (!visible)
				continue;

			if (!string.IsNullOrEmpty(_clip) && layer.Player.HasAnimation(_clip))
			{
				layer.Player.Play(_clip, 0f);
				layer.Player.Seek(position, true);
			}

			layer.Player.SpeedScale = _playbackScale > 0f ? _playbackScale : 1f;
		}
	}

	// ---------------------------------------------------------------- 初始化

	private void CollectLayers()
	{
		foreach (Node child in Model.GetChildren())
		{
			if (child is Node3D node && Find<Skeleton3D>(node) != null)
				_layers.Add(new Layer { Root = node, Skeleton = Find<Skeleton3D>(node), Player = Find<AnimationPlayer>(node) });
		}

		if (_layers.Count == 0 && Find<Skeleton3D>(Model) != null && Model is Node3D modelRoot)
			_layers.Add(new Layer { Root = modelRoot, Skeleton = Find<Skeleton3D>(Model), Player = Find<AnimationPlayer>(Model) });
	}

	private void PrepareLayer(Layer layer)
	{
		if (layer.Player == null)
		{
			layer.Player = new AnimationPlayer { Name = "AnimationPlayer" };
			layer.Root.AddChild(layer.Player);
		}

		// glTF 不带循环标记：按素材说明把五个循环动作设成 LOOP_LINEAR。
		foreach (string clip in new[] { IdleClip, WalkClip, RunClip, AimClip, AimWalkClip })
		{
			if (layer.Player.HasAnimation(clip))
				layer.Player.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
		}

		if (layer.Player.HasAnimation(DeathClip))
			DeathDuration = (float)layer.Player.GetAnimation(DeathClip).Length;
		if (layer.Player.HasAnimation(FireClip))
			_fireLength = (float)layer.Player.GetAnimation(FireClip).Length;
		if (layer.Player.HasAnimation(HitClip))
			_hitLength = (float)layer.Player.GetAnimation(HitClip).Length;
	}

	private Node FindModel()
	{
		foreach (Node child in GetParent().GetChildren())
		{
			if (child == this)
				continue;
			if (Find<Skeleton3D>(child) != null)
				return child;
		}
		return null;
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
