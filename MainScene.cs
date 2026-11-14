using Godot;

public partial class MainScene : Node3D
{
	private PlayerHealth _playerHealth;

	public override void _Ready()
	{
		_playerHealth = GetNode<PlayerHealth>("Actors/Player/Health");
		_playerHealth.Died += OnPlayerDied;
	}

	// M1 阶段的临时回路：死亡 1 秒后自动重开，等 M5 由 GameManager 接管结算与重开
	private async void OnPlayerDied(DeathCause cause)
	{
		GD.Print($"[MainScene] 玩家死亡（{cause}），1 秒后重开场景。");
		await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
		GetTree().ReloadCurrentScene();
	}
}
