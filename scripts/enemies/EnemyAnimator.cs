using Godot;

/// <summary>
/// 敌人行走 / 站定 / 死亡的程序化驱动（无蒙皮，直接旋转 Blender 导出的枢轴节点）。
///
/// 步态用"步频 × 步幅"解算：步幅由速度反推、摆腿角度再由步幅反解，
/// 相位按步频推进，任何速度下脚都踩在位移上而不是滑行。
/// 站定 ↔ 行走用统一的姿势混合权重过渡，死亡时收成瘫软姿势，不会卡在半步。
/// 枢轴层级：HipsPivot → (TorsoPivot → HeadPivot, ThighPivot → ShinPivot)。
/// 参数在 EnemyData.tres。
/// </summary>
public partial class EnemyAnimator : Node
{
	[Export] public EnemyData Data { get; set; }

	/// <summary>根节点本帧的水平速度（由 EnemyAgent 每物理帧写入；编辑器里也能调来预览步态）。</summary>
	[Export] public float Speed { get; set; }

	private Node3D _hips;
	private Node3D _torso;
	private Node3D _head;
	private Node3D _thighL;
	private Node3D _shinL;
	private Node3D _thighR;
	private Node3D _shinR;

	private Vector3 _hipsRest;

	private float _phase;
	private float _cadence;
	private float _swingDeg;
	private float _moveBlend;
	private float _idleTime;
	private float _deathBlend;
	private bool _dead;
	private bool _ready;

	public override void _Ready()
	{
		Node model = GetParent().GetNodeOrNull<Node>("Model");
		_hips = Find<Node3D>(model, "HipsPivot");
		_torso = Find<Node3D>(model, "TorsoPivot");
		_head = Find<Node3D>(model, "HeadPivot");
		_thighL = Find<Node3D>(model, "ThighLPivot");
		_shinL = Find<Node3D>(model, "ShinLPivot");
		_thighR = Find<Node3D>(model, "ThighRPivot");
		_shinR = Find<Node3D>(model, "ShinRPivot");

		_ready = _hips != null && _torso != null && _thighL != null && _shinL != null
			&& _thighR != null && _shinR != null;
		if (!_ready)
		{
			GD.PushWarning($"{GetParent().Name}: 模型缺枢轴节点，敌人动画关闭。");
			SetProcess(false);
			return;
		}

		_hipsRest = _hips.Position;
	}

	/// <summary>死亡：停步态，交给 EnemyHealth 的倒地与淡出，这里只把四肢收成瘫软姿势。</summary>
	public void SetDead()
	{
		_dead = true;
		Speed = 0f;
	}

	public override void _Process(double delta)
	{
		if (!_ready || Data == null)
			return;

		float dt = (float)delta;
		_idleTime += dt;

		if (_dead)
		{
			_deathBlend = Mathf.MoveToward(_deathBlend, 1f, dt / Mathf.Max(Data.AnimBlendTime, 0.01f));
			ApplyPose(0f, _deathBlend, 0f, 0f);
			if (_deathBlend >= 1f)
				SetProcess(false);
			return;
		}

		float speed = Mathf.Max(Speed, 0f);
		bool moving = speed > 0.12f;

		_moveBlend = Mathf.MoveToward(_moveBlend, moving ? 1f : 0f,
			dt / Mathf.Max(Data.AnimBlendTime, 0.01f));

		if (moving)
		{
			// 步频有上下限；步幅 = 速度 / 步频，再由步幅反解摆腿角度，保证脚不滑
			_cadence = Mathf.Clamp(speed / Mathf.Max(Data.AnimCycleBase, 0.1f),
				Data.AnimCadenceMin, Data.AnimCadenceMax);
			float stride = speed / Mathf.Max(_cadence, 0.01f);
			float ratio = Mathf.Clamp(stride / Mathf.Max(4f * Data.AnimLegLength, 0.01f), 0f, 0.95f);
			_swingDeg = Mathf.RadToDeg(Mathf.Asin(ratio));
			_phase = Mathf.PosMod(_phase + Mathf.Tau * _cadence * dt, Mathf.Tau);
		}

		float speedRatio = Mathf.Clamp(speed / Mathf.Max(Data.PatrolSpeed, 0.1f), 0f, 2.2f);
		ApplyPose(_moveBlend, 0f, _swingDeg, speedRatio);
	}

	/// <summary>把步态姿势写回枢轴。swingDeg 是按本帧速度解出来的摆腿角度。</summary>
	private void ApplyPose(float move, float death, float swingDeg, float speedRatio)
	{
		float swing = Mathf.DegToRad(swingDeg) * move;
		float idle = 1f - move;
		float s = Mathf.Sin(_phase);

		float thighL = swing * s;
		float thighR = -swing * s;
		float kneeL = -Mathf.DegToRad(Data.AnimKneeBend) * Mathf.Max(0f, Mathf.Sin(_phase + 1.15f)) * move;
		float kneeR = -Mathf.DegToRad(Data.AnimKneeBend) * Mathf.Max(0f, Mathf.Sin(_phase + 1.15f + Mathf.Pi)) * move;

		float bob = -Mathf.Abs(s) * Data.AnimBob * move - death * 0.28f;
		float roll = s * Mathf.DegToRad(Data.AnimHipRoll) * move;

		float counter = -s * Mathf.DegToRad(Data.AnimTorsoCounter) * move;
		float lean = -Mathf.DegToRad(Data.AnimLean) * Mathf.Clamp(speedRatio, 0f, 1.6f) * move
			- death * Mathf.DegToRad(22f);
		float breath = Mathf.Sin(_idleTime * 1.7f) * Mathf.DegToRad(1.1f) * idle;

		_thighL.Rotation = new Vector3(thighL + death * Mathf.DegToRad(14f), 0f, 0f);
		_thighR.Rotation = new Vector3(thighR - death * Mathf.DegToRad(8f), 0f, 0f);
		_shinL.Rotation = new Vector3(kneeL - death * Mathf.DegToRad(26f), 0f, 0f);
		_shinR.Rotation = new Vector3(kneeR - death * Mathf.DegToRad(22f), 0f, 0f);

		_hips.Rotation = new Vector3(0f, 0f, roll);
		_hips.Position = _hipsRest + new Vector3(0f, bob, 0f);
		_torso.Rotation = new Vector3(lean + breath, counter, 0f);
		_head.Rotation = new Vector3(-lean * 0.35f, -counter * 0.6f, 0f);
	}

	private static T Find<T>(Node root, string name) where T : Node
	{
		if (root == null)
			return null;
		if (root.Name == name && root is T typed)
			return typed;
		foreach (Node child in root.GetChildren())
		{
			T found = Find<T>(child, name);
			if (found != null)
				return found;
		}
		return null;
	}
}
