using Godot;
using System;

/// <summary>
/// 音效调度：把玩法事件映射到 ProceduralAudio 合成的音效上，管理一次性 3D 音源池与
/// 风声 / 海浪 / 直升机三组循环环境音（设计文档 §8.8、§9.3）。
/// </summary>
public partial class AudioDirector : Node
{
	/// <summary>当前关卡的音效调度；Boss 等不在同一父链上的系统通过它播一次性音效。</summary>
	public static AudioDirector Instance { get; private set; }

	private const int PoolSize = 14;

	private AudioStreamPlayer3D[] _pool;
	private int _next;
	private AudioStreamPlayer _wind;
	private AudioStreamPlayer _waves;
	private AudioStreamPlayer _radio;
	private AudioStreamPlayer3D _heliLoop;

	private PlayerController _player;
	private PlayerHealth _health;
	private WeaponController _weapon;
	private MeleeController _melee;
	private Node3D _helicopter;
	private VictorySequenceController _victory;
	private bool _victoryStarted;

	public override void _Ready()
	{
		Instance = this;
		_player = GetNode<PlayerController>("../../Actors/Player");
		_health = _player.GetNode<PlayerHealth>("Health");
		_weapon = GetTree().GetFirstNodeInGroup("weapon") as WeaponController;
		_helicopter = GetNodeOrNull<Node3D>("../../Actors/Helicopter");
		_victory = GetNodeOrNull<VictorySequenceController>("../VictorySequenceController");

		_pool = new AudioStreamPlayer3D[PoolSize];
		for (int i = 0; i < PoolSize; i++)
		{
			var source = new AudioStreamPlayer3D { MaxDistance = 90f, UnitSize = 8f, Bus = "SFX" };
			AddChild(source);
			_pool[i] = source;
		}

		_wind = new AudioStreamPlayer { Stream = ProceduralAudio.Wind(), VolumeDb = -26f, Bus = "Ambient" };
		_waves = new AudioStreamPlayer { Stream = ProceduralAudio.Waves(), VolumeDb = -24f, Bus = "Ambient" };
		_radio = new AudioStreamPlayer { Stream = ProceduralAudio.Radio(), VolumeDb = -3f, Bus = "Radio" };
		AddChild(_wind);
		AddChild(_waves);
		AddChild(_radio);
		_wind.Play();
		_waves.Play();

		_heliLoop = new AudioStreamPlayer3D
		{
			Stream = ProceduralAudio.Helicopter(),
			MaxDistance = 180f,
			UnitSize = 18f,
			VolumeDb = -10f,
			Bus = "Ambient",
		};
		AddChild(_heliLoop);

		_player.Footstepped += () =>
			PlayAt(SfxKind.Footstep, _player.GlobalPosition, -13f, 0.95f + (float)GD.RandRange(0f, 0.1f));
		_health.Damaged += OnPlayerDamaged;

		if (_weapon != null)
		{
			_weapon.ShotResolved += OnShotResolved;
			_weapon.ReloadCuePlayed += cue =>
			{
				// 只在动作真正到位的帧响：撤匣 / 顶匣 / 套筒复进
				SfxKind kind = cue switch
				{
					ReloadCue.MagOut => SfxKind.ReloadMagOut,
					ReloadCue.Seat => SfxKind.ReloadSeat,
					_ => SfxKind.ReloadSlide,
				};
				float volume = cue switch
				{
					ReloadCue.Seat => -3f,
					ReloadCue.Slide => -5f,
					_ => -8f,
				};
				PlayAt(kind, _player.GlobalPosition, volume);
			};
			_weapon.DryFired += () => PlayAt(SfxKind.DryFire, _player.GlobalPosition, -5f);
		}

		_melee = _player.GetNodeOrNull<MeleeController>("Melee");
		if (_melee != null)
		{
			_melee.Swung += () => PlayAt(SfxKind.Melee, _player.GlobalPosition, -5f);
			_melee.Resolved += (point, hit) =>
			{
				if (hit)
					PlayAt(SfxKind.Hit, point, -2f, 0.9f);
			};
		}

		if (_victory != null)
		{
			_victory.Started += () => _victoryStarted = true;
			_victory.RadioCue += () => _radio.Play();
		}

		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			// 组挂在 EnemyHealth 上，EnemyAgent 是它的父节点
			if (node is EnemyHealth existing)
				Subscribe(existing);
		}

