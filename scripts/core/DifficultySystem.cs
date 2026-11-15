using Godot;

/// <summary>
/// 当前难度（2.0 设计 §10.5）。存档加载后由 <see cref="SaveSystem"/> 写入，
/// 敌人侧每帧读取，所以切换难度立即生效，且不触碰玩家生命 / 回血。
/// </summary>
public static class DifficultySystem
{
	private static DifficultyConfig _current = new();

	public static DifficultyConfig Current => _current;

	public static void Set(DifficultyConfig config)
	{
		_current = config ?? new DifficultyConfig();
	}

	/// <summary>敌人反应时间：基础值 + 难度偏移，保底 0.08s 防止负值。</summary>
	public static float ReactionTime(float baseSeconds) =>
		Mathf.Max(0.08f, baseSeconds + _current.ReactionTimeDelta);

	/// <summary>敌人命中散布：基础散布 + 难度加减值，保底 0.5°。</summary>
	public static float ApplySpread(float baseDegrees) =>
		Mathf.Max(0.5f, baseDegrees + _current.SpreadBonusDegrees);

	/// <summary>同屏开火令牌数；0 表示沿用小队配置。</summary>
	public static int FireTokens(int configTokens) =>
		_current.FireTokens > 0 ? _current.FireTokens : configTokens;

	/// <summary>弹药包发数：按难度倍率缩放，至少 1 发。</summary>
	public static int ApplyAmmo(int baseRounds) =>
		Mathf.Max(1, Mathf.RoundToInt(baseRounds * _current.AmmoMultiplier));
}
