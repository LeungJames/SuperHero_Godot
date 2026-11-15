using Godot;
using System;

/// <summary>
/// 收集品拾取物（2.0 设计 §4.2）。复用弹药包的 Area3D 自动拾取交互，
/// 只写叙事进度与 HUD 提示，不提供任何数值收益。
/// </summary>
public partial class CollectiblePickup : Area3D
{
	[Export] public CollectibleData Data { get; set; }
	/// <summary>拾取后由 NarrativeDirector 刷新菜单计数，不做玩法结算。</summary>
	public event Action<CollectiblePickup> Collected;

	private Label3D _label;
	private bool _taken;

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;

		_label = GetNodeOrNull<Label3D>("Label");
		if (_label != null && Data != null)
			_label.Text = Data.Name;

		if (Data != null)
			AddToGroup("collectible");
	}

	public override void _Process(double delta)
	{
		// 轻微自转，让发光体在场景里更易被看到
		RotateY((float)delta * 0.9f);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (_taken || Data == null || body is not PlayerController)
			return;

		_taken = true;
		SaveSystem.Instance?.AddCollectible(Data.Id);

		var hud = GetTree().GetFirstNodeInGroup("hud") as HudController;
		hud?.ShowMessage($"拾取：{Data.Name}", new Color(0.62f, 0.9f, 0.95f));

		Collected?.Invoke(this);
		QueueFree();
	}
}
