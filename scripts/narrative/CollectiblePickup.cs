using Godot;
using System;

/// <summary>
/// 收集品拾取物（2.0 设计 §4.2）。复用弹药包的 Area3D 自动拾取交互，
/// 只写叙事进度，以短促的光点和声音确认拾取；名称与内容留在档案菜单。
/// </summary>
public partial class CollectiblePickup : Area3D
{
	[Export] public CollectibleData Data { get; set; }
	/// <summary>拾取后由 NarrativeDirector 刷新菜单计数，不做玩法结算。</summary>
	public event Action<CollectiblePickup> Collected;

	private Node3D _presentation;
	private bool _taken;

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
		_presentation = GetNodeOrNull<Node3D>("Presentation");

		if (Data != null)
			AddToGroup("collectible");
	}

	public override void _Process(double delta)
	{
		// Slow rotation exposes the object's modeled depth without moving the pickup area.
		_presentation?.RotateY((float)delta * 0.32f);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (_taken || Data == null || body is not PlayerController)
			return;

		_taken = true;
		SaveSystem.Instance?.AddCollectible(Data.Id);

		var flash = new CollectiblePickupFlash();
		GetParent().AddChild(flash);
		flash.GlobalPosition = GlobalPosition;
		AudioDirector.Instance?.PlayWorld(SfxKind.ReloadSeat, GlobalPosition, -12f, 1.8f);

		Collected?.Invoke(this);
		QueueFree();
	}
}
