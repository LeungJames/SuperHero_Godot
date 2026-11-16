using Godot;

/// <summary>
/// First-person locomotion presentation only. Local offsets are metres; rotations are degrees.
/// No input interception, movement writes, camera motion, firing gates, or gameplay timers.
/// </summary>
public sealed class SprintPresentation
{
	public float Blend { get; private set; }
	public Vector3 PosePosition { get; private set; }
	public Vector3 PoseRotation { get; private set; }
	public Vector3 BobPosition { get; private set; }
	public Vector3 BobRotation { get; private set; }

	private float _sprintAmount;
	private float _motionAmount;
	private float _phase;

	/// <summary>
	/// Call once per rendered frame. Suppress while aiming, firing, reloading, attacking,
	/// switching weapons, or unable to act. Pose outputs already include Blend; bob also
	/// contains a smaller walking gait, so it replaces the existing locomotion sway.
	/// </summary>
	public void Update(float delta, PlayerController player, bool suppress, bool longGun = true)
	{
		if (player == null || !GodotObject.IsInstanceValid(player) || player.Data == null)
		{
			Reset();
			return;
		}

		float dt = Mathf.Clamp(delta, 0f, 0.1f);
		bool grounded = player.InputEnabled && player.State == MoveState.Normal && player.IsOnFloor();
		Vector3 velocity = player.GetRealVelocity();
		float speed = new Vector2(velocity.X, velocity.Z).Length();
		bool sprinting = grounded && player.IsSprinting && !suppress;

		// Enter deliberately; interactions recover in 75 ms without delaying their gameplay action.
		float transition = sprinting ? 0.23f : suppress ? 0.075f : 0.16f;
		_sprintAmount = Mathf.MoveToward(_sprintAmount, sprinting ? 1f : 0f, dt / transition);
		Blend = Mathf.SmoothStep(0f, 1f, _sprintAmount);

		float walkSpeed = Mathf.Max(player.Data.WalkSpeed, 0.1f);
		float motionTarget = grounded && !suppress ? Mathf.Clamp(speed / walkSpeed, 0f, 1f) : 0f;
		_motionAmount = Mathf.MoveToward(_motionAmount, motionTarget, dt / (suppress ? 0.06f : 0.14f));

		// A complete left/right stride: slow, restrained walk versus a faster asymmetric sprint.
		float cadence = Mathf.Lerp(1.55f, 2.15f, Blend);
		float speedRatio = Mathf.Clamp(speed / Mathf.Lerp(walkSpeed,
			Mathf.Max(player.Data.SprintSpeed, walkSpeed), Blend), 0.25f, 1.25f);
		_phase = Mathf.PosMod(_phase + dt * Mathf.Tau * cadence * speedRatio * _motionAmount, Mathf.Tau);
		float side = Mathf.Sin(_phase);
		float step = Mathf.Cos(_phase * 2f);
		float push = Mathf.Sin(_phase * 2f + 0.55f);

		// Raise the muzzle and cant the weapon to the right, leaving the central sightline open.
		PosePosition = (longGun ? new Vector3(0.055f, -0.035f, 0.055f)
			: new Vector3(0.075f, -0.015f, 0.035f)) * Blend;
		PoseRotation = (longGun ? new Vector3(48f, -12f, -20f)
			: new Vector3(38f, -10f, -16f)) * Blend;

		BobPosition = new Vector3(
			side * Mathf.Lerp(0.008f, 0.025f, Blend),
			step * Mathf.Lerp(0.005f, 0.018f, Blend),
			push * Mathf.Lerp(0.002f, 0.012f, Blend)) * _motionAmount;
		BobRotation = new Vector3(
			step * Mathf.Lerp(0.3f, 1.7f, Blend),
			side * Mathf.Lerp(0.2f, 0.9f, Blend),
			side * Mathf.Lerp(0.35f, 2.2f, Blend)) * _motionAmount;
	}

	/// <summary>Clear presentation state when rebuilding or unequipping a model.</summary>
	public void Reset()
	{
		_sprintAmount = 0f;
		_motionAmount = 0f;
		_phase = 0f;
		Blend = 0f;
		PosePosition = Vector3.Zero;
		PoseRotation = Vector3.Zero;
		BobPosition = Vector3.Zero;
		BobRotation = Vector3.Zero;
	}
}
