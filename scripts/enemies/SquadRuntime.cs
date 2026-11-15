using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 一支小队的运行时状态：共享情报、槽位分配、后撤计时（2.0 设计 §2.3–§2.5）。
/// 不做单兵行为，行为仍在 EnemyAgent；这里只负责"谁该干什么"。
/// </summary>
public sealed class SquadRuntime
{
	public string Id { get; }
	public SquadConfig Config { get; }
	public SquadDirector Director { get; set; }
	public EnemyAgent Leader { get; private set; }
	public readonly List<EnemyAgent> Members = new();

	/// <summary>队长阵亡后降级：全员原地防守，不再包抄（§2.2）。</summary>
	public bool Broken { get; private set; }

	/// <summary>共享的最后已知位置与置信度（§2.5）。</summary>
	public Vector3 SharedPosition { get; private set; }
	public float SharedConfidence { get; private set; }
	public bool HasSharedContact => SharedConfidence > 0.01f;

	private float _shareDelayLeft;
	private Vector3 _pendingPosition;
	private bool _pending;
	private float _reviewTimer;

	public SquadRuntime(string id, SquadConfig config, EnemyAgent leader)
	{
		Id = id;
		Config = config;
		Leader = leader;
		SharedPosition = leader != null ? leader.GlobalPosition : Vector3.Zero;
		SharedConfidence = 0f;
		Members.Add(leader);
	}

	public void AddMember(EnemyAgent member)
	{
		if (member != null && !Members.Contains(member))
			Members.Add(member);
	}

	public IEnumerable<EnemyAgent> Alive =>
		Members.Where(m => m != null && GodotObject.IsInstanceValid(m) && !m.IsDead);

	public void Tick(float dt, SquadDirector director)
	{
		if (Leader != null && (!GodotObject.IsInstanceValid(Leader) || Leader.IsDead))
		{
			Leader = null;
			Broken = true;
		}

		// 共享情报：喊话延迟 + 置信度衰减
		if (_pending)
		{
			_shareDelayLeft -= dt;
			if (_shareDelayLeft <= 0f)
			{
				_pending = false;
				SharedPosition = _pendingPosition;
				SharedConfidence = 1f;
			}
		}
		else if (SharedConfidence > 0f)
		{
			SharedConfidence = Mathf.Max(0f, SharedConfidence - Config.ShareConfidenceDecay * dt);
		}

		_reviewTimer -= dt;
		if (_reviewTimer <= 0f)
		{
			_reviewTimer = Mathf.Max(0.5f, Config.SlotReviewInterval);
			director.ReviewSlots(this);
		}
	}

	/// <summary>发现者喊话：延迟 <see cref="SquadConfig.ShareDelay"/> 后同步给全队。</summary>
	public void ReportSighting(Vector3 position)
	{
		// 持续看得见时不要每帧重置延迟，否则情报永远同步不出去
		if (!_pending)
		{
			_pending = true;
			_shareDelayLeft = Config.ShareDelay;
		}

		_pendingPosition = position;
	}

	/// <summary>队友从哪个位置开始找：自己看得见就用实时位置，否则用共享情报。</summary>
	public bool TryGetSearchPosition(EnemyAgent asker, out Vector3 position)
	{
		if (asker.HasSight)
		{
			position = asker.PlayerPosition;
			return true;
		}

		if (HasSharedContact)
		{
			position = SharedPosition;
			return true;
		}

		position = Vector3.Zero;
		return false;
	}

	/// <summary>共享情报是否已经衰减到只能保方向（§2.5）。</summary>
	public bool SharedIsDirectionOnly => SharedConfidence > 0f && SharedConfidence < Config.ShareDirectionOnlyConfidence;

	/// <summary>小队里是否有人真正看得见玩家。</summary>
	public bool AnyoneHasSight()
	{
		foreach (EnemyAgent m in Alive)
			if (m.HasSight)
				return true;
		return false;
	}
}