		EnemyHealth.Spawned += Subscribe;
	}

	/// <summary>敌人（含运行时增援）进场后挂上音效。</summary>
	private void Subscribe(EnemyHealth health)
	{
		if (health == null || health.GetParent() is not EnemyAgent agent)
			return;

		agent.Fired += position =>
			PlayAt(SfxKind.Gunshot, position, -3f, 0.94f + (float)GD.RandRange(0f, 0.12f));
		agent.Alerted += () => PlayAt(SfxKind.Alert, agent.GlobalPosition, -7f);
		health.Died += () =>
		{
			PlayAt(SfxKind.Death, agent.GlobalPosition, -4f, 0.9f + (float)GD.RandRange(0f, 0.2f));
			// 击杀确认必须比命中更响、更厚，单独一层低频 + 金属脆响
			PlayAt(SfxKind.Kill, agent.GlobalPosition, 1f, 0.98f + (float)GD.RandRange(0f, 0.04f));
		};
	}

	public override void _ExitTree()
	{
		EnemyHealth.Spawned -= Subscribe;
		_wind?.Stop();
		_waves?.Stop();
		_radio?.Stop();
		_heliLoop?.Stop();
		foreach (AudioStreamPlayer3D source in _pool)
		{
			source?.Stop();
			if (source != null)
				source.Stream = null;
		}

		_wind = null;
		_waves = null;
		_radio = null;
		_heliLoop = null;
		if (Instance == this)
			Instance = null;
		ProceduralAudio.ClearCache();
	}

	public override void _Process(double delta)
	{
		// 高度越高风声越明显，海浪越靠近地面越响（§8.8）
		float height = Mathf.Clamp(_player.GlobalPosition.Y / 42f, 0f, 1f);
		_wind.VolumeDb = Mathf.Lerp(-26f, -16f, height);
		_waves.VolumeDb = Mathf.Lerp(-14f, -30f, height);

		if (_helicopter == null)
			return;

		_heliLoop.GlobalPosition = _helicopter.GlobalPosition;
		if (!_victoryStarted && _helicopter.Visible)
			_victoryStarted = true;

		if (_victoryStarted && !_heliLoop.Playing)
			_heliLoop.Play();
	}

	private void OnPlayerDamaged(float amount, DeathCause cause)
	{
		SfxKind kind = cause == DeathCause.Fall ? SfxKind.Land : SfxKind.Hurt;
		PlayAt(kind, _player.GlobalPosition, -5f, 0.95f + (float)GD.RandRange(0f, 0.1f));
	}

	private void OnShotResolved(HitscanHit hit)
	{
		PlayAt(SfxKind.Gunshot, _weapon.GlobalPosition, -2f, 0.95f + (float)GD.RandRange(0f, 0.1f));

		if (!hit.HasHit)
			return;

		bool headshot = hit.Collider is Hitbox { IsHead: true };
		PlayAt(headshot ? SfxKind.Headshot : SfxKind.Hit, hit.Point,
			headshot ? 2f : 1f, 0.96f + (float)GD.RandRange(0f, 0.08f));
	}

	/// <summary>关卡脚本（小队协同提示等）用的一次性世界音效。</summary>
	public void PlayWorld(SfxKind kind, Vector3 position, float volumeDb = 0f, float pitch = 1f) =>
		PlayAt(kind, position, volumeDb, pitch);

	/// <summary>无线电台词提示音（2.0 设计 §4.3），与撤离电台共用 Radio 总线。</summary>
	public void PlayRadioCue() => _radio?.Play();

	private void PlayAt(SfxKind kind, Vector3 position, float volumeDb = 0f, float pitch = 1f)	{
		AudioStreamPlayer3D source = _pool[_next];
		_next = (_next + 1) % _pool.Length;

		source.Stop();
		source.Stream = ProceduralAudio.Get(kind);
		source.GlobalPosition = position;
		source.VolumeDb = volumeDb;
		source.PitchScale = pitch;
		source.Play();
	}
}
