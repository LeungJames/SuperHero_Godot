using Godot;
using System;

public enum NoiseKind
{
	Footstep,
	Gunshot,
}

/// <summary>
/// 噪声广播：玩家开枪、走路时向所有敌人广播位置，敌人按自己的听觉半径判断（设计文档 §5.4、§9.3）。
/// </summary>
public static class NoiseEmitter
{
	/// <summary>枪声传播半径（§5.1）。</summary>
	public const float GunshotRadius = 30f;

	public static event Action<Vector3, NoiseKind> Emitted;

	public static void Emit(Vector3 position, NoiseKind kind)
	{
		Emitted?.Invoke(position, kind);
	}
}
