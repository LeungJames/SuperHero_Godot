using Godot;

/// <summary>
/// 叙事总控（2.0 设计 §4）。按关卡生成收集品、无线电台词与漫画过场；
/// 只读数据与写存档标记，不修改任何玩法数值。
/// </summary>
public partial class NarrativeDirector : Node
{
	[Export] public string ChapterId { get; set; } = "port";
	[Export] public string GangId { get; set; } = "salt";
	[Export] public CutsceneData OpeningCutscene { get; set; }
	/// <summary>Boss 两段过场只在调试预览可用，正式触发留给 M7。</summary>
	[Export] public bool DebugCutscenePreview { get; set; }
	[Export] public CollectibleIndex Collectibles { get; set; }
	[Export] public PackedScene CollectibleScene { get; set; }

	public RadioBarkPlayer Radio { get; private set; }
	public CutsceneComicPlayer Cutscenes { get; private set; }
	public int SpawnedCollectibles { get; private set; }
	public bool OpeningPlayed { get; private set; }

	private const string IndexPath = "res://data/narrative/collectibles/index.tres";
	private const string PickupScenePath = "res://scenes/narrative/CollectiblePickup.tscn";
	private const string BossIntroPath = "res://data/narrative/cutscenes/boss_intro.tres";
	private const string BossDeathPath = "res://data/narrative/cutscenes/boss_death.tres";

	public override void _Ready()
	{
		AddToGroup("narrative_director");

		Radio = new RadioBarkPlayer { Name = "RadioBark", GangId = GangId };
		AddChild(Radio);

		Cutscenes = new CutsceneComicPlayer { Name = "CutscenePlayer" };
		AddChild(Cutscenes);

		SpawnCollectibles();
		CallDeferred(nameof(PlayOpening));
	}

	// ---------------------------------------------------------------- 收集品

	private void SpawnCollectibles()
	{
		CollectibleIndex index = Collectibles ??
			(ResourceLoader.Exists(IndexPath) ? GD.Load<CollectibleIndex>(IndexPath) : null);
		if (index == null)
		{
			GD.PushWarning("NarrativeDirector: 收集品索引缺失。");
			return;
		}

		PackedScene scene = CollectibleScene ??
			(ResourceLoader.Exists(PickupScenePath) ? GD.Load<PackedScene>(PickupScenePath) : null);
		if (scene == null)
		{
			GD.PushWarning("NarrativeDirector: 收集品场景缺失。");
			return;
		}

		foreach (CollectibleData item in index.Items)
		{
			if (item == null || item.ChapterId != ChapterId)
				continue;
			if (SaveSystem.Instance?.HasCollectible(item.Id) ?? false)
				continue;

			if (scene.Instantiate() is not CollectiblePickup pickup)
				continue;

			pickup.Data = item;
			pickup.Name = $"Collectible_{item.Id.Replace('-', '_')}";
			// 场景根在 _Ready 期间仍在建子节点，挂到本节点下更安全
			AddChild(pickup);
			pickup.GlobalPosition = item.Position;
			SpawnedCollectibles++;
		}

		GD.Print($"[Narrative] chapter={ChapterId} gang={GangId} collectibles={SpawnedCollectibles}");
	}

	// ---------------------------------------------------------------- 过场

	private void PlayOpening()
	{
		if (OpeningCutscene == null || OpeningCutscene.Panels.Count == 0)
			return;

		// 看过一次就跳过；Boss 两段不在这里，走调试预览
		if (SaveSystem.Instance?.IsCutsceneSeen(OpeningCutscene.Id) ?? false)
			return;

		OpeningPlayed = true;
		Cutscenes.Play(OpeningCutscene);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!DebugCutscenePreview || Cutscenes == null || Cutscenes.IsPlaying)
			return;
		if (@event is not InputEventKey { Pressed: true, Echo: false } key)
			return;

		string path = key.Keycode switch
		{
			Key.F6 => BossIntroPath,
			Key.F7 => BossDeathPath,
			_ => string.Empty,
		};

		if (string.IsNullOrEmpty(path))
			return;

		GetViewport().SetInputAsHandled();
		DebugPreviewBoss(key.Keycode == Key.F6);
	}

	/// <summary>Boss 两段过场的调试预览（F6 / F7）；不会标记为已看。</summary>
	public bool DebugPreviewBoss(bool intro)
	{
		if (!DebugCutscenePreview || Cutscenes == null || Cutscenes.IsPlaying)
			return false;

		string path = intro ? BossIntroPath : BossDeathPath;
		if (!ResourceLoader.Exists(path))
			return false;

		Cutscenes.Play(GD.Load<CutsceneData>(path), markSeen: false);
		return Cutscenes.IsPlaying;
	}

	/// <summary>正式播放 Boss 登场过场（M7）。看过一次后由 CutsceneComicPlayer 允许跳过。</summary>
	public bool PlayBossIntro() => PlayBossCutscene(BossIntroPath);

	/// <summary>正式播放 Boss 击败过场（M7）。</summary>
	public bool PlayBossDeath() => PlayBossCutscene(BossDeathPath);

	public bool BossCutscenePlaying => Cutscenes != null && Cutscenes.IsPlaying;

	private bool PlayBossCutscene(string path)
	{
		if (Cutscenes == null || Cutscenes.IsPlaying || !ResourceLoader.Exists(path))
			return false;

		CutsceneData data = GD.Load<CutsceneData>(path);
		if (data == null || data.Panels == null || data.Panels.Count == 0)
			return false;

		Cutscenes.Play(data);
		return Cutscenes.IsPlaying;
	}
}
