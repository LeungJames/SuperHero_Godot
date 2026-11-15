using Godot;
using System;

public enum NoiseKind
{
	/// <summary>普通走路：半径较小（2.0 §3.4 / §5.4）。</summary>
	FootstepWalk,
	/// <summary>冲刺：半径更大。</summary>
	FootstepSprint,
	Gunshot,
}

/// <summary>
/// 噪声广播：玩家开枪、走路时向所有敌人广播位置，敌人按潜行配置里的听觉半径判断
/// （2.0 设计 §3.4、§5.4、§9.3）。
/// </summary>
public static class NoiseEmitter
{
	/// <summary>枪声传播半径的兜底值；实际半径由 StealthConfig.GunshotNoiseRadius 决定。</summary>
	public const float GunshotRadius = 30f;

	/// <summary>噪声事件：位置、类型、半径倍率（1 = 原半径）。</summary>
	public static event Action<Vector3, NoiseKind, float> Emitted;

	public static void Emit(Vector3 position, NoiseKind kind, float radiusScale = 1f)
	{
		Emitted?.Invoke(position, kind, radiusScale);
	}
}
