using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Foreman performance layer. Existing battle timing remains authoritative:
/// locomotion clips provide weight, and skeleton-space poses telegraph each move.
/// Evaluated manually once per frame, so additive rotations cannot accumulate.
/// </summary>
public partial class BossAnimation : Node
{
	private BossController _boss;
	private Node3D _visual;
	private Skeleton3D _skeleton;
	private AnimationPlayer _player;
	private Node3D _core;
	private OmniLight3D _coreLight;
	private Vector3 _coreBoneOffset;
	private readonly Dictionary<string, int> _bones = new();
	private readonly Dictionary<string, Vector3> _pose = new();
	private string _clip = "";
	private string _lastState = "";
	private float _time;
	private float _stateTime;
	private float _strikeAge = 10f;
	private BossMoveKind _strikeKind;
	private int _strikeStage;
	private bool _dead;

	public string AnimationState => _clip + "/" + (_boss?.StateName ?? "unbound");

	public override void _Ready()
	{
		_boss = GetParent<BossController>();
		_visual = _boss.GetNode<Node3D>("Visual");
		_skeleton = Find<Skeleton3D>(_visual);
		_player = Find<AnimationPlayer>(_visual);
		_core = _visual.GetNodeOrNull<Node3D>("CoreGlow");
		_coreLight = _visual.GetNodeOrNull<OmniLight3D>("CoreLight");
		if (_skeleton == null || _player == null)
		{
			GD.PushError("BossAnimation: the foreman asset requires its preserved skeleton and animation library.");
			SetProcess(false);
			return;
		}
		foreach (string bone in new[] { "pelvis", "spine_01", "spine_03", "spine_05", "head", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "hand_l", "hand_r" })
			_bones[bone] = _skeleton.FindBone(bone);
		_player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		foreach (string clip in new[] { "idle", "walk", "run" })
			if (_player.HasAnimation(clip)) _player.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
		_boss.AttackResolved += OnAttack;
		if (_core != null && _bones["spine_03"] >= 0)
		{
			Transform3D rest = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalRest(_bones["spine_03"]);
			_coreBoneOffset = rest.AffineInverse() * _core.GlobalPosition;
		}
		ApplyMaterials(_visual);
		Play("idle", 1f);
		_player.Advance(0);
	}

	public override void _ExitTree()
	{
		if (_boss != null) _boss.AttackResolved -= OnAttack;
	}

	public override void _Process(double delta)
	{
		if (_skeleton == null || _player == null) return;
		float dt = (float)delta;
		_time += dt;
		_stateTime += dt;
		_strikeAge += dt;
		string state = _boss.StateName;
		if (state != _lastState) { _lastState = state; _stateTime = 0; }
		float speed = new Vector2(_boss.Velocity.X, _boss.Velocity.Z).Length();

		if (_boss.IsDefeated)
		{
			if (!_dead) { _dead = true; Play("death", 1f); }
			_player.Advance(dt);
			UpdateCore();
			return;
		}

		string clip = state == "Charge" ? "run" : state == "Chase" && speed > 0.12f ? (speed > 2.3f ? "run" : "walk") : "idle";
		float clipSpeed = clip == "run" ? 1.77f : 0.7f;
		float scale = clip == "idle" ? 1f : Mathf.Clamp(speed / (clipSpeed * 1.5f), 0.65f, 1.6f);
		Play(clip, scale);
		_player.Advance(dt);
		_pose.Clear();

		// Broad, low guard with a subtle heavy breathing rhythm.
		Pose("upperarm_l", new Vector3(0, 0, -7));
		Pose("upperarm_r", new Vector3(0, 0, 7));
		Pose("spine_03", new Vector3(Mathf.Sin(_time * 1.7f) * 1.2f, 0, 0));

		if (state == "Windup") Windup(_boss.CurrentMoveKind, _boss.WindupProgress, _boss.CurrentMoveStage);
		if (state == "Charge")
		{
			Pose("spine_03", new Vector3(-22, 0, 0));
			Pose("upperarm_l", new Vector3(-36, 0, -10));
			Pose("upperarm_r", new Vector3(-46, 0, 10));
		}
		if (state == "Recover")
		{
			float weight = 1f - Mathf.SmoothStep(0, 1, Mathf.Clamp(_stateTime / 0.65f, 0, 1));
			Pose("spine_03", new Vector3(-13, 9, 0) * weight);
			Pose("head", new Vector3(12, -9, 0) * weight);
		}
		if (state == "PhaseShift")
		{
			float lift = Mathf.Sin(Mathf.Clamp(_stateTime / 0.8f, 0, 1) * Mathf.Pi);
			Pose("upperarm_l", new Vector3(-42, 0, -38) * lift);
			Pose("upperarm_r", new Vector3(-42, 0, 38) * lift);
			Pose("spine_03", new Vector3(10, 0, 0) * lift);
			Pose("head", new Vector3(-14, 0, 0) * lift);
		}
		if (_strikeAge < 0.30f) Strike(_strikeKind, _strikeAge / 0.30f, _strikeStage);
		foreach (var pair in _pose) RotateBone(pair.Key, pair.Value);
		UpdateCore();
	}

