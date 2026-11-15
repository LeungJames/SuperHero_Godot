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

	private const int PoolSize = 24;

	private AudioStreamPlayer3D[] _pool;
	private int _next;
	private AudioStreamPlayer _wind;
	private AudioStreamPlayer _waves;
	private AudioStreamPlayer _radio;
	private AudioStreamPlayer _industry;
	private MusicDirector _music;
	public bool RadioPlaying => _radio?.Playing == true;
	private AudioStreamPlayer3D _heliLoop;

	private PlayerController _player;
	private PlayerHealth _health;
	private WeaponController _weapon;
	private MeleeController _melee;
	private Node3D _helicopter;
	private VictorySequenceController _victory;
	private GameManager _game;
	private bool _victoryStarted;

	public override void _Ready()
	{
		Instance = this;
		EnsureMixBuses();
		_player = GetNode<PlayerController>("../../Actors/Player");
		_health = _player.GetNode<PlayerHealth>("Health");
		_weapon = GetTree().GetFirstNodeInGroup("weapon") as WeaponController;
		_helicopter = GetNodeOrNull<Node3D>("../../Actors/Helicopter");
		_victory = GetNodeOrNull<VictorySequenceController>("../VictorySequenceController");
		_game = GetNodeOrNull<GameManager>("../GameManager");

		_pool = new AudioStreamPlayer3D[PoolSize];
		for (int i = 0; i < PoolSize; i++)
		{
			var source = new AudioStreamPlayer3D { MaxDistance = 90f, UnitSize = 8f, Bus = "SFX", MaxDb = 0f };
			AddChild(source);
			_pool[i] = source;
		}

		_wind = new AudioStreamPlayer { Stream = AudioPalette.Load("res://audio/sfx/harbor_wind.ogg", true) ?? ProceduralAudio.Wind(), VolumeDb = -18f, Bus = "Ambient" };
		_waves = new AudioStreamPlayer { Stream = AudioPalette.Load("res://audio/sfx/harbor_water.ogg", true) ?? ProceduralAudio.Waves(), VolumeDb = -20f, Bus = "Ambient" };
		_industry = new AudioStreamPlayer { Stream = AudioPalette.Load("res://audio/sfx/harbor_industry.ogg", true), VolumeDb = -23f, Bus = "Ambient" };
		_radio = new AudioStreamPlayer { Stream = ProceduralAudio.Radio(), VolumeDb = -3f, Bus = "Radio" };
		AddChild(_wind);
		AddChild(_waves);
		AddChild(_radio);
		AddChild(_industry);
		_wind.Play();
		_waves.Play();
		if (_industry.Stream != null) _industry.Play();
		_music = new MusicDirector { Name = "MusicDirector" };
		AddChild(_music);

		_heliLoop = new AudioStreamPlayer3D
		{
			Stream = ProceduralAudio.Helicopter(),
			MaxDistance = 180f,
			UnitSize = 18f,
			VolumeDb = -10f,
			Bus = "Ambient",
		};
		AddChild(_heliLoop);

		_player.Footstepped += PlayFootstep;
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
		{
			PlayStream(AudioPalette.Shot(), position, -8f, 0.96f + (float)GD.RandRange(0f, 0.08f));
			if (_player.GlobalPosition.DistanceTo(position) < 65f) _music.NotifyCombat();
		};
		agent.Alerted += () => { PlayAt(SfxKind.Alert, agent.GlobalPosition, -15f); _music.NotifyAlert(); };
		health.Died += () =>
		{
			PlayAt(SfxKind.Death, agent.GlobalPosition, -4f, 0.9f + (float)GD.RandRange(0f, 0.2f));
			// 击杀确认必须比命中更响、更厚，单独一层低频 + 金属脆响
			PlayAt(SfxKind.Kill, agent.GlobalPosition, -5f, 0.98f + (float)GD.RandRange(0f, 0.04f));
		};
	}

	public override void _ExitTree()
	{
		EnemyHealth.Spawned -= Subscribe;
		if (_player != null) _player.Footstepped -= PlayFootstep;
		if (_health != null) _health.Damaged -= OnPlayerDamaged;
		if (_weapon != null) _weapon.ShotResolved -= OnShotResolved;
		_wind?.Stop();
		_waves?.Stop();
		_radio?.Stop();
		_industry?.Stop();
		_heliLoop?.Stop();
		foreach (AudioStreamPlayer3D source in _pool ?? Array.Empty<AudioStreamPlayer3D>())
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
		AudioPalette.Clear();
	}

	public override void _Process(double delta)
	{
		// 高度越高风声越明显，海浪越靠近地面越响（§8.8）
		float height = Mathf.Clamp(_player.GlobalPosition.Y / 42f, 0f, 1f);
		float duck = RadioPlaying ? -5f : _health.IsDead ? -8f : 0f;
		_wind.VolumeDb = Mathf.Lerp(-22f, -13f, height) + duck;
		_waves.VolumeDb = Mathf.Lerp(-15f, -30f, height) + duck;
		bool factory = _game?.ChapterId == "factory";
		if (factory) _waves.VolumeDb -= 15f;
		_industry.VolumeDb = (factory ? -17f : -25f) + duck;

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
		if (cause == DeathCause.Gunshot || cause == DeathCause.Explosion) _music.NotifyCombat();
	}

	private void OnShotResolved(HitscanHit hit)
	{
		PlayStream(AudioPalette.Shot(_weapon.Data), _weapon.GlobalPosition, -4f, 0.97f + (float)GD.RandRange(0f, 0.06f));
		_music.NotifyCombat();

		if (!hit.HasHit)
			return;

		bool headshot = hit.Collider is Hitbox { IsHead: true };
		PlayAt(headshot ? SfxKind.Headshot : SfxKind.Hit, hit.Point,
			headshot ? -3f : -7f, 0.96f + (float)GD.RandRange(0f, 0.08f));
	}

	/// <summary>关卡脚本（小队协同提示等）用的一次性世界音效。</summary>
	public void PlayWorld(SfxKind kind, Vector3 position, float volumeDb = 0f, float pitch = 1f) =>
		PlayAt(kind, position, volumeDb, pitch);

	/// <summary>无线电台词提示音（2.0 设计 §4.3），与撤离电台共用 Radio 总线。</summary>
	public void PlayRadioCue() => _radio?.Play();

	private void PlayAt(SfxKind kind, Vector3 position, float volumeDb = 0f, float pitch = 1f) =>
		PlayStream(kind == SfxKind.Gunshot ? AudioPalette.Shot() : AudioPalette.Cue(kind), position, volumeDb, pitch);

	private void PlayStream(AudioStream stream, Vector3 position, float volumeDb = 0f, float pitch = 1f)
	{
		if (stream == null || _pool == null) return;
		// Reuse idle voices first so reload/impact tails survive automatic gunfire.
		int index = _next;
		for (int i = 0; i < _pool.Length; i++)
		{
			int candidate = (_next + i) % _pool.Length;
			if (!_pool[candidate].Playing) { index = candidate; break; }
		}
		AudioStreamPlayer3D source = _pool[index];
		_next = (index + 1) % _pool.Length;

		source.Stop();
		source.Stream = stream;
		source.GlobalPosition = position;
		source.VolumeDb = volumeDb;
		source.PitchScale = pitch;
		source.Play();
	}

	private void PlayFootstep()
	{
		string surface = "concrete";
		Vector3 p = _player.GlobalPosition;
		var query = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * .2f, p + Vector3.Down * .55f);
		query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		Godot.Collections.Dictionary hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node collider)
		{
			// Explicit audio_surface metadata wins; existing level names give useful defaults.
			if (collider.HasMeta("audio_surface")) surface = collider.GetMeta("audio_surface").AsString();
			else
			{
				string label = (collider.GetPath().ToString()).ToLowerInvariant();
				if (label.Contains("deck") || label.Contains("container") || label.Contains("crane") || label.Contains("catwalk") || label.Contains("stair")) surface = "metal";
				else if (label.Contains("crate") || label.Contains("pallet")) surface = "wood";
			}
		}
		if (surface != "metal" && surface != "wood") surface = "concrete";
		PlayStream(AudioPalette.Step(surface), p, -13f, .94f + (float)GD.RandRange(0f, .12f));
	}

	private static void EnsureMixBuses()
	{
		foreach (string bus in new[] { "SFX", "Ambient", "Radio", "Music" })
		{
			if (AudioServer.GetBusIndex(bus) >= 0) continue;
			AudioServer.AddBus();
			int index = AudioServer.BusCount - 1;
			AudioServer.SetBusName(index, bus);
			AudioServer.SetBusSend(index, "Master");
		}
		// A single master limiter catches stacked shots without changing saved volume settings.
		bool hasLimiter = false;
		for (int i = 0; i < AudioServer.GetBusEffectCount(0); i++)
			if (AudioServer.GetBusEffect(0, i) is AudioEffectHardLimiter) hasLimiter = true;
		if (!hasLimiter) AudioServer.AddBusEffect(0, new AudioEffectHardLimiter());
	}
}
