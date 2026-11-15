using Godot;

/// <summary>
/// 当前关卡的潜行配置（2.0 设计 §3.7）。由 <see cref="StealthDirector"/> 在关卡开始时写入；
/// 噪音半径这类"敌人每帧要读"的参数在这里取值，避免各敌人各自持有一份配置。
/// </summary>
public static class StealthSystem
{
	private static StealthConfig _config = new();

	public static StealthConfig Config => _config;

	public static void Set(StealthConfig config)
	{
		_config = config ?? new StealthConfig();
	}

	/// <summary>不同动作的听觉半径：静步 0、走路 8、冲刺 14、枪声 30（默认值，可在 tres 里改）。</summary>
	public static float NoiseRadius(NoiseKind kind) => kind switch
	{
		NoiseKind.Gunshot => _config.GunshotNoiseRadius,
		NoiseKind.FootstepSprint => _config.SprintNoiseRadius,
		NoiseKind.FootstepWalk => _config.WalkNoiseRadius,
		_ => _config.QuietNoiseRadius,
	};
}