	private void Windup(BossMoveKind kind, float progress, int stage)
	{
		float t = Mathf.SmoothStep(0, 1, progress);
		bool left = kind == BossMoveKind.DoubleHook && stage == 1;
		string arm = left ? "upperarm_l" : "upperarm_r";
		switch (kind)
		{
			case BossMoveKind.Sweep:
			case BossMoveKind.DoubleHook:
				Pose("spine_03", new Vector3(5, left ? -28 : 28, 0) * t);
				Pose(arm, new Vector3(-35, left ? -48 : 48, left ? -22 : 22) * t);
				Pose(left ? "lowerarm_l" : "lowerarm_r", new Vector3(-34, 0, 0) * t);
				break;
			case BossMoveKind.Charge:
				Pose("spine_03", new Vector3(-25, 0, 0) * t);
				Pose("upperarm_l", new Vector3(-55, 0, -12) * t);
				Pose("upperarm_r", new Vector3(-55, 0, 12) * t);
				break;
			case BossMoveKind.ChainPull:
				Pose("spine_03", new Vector3(5, 20, 0) * t);
				Pose("upperarm_r", new Vector3(-105, 15, 12) * t);
				break;
			case BossMoveKind.Summon:
				Pose("upperarm_l", new Vector3(-95, 0, -20) * t);
				Pose("head", new Vector3(-8, -18, 0) * t);
				break;
			case BossMoveKind.AnchorRain:
				Pose("upperarm_l", new Vector3(-130, 0, -24) * t);
				Pose("upperarm_r", new Vector3(-130, 0, 24) * t);
				Pose("spine_03", new Vector3(10, 0, 0) * t);
				break;
		}
	}

	private void Strike(BossMoveKind kind, float t, int stage)
	{
		float weight = 1f - Mathf.SmoothStep(0, 1, t);
		float follow = Mathf.Sin(t * Mathf.Pi);
		bool left = kind == BossMoveKind.DoubleHook && stage == 1;
		if (kind is BossMoveKind.Sweep or BossMoveKind.DoubleHook or BossMoveKind.ChainPull)
		{
			Pose("spine_03", new Vector3(-12, left ? 28 : -28, 0) * weight);
			Pose(left ? "upperarm_l" : "upperarm_r", new Vector3(-90 + follow * 38, left ? 25 : -25, 0) * weight);
		}
		else if (kind == BossMoveKind.AnchorRain)
		{
			Pose("spine_03", new Vector3(-24, 0, 0) * weight);
			Pose("upperarm_l", new Vector3(-68, 0, -10) * weight);
			Pose("upperarm_r", new Vector3(-68, 0, 10) * weight);
		}
	}

	private void OnAttack(BossMoveKind kind, int stage)
	{
		_strikeKind = kind; _strikeStage = stage; _strikeAge = 0;
	}
	private void Pose(string bone, Vector3 degrees) => _pose[bone] = degrees;
	private void RotateBone(string name, Vector3 degrees)
	{
		if (!_bones.TryGetValue(name, out int bone) || bone < 0) return;
		Basis rest = _skeleton.GetBoneGlobalRest(bone).Basis.Orthonormalized();
		Quaternion delta = Quaternion.Identity;
		foreach (var pair in new[] { (Vector3.Right, degrees.X), (Vector3.Up, degrees.Y), (Vector3.Back, degrees.Z) })
			if (Mathf.Abs(pair.Item2) > 0.001f)
				delta *= new Quaternion((rest.Inverse() * pair.Item1).Normalized(), Mathf.DegToRad(pair.Item2));
		_skeleton.SetBonePoseRotation(bone, (_skeleton.GetBonePoseRotation(bone) * delta).Normalized());
	}

	private void UpdateCore()
	{
		if (_core == null || _bones["spine_03"] < 0) return;
		_core.GlobalPosition = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_bones["spine_03"]) * _coreBoneOffset;
		if (_coreLight != null)
		{
			_coreLight.GlobalPosition = _core.GlobalPosition;
			_coreLight.LightEnergy = _boss.WeakPointExposed ? 1.5f + Mathf.Sin(_time * 12) * 0.2f : 0.28f;
		}
	}

	private void Play(string clip, float speed)
	{
		if (_clip != clip)
		{
			_clip = clip;
			_player.Play(clip, 0.12f);
		}
		_player.SpeedScale = speed;
	}

	private static void ApplyMaterials(Node node)
	{
		ComicMaterials.Apply(node);
		var outline = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/outline.gdshader") };
		outline.SetShaderParameter("pixel_width", 1.35f);
		foreach (Node child in node.FindChildren("*", "MeshInstance3D", true, false))
			((MeshInstance3D)child).MaterialOverlay = outline;
	}
	private static T Find<T>(Node root) where T : Node
	{
		if (root is T found) return found;
		foreach (Node child in root.GetChildren())
		{
			T result = Find<T>(child);
			if (result != null) return result;
		}
		return null;
	}
}
