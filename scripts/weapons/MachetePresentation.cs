using Godot;

/// <summary>Two deliberate slashes with a recovery tail; damage remains owned by MeleeController.</summary>
public partial class MachetePresentation : Node
{
	private Node3D _root;
	private WeaponController _weapon;
	private MeleeController _melee;
	private Camera3D _camera;
	private Node3D _wrist;
	private Node3D _forearm;
	private int _sequence;
	private bool _swinging;
	private float _idleTime;
	private float _guard;
	private Transform3D _start;
	private Vector3 _bladeAxis;

	public void Initialize(Node3D root, WeaponController weapon, MeleeController melee, Camera3D camera)
	{
		_root = root; _weapon = weapon; _melee = melee; _camera = camera;
		_wrist = root.FindChild("KnifeWristR", true, false) as Node3D;
		_forearm = root.FindChild("ForearmRPivot", true, false) as Node3D;
		var tip = root.FindChild("BladeTip", true, false) as Node3D;
		var heel = root.FindChild("BladeHeel", true, false) as Node3D;
		_bladeAxis = tip != null && heel != null
			? (root.GlobalBasis.Inverse() * (tip.GlobalPosition - heel.GlobalPosition)).Normalized()
			: new Vector3(-.139f, .743f, -.655f).Normalized();
		Reset();
	}

	public void Reset()
	{
		_sequence = _melee?.SwingSequence ?? 0;
		_swinging = false;
		_guard = 0;
		if (_root != null) _root.Transform = Pose(Vector3.Zero, Vector3.Zero);
	}

	public void Advance(float dt)
	{
		_idleTime += dt;
		_guard = Mathf.MoveToward(_guard, _weapon.IsBlocking ? 1f : 0f, dt * 12f);
		if (_melee != null && _melee.SwingSequence != _sequence && !_weapon.IsSwapping)
		{
			_sequence = _melee.SwingSequence;
			_start = _root.Transform;
			_swinging = true;
		}
		Transform3D idle = Pose(new Vector3(Mathf.Sin(_idleTime * 1.7f) * .003f, Mathf.Cos(_idleTime * 2.1f) * .004f, 0), Vector3.Zero);
		if (_swinging && _melee?.Data != null)
		{
			float elapsed = _melee.SwingElapsed;
			float hit = Mathf.Max(_melee.Data.SwingTime * _melee.Data.HitTimeRatio, .04f);
			float windup = hit * .5f, follow = hit + .085f;
			float end = Mathf.Max(_melee.Data.SwingTime + .18f, follow + .20f);
			bool returning = _sequence % 2 == 0;
			Transform3D ready = returning
				? Pose(new Vector3(-.17f, -.055f, -.06f), new Vector3(8, 8, 90))
				: Pose(new Vector3(.06f, .015f, .02f), new Vector3(-12, -14, -32));
			Transform3D contact = returning
				? Pose(new Vector3(-.04f, .01f, -.12f), new Vector3(8, -12, 45))
				: Pose(new Vector3(-.075f, .04f, -.14f), new Vector3(5, 20, 52));
			Transform3D finish = returning
				? Pose(new Vector3(.13f, .06f, -.03f), new Vector3(-15, -16, -38))
				: Pose(new Vector3(-.20f, -.09f, -.09f), new Vector3(-20, 5, 100));
			if (returning)
			{
				// Turn the single cutting edge into the reverse stroke during the windup.
				Basis turn = new(_bladeAxis, Mathf.Pi);
				ready.Basis *= turn;
				contact.Basis *= turn;
				finish.Basis *= turn;
			}
			if (elapsed < windup) _root.Transform = Blend(_start, ready, elapsed / windup);
			else if (elapsed < hit) _root.Transform = Blend(ready, contact, (elapsed - windup) / (hit - windup));
			else if (elapsed < follow) _root.Transform = Blend(contact, finish, (elapsed - hit) / (follow - hit));
			else if (elapsed < end) _root.Transform = Blend(finish, idle, (elapsed - follow) / (end - follow));
			else { _swinging = false; _root.Transform = idle; }
		}
		else _root.Transform = Blend(idle, Pose(new Vector3(-.10f, .075f, -.09f), new Vector3(-8, -12, 62)), _guard);
		UpdateArm();
	}

	private Transform3D Pose(Vector3 offset, Vector3 degrees) => new(
		Basis.FromEuler(degrees * (Mathf.Pi / 180f)), _weapon.KnifeGripOffset + offset);
	private static Transform3D Blend(Transform3D from, Transform3D to, float t) =>
		from.InterpolateWith(to, Mathf.SmoothStep(0, 1, Mathf.Clamp(t, 0, 1)));

	private void UpdateArm()
	{
		if (_wrist == null || _forearm == null) return;
		Vector3 direction = _camera.ToGlobal(new Vector3(.36f, -.38f, -.10f)) - _wrist.GlobalPosition;
		float length = Mathf.Max(direction.Length(), .001f);
		Vector3 z = direction / length;
		Vector3 reference = Mathf.Abs(_camera.GlobalBasis.Y.Dot(z)) > .98f ? _camera.GlobalBasis.X : _camera.GlobalBasis.Y;
		Vector3 x = reference.Cross(z).Normalized();
		_forearm.GlobalTransform = new Transform3D(new Basis(x, z.Cross(x).Normalized(), z * (length / .36f)), _wrist.GlobalPosition);
	}
}
