using Godot;
using System;
using System.Collections.Generic;

/// <summary>Five 32-bar arrangements share a transport and harmony; changes preserve the musical phrase.</summary>
public partial class MusicDirector : Node
{
	public enum ScoreState { Explore, Alert, Combat, Boss, Extraction, Defeat }
	public ScoreState CurrentState { get; private set; } = ScoreState.Explore;
	private const double PhraseSeconds = 76.8;
	private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[2];
	private readonly List<EnemyAgent> _enemies = new();
	private AudioStreamPlayer _heartbeat;
	private PlayerHealth _health;
	private PlayerController _player;
	private BossController _boss;
	private GameManager _game;
	private VictorySequenceController _victory;
	private AudioDirector _audio;
	private int _active;
	private float _fade = 1f;
	private float _fadeSeconds = 2.4f;
	private float _scan;
	private float _combatHold;
	private float _alertHold;
	private float _duckDb;
	private bool _started;
	private bool _defeatCuePlayed;
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_audio = GetParent<AudioDirector>();
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_health = _player?.GetNodeOrNull<PlayerHealth>("Health");
		if (_health != null) _health.Died += OnPlayerDied;
		_game = _audio.GetNodeOrNull<GameManager>("../GameManager");
		_victory = _audio.GetNodeOrNull<VictorySequenceController>("../VictorySequenceController");
		for (int i = 0; i < 2; i++)
		{
			_voices[i] = new AudioStreamPlayer { Name = $"Score{i}", Bus = "Music", VolumeDb = -80f, ProcessMode = ProcessModeEnum.Always };
			AddChild(_voices[i]);
		}
		_heartbeat = new AudioStreamPlayer { Name = "LowHealthPulse", Bus = "SFX", VolumeDb = -22f,
			Stream = AudioPalette.Load("res://audio/sfx/heartbeat.ogg", true), ProcessMode = ProcessModeEnum.Always };
		AddChild(_heartbeat);
		RefreshActors();
		StartArrangement(ScoreState.Explore);
	}
	public void NotifyCombat() { _combatHold = 7f; _alertHold = 15f; }
	public void NotifyAlert() => _alertHold = Mathf.Max(_alertHold, 9f);
	private void OnPlayerDied(DeathCause cause) => StartArrangement(ScoreState.Defeat);
	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(_health)) _health.Died -= OnPlayerDied;
		foreach (AudioStreamPlayer voice in _voices) voice?.Stop();
		_heartbeat?.Stop();
	}
	private void RefreshActors()
	{
		_enemies.Clear();
		foreach (Node n in GetTree().GetNodesInGroup("enemy"))
			if (n is EnemyHealth h && h.GetParent() is EnemyAgent agent) _enemies.Add(agent);
		_boss = GetTree().GetFirstNodeInGroup("boss") as BossController;
	}
	public override void _Process(double delta)
	{
		float dt = (float)delta;
		bool paused = GetTree().Paused;
		bool dead = _health != null && _health.IsDead;
		bool low = !dead && _health != null && _health.MaxHealth > 0 && _health.Health / _health.MaxHealth < .28f;
		float desiredDuck = paused ? -13f : _audio.RadioPlaying ? -8f : low ? -3f : 0f;
		_duckDb = Mathf.Lerp(_duckDb, desiredDuck, 1f - Mathf.Exp(-dt * 4f));
		if (low && !paused && _game?.State == GameState.Playing)
		{
			if (!_heartbeat.Playing && _heartbeat.Stream != null) _heartbeat.Play();
		}
		else _heartbeat.Stop();
		if (!paused)
		{
			_combatHold = Mathf.Max(0, _combatHold - dt);
			_alertHold = Mathf.Max(0, _alertHold - dt);
			_scan -= dt;
			if (_scan <= 0)
			{
				_scan = .5f;
				RefreshActors();
				ScoreState desired = ResolveState(dead);
				if (desired != CurrentState && (_fade >= 1 || desired == ScoreState.Defeat)) StartArrangement(desired);
			}
		}
		_fade = Mathf.Min(1, _fade + dt / _fadeSeconds);
		// Equal-power crossfade. The destination seeks into the same harmony/bar.
		float a = Mathf.Sin(_fade * Mathf.Pi * .5f);
		float b = Mathf.Cos(_fade * Mathf.Pi * .5f);
		_voices[_active].VolumeDb = -7f + _duckDb + Mathf.LinearToDb(Mathf.Max(a, .0001f));
		_voices[1 - _active].VolumeDb = -7f + _duckDb + Mathf.LinearToDb(Mathf.Max(b, .0001f));
		if (_fade >= 1f && _voices[1 - _active].Playing) _voices[1 - _active].Stop();
	}
	private ScoreState ResolveState(bool dead)
	{
		if (dead || _game?.State == GameState.Defeat) return ScoreState.Defeat;
		if (_game?.State == GameState.Victory || _victory?.IsPlaying == true) return ScoreState.Extraction;
		if (GodotObject.IsInstanceValid(_boss) && _boss.IsEngaged && !_boss.IsDefeated) return ScoreState.Boss;
		foreach (EnemyAgent enemy in _enemies)
		{
			if (!GodotObject.IsInstanceValid(enemy) || enemy.IsDead || enemy.DistanceToPlayer > 65f) continue;
			if (enemy.HasSight) { _combatHold = Mathf.Max(_combatHold, 3f); _alertHold = Mathf.Max(_alertHold, 10f); }
			else if (enemy.State != EnemyState.Patrol) _alertHold = Mathf.Max(_alertHold, 2f);
		}
		return _combatHold > 0 ? ScoreState.Combat : _alertHold > 0 ? ScoreState.Alert : ScoreState.Explore;
	}
	private void StartArrangement(ScoreState state)
	{
		if (state == ScoreState.Defeat && _defeatCuePlayed) return;
		double transport = _started ? _voices[_active].GetPlaybackPosition() % PhraseSeconds : 0;
		string name = state.ToString().ToLowerInvariant();
		AudioStream stream = AudioPalette.Load($"res://audio/score/harbor_{name}.ogg", state != ScoreState.Defeat);
		if (stream == null) return;
		_active = _started ? 1 - _active : 0;
		_voices[_active].Stream = stream;
		_voices[_active].VolumeDb = -80;
		_voices[_active].Play(state == ScoreState.Defeat ? 0 : (float)transport);
		_fade = 0;
		_fadeSeconds = state == ScoreState.Defeat ? .75f : 2.4f;
		CurrentState = state;
		_started = true;
		if (state == ScoreState.Defeat) _defeatCuePlayed = true;
	}
}
