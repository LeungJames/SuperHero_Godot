using Godot;

/// <summary>
/// 单局检查点（2.0 设计 §8.5）：只保存在内存里，跨场景重载保留、重新进入章节清空。
/// 只记录「闸门坐标 + 朝向」，不保存敌人、Boss 或玩家血量状态。
/// </summary>
public static class RunCheckpoint
{
	public static bool HasActive { get; private set; }
	public static string ScenePath { get; private set; } = string.Empty;
	public static Vector3 Position { get; private set; }
	public static float Yaw { get; private set; }

	/// <summary>重载场景后是否把玩家放回检查点；由 GameManager.Restart 写入、新场景消费一次。</summary>
	public static bool Pending { get; private set; }

	/// <summary>本次加载是否来自检查点重试；用于跳过已看过的 Boss 登场过场。</summary>
	public static bool ConsumedOnLoad { get; private set; }

	public static void Set(string scenePath, Vector3 position, float yaw)
	{
		if (string.IsNullOrEmpty(scenePath))
			return;

		HasActive = true;
		ConsumedOnLoad = false;
		ScenePath = scenePath;
		Position = position;
		Yaw = yaw;
	}

	/// <summary>只在当前场景与检查点场景一致时请求重试传送。</summary>
	public static void RequestRetry(string currentScene)
	{
		Pending = HasActive && string.Equals(ScenePath, currentScene, System.StringComparison.Ordinal);
	}

	/// <summary>新场景启动时消费一次重试请求。</summary>
	public static bool TryConsume(out Vector3 position, out float yaw)
	{
		position = Position;
		yaw = Yaw;

		if (!Pending)
			return false;

		Pending = false;
		ConsumedOnLoad = true;
		return true;
	}

	public static void Clear()
	{
		HasActive = false;
		Pending = false;
		ConsumedOnLoad = false;
		ScenePath = string.Empty;
		Position = Vector3.Zero;
		Yaw = 0f;
	}
}
