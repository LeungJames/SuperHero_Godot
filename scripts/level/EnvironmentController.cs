using Godot;

/// <summary>
/// 按玩家高度插值雾密度与环境光能量：塔顶明亮开阔、地面浓金（设计文档 §6.8、§9.3）。
/// 太阳角度固定，本脚本不改太阳。
/// </summary>
public partial class EnvironmentController : WorldEnvironment
{
	// 高度 → (雾密度, 环境光能量)，数值取自设计文档 §6.8 的四段表；
	// 环境光按当前 ACES 曝光整体缩放，保持"高度越高越亮"的相对关系。
	private static readonly float[] Heights = { 0f, 10f, 25f, 40f };
	private static readonly float[] FogDensities = { 0.014f, 0.010f, 0.007f, 0.004f };
	private static readonly float[] AmbientEnergies = { 0.30f, 0.34f, 0.40f, 0.45f };

	private Environment _environment;
	private PlayerController _player;

	public override void _Ready()
	{
		_environment = Environment;
		_player = GetNodeOrNull<PlayerController>("../../Actors/Player");
		Apply();
	}

	public override void _Process(double delta) => Apply();

	private void Apply()
	{
		if (_environment == null || _player == null)
			return;

		float y = Mathf.Max(0f, _player.GlobalPosition.Y);
		int index = 0;
		while (index < Heights.Length - 2 && y > Heights[index + 1])
			index++;

		float span = Heights[index + 1] - Heights[index];
		float t = Mathf.Clamp((y - Heights[index]) / span, 0f, 1f);

		_environment.FogDensity = Mathf.Lerp(FogDensities[index], FogDensities[index + 1], t);
		_environment.AmbientLightEnergy = Mathf.Lerp(AmbientEnergies[index], AmbientEnergies[index + 1], t);
	}
}
