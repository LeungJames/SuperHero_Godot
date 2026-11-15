using Godot;
using System.Collections.Generic;

/// <summary>
/// 跨局存档与设置（2.0 设计 §10.4、§10.7）。
/// 以 autoload 形式在关卡之前加载 user://save_v2.json，负责：
/// 章节通关标记、难度、灵敏度 / 音量 / 画质、键鼠与手柄绑定。
/// 只保存跨局进度，不保存单局中途状态；不含任何回血 / 生命上限字段（零回血硬约束）。
/// </summary>
public partial class SaveSystem : Node
{
	public const string SavePath = "user://save_v2.json";
	/// <summary>单局指标日志（每行一条 JSON），供评级与平衡复盘导出。</summary>
	public const string RunLogPath = "user://runs_v2.jsonl";
	public const int Version = 2;

	public static readonly string[] RebindableActions =
	{
		"move_forward", "move_back", "move_left", "move_right",
		"jump", "sprint", "crouch", "walk_slow",
		"fire", "aim", "reload", "melee", "execution",
		"camera_mode", "pause",
	};

	public readonly struct ChapterInfo
	{
		public ChapterInfo(string id, string label, string scene, string requires)
		{
			Id = id;
			Label = label;
			Scene = scene;
			Requires = requires;
		}

		public string Id { get; }
		public string Label { get; }
		public string Scene { get; }
		/// <summary>解锁前置章节；空表示初始解锁。</summary>
		public string Requires { get; }
	}

	public static readonly ChapterInfo[] Chapters =
	{
		new("port", "第一章 · 黄昏港口", "res://mainScene.tscn", ""),
		new("factory", "第二章 · 赤蛇工厂", "res://scenes/FactoryScene.tscn", "port"),
		new("anchor", "第三章 · 铁锚据点", "res://scenes/AnchorScene.tscn", "factory"),
	};

	public static SaveSystem Instance { get; private set; }
	public static bool HasInstance => Instance != null && GodotObject.IsInstanceValid(Instance);

	public static float MouseSensitivitySetting => HasInstance ? Instance.MouseSensitivity : 0.12f;
	public static float AdsSensitivitySetting => HasInstance ? Instance.AdsSensitivityScale : 0.7f;
	public static float GamepadSensitivitySetting => HasInstance ? Instance.GamepadSensitivity : 1f;

	// ------------------------------------------------------------ 运行时设置

	public float MouseSensitivity { get; private set; } = 0.12f;
	public float AdsSensitivityScale { get; private set; } = 0.7f;
	public float GamepadSensitivity { get; private set; } = 1f;

	public float MasterVolume { get; private set; } = 1f;
	public float SfxVolume { get; private set; } = 0.9f;
	public float AmbientVolume { get; private set; } = 0.8f;
	public float RadioVolume { get; private set; } = 0.9f;

	public bool Fullscreen { get; private set; }
	/// <summary>0 关闭 / 1 = 2x / 2 = 4x。</summary>
	public int Msaa { get; private set; } = 1;
	public bool Shadows { get; private set; } = true;

	public string DifficultyId { get; private set; } = "normal";

	private Godot.Collections.Dictionary _data = new();
	private readonly Godot.Collections.Dictionary _chapters = new();
	private readonly Dictionary<string, Godot.Collections.Array<InputEvent>> _defaults = new();
	private readonly Dictionary<string, Godot.Collections.Array<InputEvent>> _overrides = new();

	public override void _EnterTree()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _Ready()
	{
		EnsureAudioBuses();
		SnapshotDefaultBindings();
		Load();
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	// ------------------------------------------------------------ 章节

	public bool IsChapterCleared(string id)
	{
		if (string.IsNullOrEmpty(id))
			return false;
		if (!_chapters.TryGetValue(id, out Variant value) || value.VariantType != Variant.Type.Dictionary)
			return false;
		return GetBool(value.AsGodotDictionary(), "cleared", false);
	}

	public float GetBestTime(string id)
	{
		if (string.IsNullOrEmpty(id))
			return 0f;
		if (!_chapters.TryGetValue(id, out Variant value) || value.VariantType != Variant.Type.Dictionary)
			return 0f;
		return GetFloat(value.AsGodotDictionary(), "best_time", 0f);
	}

	public bool IsChapterUnlocked(string id)
	{
		foreach (ChapterInfo chapter in Chapters)
		{
			if (chapter.Id != id)
				continue;
			return string.IsNullOrEmpty(chapter.Requires) || IsChapterCleared(chapter.Requires);
		}

		return false;
	}

	/// <summary>通关时写入；最佳时间只保留更短的一次。</summary>
	public void MarkChapterCleared(string id, float time)
	{
		if (string.IsNullOrEmpty(id))
			return;

		if (!_chapters.TryGetValue(id, out Variant value) || value.VariantType != Variant.Type.Dictionary)
			_chapters[id] = new Godot.Collections.Dictionary();

		Godot.Collections.Dictionary entry = _chapters[id].AsGodotDictionary();
		entry["cleared"] = true;

		float best = GetFloat(entry, "best_time", 0f);
		if (best <= 0f || time < best)
			entry["best_time"] = time;

		Write();
	}

	// ------------------------------------------------------------ 难度

	/// <summary>单局结算埋点：追加一行 JSON，不参与存档校验，也不改任何玩法数值。</summary>
	public void AppendRunRecord(string chapter, bool victory, float time, int kills, int total,
		int shots, int hits, int headshots, string deathCause)
	{
		var record = new Godot.Collections.Dictionary();
		record["chapter"] = chapter;
		record["victory"] = victory;
		record["time"] = time;
		record["kills"] = kills;
		record["total"] = total;
		record["shots"] = shots;
		record["hits"] = hits;
		record["headshots"] = headshots;
		record["death_cause"] = deathCause ?? string.Empty;
		record["difficulty"] = DifficultyId;

		FileAccess file;
		if (FileAccess.FileExists(RunLogPath))
		{
			file = FileAccess.Open(RunLogPath, FileAccess.ModeFlags.ReadWrite);
			file?.SeekEnd();
		}
		else
		{
			file = FileAccess.Open(RunLogPath, FileAccess.ModeFlags.Write);
		}

		using (file)
		{
			file?.StoreLine(Json.Stringify(record));
		}
	}

	public void SetDifficulty(string id)
	{
		if (string.IsNullOrEmpty(id))
			return;

		DifficultyId = id;
		ApplyDifficulty();
		Write();
	}

	private void ApplyDifficulty()
	{
		string path = $"res://data/difficulty/{DifficultyId}.tres";
		DifficultyConfig config = ResourceLoader.Exists(path) ? GD.Load<DifficultyConfig>(path) : null;
		DifficultySystem.Set(config);
		GD.Print($"[Difficulty] id={DifficultyId} reactionDelta={DifficultySystem.Current.ReactionTimeDelta} " +
			$"spreadBonus={DifficultySystem.Current.SpreadBonusDegrees} tokens={DifficultySystem.Current.FireTokens} " +
			$"ammoX={DifficultySystem.Current.AmmoMultiplier}");
	}

	// ------------------------------------------------------------ 设置应用

	public void SetMouseSensitivity(float value)
	{
		MouseSensitivity = Mathf.Clamp(value, 0.02f, 0.6f);
	}

	public void SetAdsSensitivityScale(float value)
	{
		AdsSensitivityScale = Mathf.Clamp(value, 0.2f, 1f);
	}

	public void SetGamepadSensitivity(float value)
	{
		GamepadSensitivity = Mathf.Clamp(value, 0.3f, 3f);
	}

	public void SetMasterVolume(float value)
	{
		MasterVolume = Mathf.Clamp(value, 0f, 1f);
		ApplyVolumes();
	}

	public void SetSfxVolume(float value)
	{
		SfxVolume = Mathf.Clamp(value, 0f, 1f);
		ApplyVolumes();
	}

	public void SetAmbientVolume(float value)
	{
		AmbientVolume = Mathf.Clamp(value, 0f, 1f);
		ApplyVolumes();
	}

	public void SetRadioVolume(float value)
	{
		RadioVolume = Mathf.Clamp(value, 0f, 1f);
		ApplyVolumes();
	}

	public void SetFullscreen(bool value)
	{
		Fullscreen = value;
		ApplyGraphics();
		Write();
	}

	public void SetMsaa(int value)
	{
		Msaa = Mathf.Clamp(value, 0, 2);
		ApplyGraphics();
		Write();
	}

	public void SetShadows(bool value)
	{
		Shadows = value;
		ApplyShadows();
		Write();
	}

	/// <summary>设置改动后落盘（滑条拖动结束时调用，避免每帧写文件）。</summary>
	public void SaveSettings() => Write();

	public void ResetSettings()
	{
		MouseSensitivity = 0.12f;
		AdsSensitivityScale = 0.7f;
		GamepadSensitivity = 1f;
		MasterVolume = 1f;
		SfxVolume = 0.9f;
		AmbientVolume = 0.8f;
		RadioVolume = 0.9f;
		Fullscreen = false;
		Msaa = 1;
		Shadows = true;

		ApplyVolumes();
		ApplyGraphics();
		ApplyShadows();
		Write();
	}

	public void ApplyGraphics()
	{
		DisplayServer.WindowSetMode(Fullscreen
			? DisplayServer.WindowMode.Fullscreen
			: DisplayServer.WindowMode.Windowed);

		Viewport root = GetTree()?.Root;
		if (root == null)
			return;

		root.Msaa3D = Msaa switch
		{
			0 => Viewport.Msaa.Disabled,
			2 => Viewport.Msaa.Msaa4X,
			_ => Viewport.Msaa.Msaa2X,
		};
	}

	/// <summary>打开 / 关闭方向光阴影；每关的 EnvironmentController 进树时也会调用一次。</summary>
	public void ApplyShadows()
	{
		Node root = GetTree()?.Root;
		if (root == null)
			return;

		foreach (Node node in root.FindChildren("*", "DirectionalLight3D", true, false))
		{
			if (node is DirectionalLight3D light)
				light.ShadowEnabled = Shadows;
		}
	}

	private void ApplyVolumes()
	{
		SetBusVolume("Master", MasterVolume);
		SetBusVolume("SFX", SfxVolume);
		SetBusVolume("Ambient", AmbientVolume);
		SetBusVolume("Radio", RadioVolume);
	}

	private static void SetBusVolume(string bus, float linear)
	{
		int index = AudioServer.GetBusIndex(bus);
		if (index < 0)
			return;

		AudioServer.SetBusVolumeDb(index, linear <= 0.001f ? -80f : Mathf.LinearToDb(linear));
	}

	private static void EnsureAudioBuses()
	{
		foreach (string bus in new[] { "SFX", "Ambient", "Radio" })
		{
			if (AudioServer.GetBusIndex(bus) >= 0)
				continue;

			AudioServer.AddBus();
			int index = AudioServer.BusCount - 1;
			AudioServer.SetBusName(index, bus);
			AudioServer.SetBusSend(index, "Master");
		}
	}

	// ------------------------------------------------------------ 按键绑定

	/// <summary>把一次重绑写进 InputMap 并落盘。</summary>
	public void StoreBinding(string action, Godot.Collections.Array<InputEvent> events)
	{
		if (!InputMap.HasAction(action))
			return;

		InputMap.ActionEraseEvents(action);
		var copy = new Godot.Collections.Array<InputEvent>();
		foreach (InputEvent evt in events)
		{
			copy.Add(evt);
			InputMap.ActionAddEvent(action, evt);
		}

		_overrides[action] = copy;
		Write();
	}

	public void ResetBindings()
	{
		foreach (KeyValuePair<string, Godot.Collections.Array<InputEvent>> pair in _defaults)
		{
			InputMap.ActionEraseEvents(pair.Key);
			foreach (InputEvent evt in pair.Value)
				InputMap.ActionAddEvent(pair.Key, evt);
		}

		_overrides.Clear();
		Write();
	}

	/// <summary>同一设备类型上的冲突检测；返回占用该按键的动作名，无冲突返回 null。</summary>
	public string FindConflict(string action, InputEvent candidate)
	{
		foreach (string other in RebindableActions)
		{
			if (other == action || !InputMap.HasAction(other))
				continue;

			foreach (InputEvent existing in InputMap.ActionGetEvents(other))
			{
				if (IsSameBinding(existing, candidate))
					return other;
			}
		}

		return null;
	}

	public static bool SameDevice(InputEvent a, InputEvent b) => (a, b) switch
	{
		(InputEventKey, InputEventKey) => true,
		(InputEventMouseButton, InputEventMouseButton) => true,
		(InputEventJoypadButton, InputEventJoypadButton) => true,
		(InputEventJoypadMotion, InputEventJoypadMotion) => true,
		_ => false,
	};

	private static bool IsSameBinding(InputEvent a, InputEvent b) => (a, b) switch
	{
		(InputEventKey ka, InputEventKey kb) => KeyOf(ka) == KeyOf(kb),
		(InputEventMouseButton ma, InputEventMouseButton mb) => ma.ButtonIndex == mb.ButtonIndex,
		(InputEventJoypadButton ja, InputEventJoypadButton jb) => ja.ButtonIndex == jb.ButtonIndex,
		(InputEventJoypadMotion aa, InputEventJoypadMotion ab) =>
			aa.Axis == ab.Axis && Mathf.Sign(aa.AxisValue) == Mathf.Sign(ab.AxisValue),
		_ => false,
	};

	private static Key KeyOf(InputEventKey key) =>
		key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;

	private void SnapshotDefaultBindings()
	{
		foreach (string action in RebindableActions)
		{
			if (!InputMap.HasAction(action))
				continue;

			var copy = new Godot.Collections.Array<InputEvent>();
			foreach (InputEvent evt in InputMap.ActionGetEvents(action))
				copy.Add(evt.Duplicate() as InputEvent ?? evt);

			_defaults[action] = copy;
		}
	}

	private void ApplySavedBindings()
	{
		if (!_data.TryGetValue("bindings", out Variant saved) || saved.VariantType != Variant.Type.Dictionary)
			return;

		Godot.Collections.Dictionary bindings = saved.AsGodotDictionary();
		foreach (string action in RebindableActions)
		{
			if (!bindings.TryGetValue(action, out Variant encoded) || encoded.VariantType != Variant.Type.Array)
				continue;

			Godot.Collections.Array<InputEvent> events = DecodeEvents(encoded.AsGodotArray());
			if (events.Count == 0 || !InputMap.HasAction(action))
				continue;

			InputMap.ActionEraseEvents(action);
			foreach (InputEvent evt in events)
				InputMap.ActionAddEvent(action, evt);

			_overrides[action] = events;
		}
	}

	private static Godot.Collections.Dictionary EncodeEvent(InputEvent evt)
	{
		var encoded = new Godot.Collections.Dictionary();

		switch (evt)
		{
			case InputEventKey key:
				encoded["type"] = "key";
				encoded["keycode"] = (int)KeyOf(key);
				return encoded;
			case InputEventMouseButton mouse:
				encoded["type"] = "mouse";
				encoded["button"] = (int)mouse.ButtonIndex;
				return encoded;
			case InputEventJoypadButton joy:
				encoded["type"] = "joy_button";
				encoded["button"] = (int)joy.ButtonIndex;
				return encoded;
			case InputEventJoypadMotion axis:
				encoded["type"] = "joy_axis";
				encoded["axis"] = (int)axis.Axis;
				encoded["value"] = axis.AxisValue;
				return encoded;
			default:
				return null;
		}
	}

	private static Godot.Collections.Array<InputEvent> DecodeEvents(Godot.Collections.Array raw)
	{
		var events = new Godot.Collections.Array<InputEvent>();

		foreach (Variant item in raw)
		{
			if (item.VariantType != Variant.Type.Dictionary)
				continue;

			Godot.Collections.Dictionary encoded = item.AsGodotDictionary();
			switch (GetString(encoded, "type", string.Empty))
			{
				case "key":
					events.Add(new InputEventKey { PhysicalKeycode = (Key)GetInt(encoded, "keycode", 0) });
					break;
				case "mouse":
					events.Add(new InputEventMouseButton
					{
						ButtonIndex = (MouseButton)GetInt(encoded, "button", (int)MouseButton.Left),
					});
					break;
				case "joy_button":
					events.Add(new InputEventJoypadButton
					{
						ButtonIndex = (JoyButton)GetInt(encoded, "button", 0),
					});
					break;
				case "joy_axis":
					events.Add(new InputEventJoypadMotion
					{
						Axis = (JoyAxis)GetInt(encoded, "axis", 0),
						AxisValue = GetFloat(encoded, "value", 0f),
					});
					break;
			}
		}

		return events;
	}

	// ------------------------------------------------------------ 文件读写

	private void Load()
	{
		if (FileAccess.FileExists(SavePath))
		{
			using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
			if (file != null)
			{
				Variant parsed = Json.ParseString(file.GetAsText());
				if (parsed.VariantType == Variant.Type.Dictionary)
					_data = parsed.AsGodotDictionary();
				else
					GD.PushWarning($"SaveSystem: {SavePath} 内容不是字典，按新存档处理。");
			}
		}

		ReadSettings();
		ApplyDifficulty();
		ApplyVolumes();
		ApplyGraphics();
		ApplySavedBindings();
	}

	private void ReadSettings()
	{
		Godot.Collections.Dictionary settings = GetDictionary(_data, "settings");
		MouseSensitivity = Mathf.Clamp(GetFloat(settings, "mouse_sensitivity", MouseSensitivity), 0.02f, 0.6f);
		AdsSensitivityScale = Mathf.Clamp(GetFloat(settings, "ads_sensitivity_scale", AdsSensitivityScale), 0.2f, 1f);
		GamepadSensitivity = Mathf.Clamp(GetFloat(settings, "gamepad_sensitivity", GamepadSensitivity), 0.3f, 3f);
		MasterVolume = Mathf.Clamp(GetFloat(settings, "master_volume", MasterVolume), 0f, 1f);
		SfxVolume = Mathf.Clamp(GetFloat(settings, "sfx_volume", SfxVolume), 0f, 1f);
		AmbientVolume = Mathf.Clamp(GetFloat(settings, "ambient_volume", AmbientVolume), 0f, 1f);
		RadioVolume = Mathf.Clamp(GetFloat(settings, "radio_volume", RadioVolume), 0f, 1f);
		Fullscreen = GetBool(settings, "fullscreen", Fullscreen);
		Msaa = Mathf.Clamp(GetInt(settings, "msaa", Msaa), 0, 2);
		Shadows = GetBool(settings, "shadows", Shadows);

		Godot.Collections.Dictionary chapters = GetDictionary(_data, "chapters");
		foreach (Variant key in chapters.Keys)
			_chapters[key] = chapters[key];

		DifficultyId = GetString(_data, "difficulty", DifficultyId);
	}

	private void Write()
	{
		var settings = new Godot.Collections.Dictionary();
		settings["mouse_sensitivity"] = MouseSensitivity;
		settings["ads_sensitivity_scale"] = AdsSensitivityScale;
		settings["gamepad_sensitivity"] = GamepadSensitivity;
		settings["master_volume"] = MasterVolume;
		settings["sfx_volume"] = SfxVolume;
		settings["ambient_volume"] = AmbientVolume;
		settings["radio_volume"] = RadioVolume;
		settings["fullscreen"] = Fullscreen;
		settings["msaa"] = Msaa;
		settings["shadows"] = Shadows;

		_data["version"] = Version;
		_data["settings"] = settings;
		_data["difficulty"] = DifficultyId;
		_data["chapters"] = _chapters;

		if (_overrides.Count > 0)
		{
			var bindings = new Godot.Collections.Dictionary();
			foreach (KeyValuePair<string, Godot.Collections.Array<InputEvent>> pair in _overrides)
			{
				var encoded = new Godot.Collections.Array();
				foreach (InputEvent evt in pair.Value)
				{
					Godot.Collections.Dictionary item = EncodeEvent(evt);
					if (item != null)
						encoded.Add(item);
				}

				bindings[pair.Key] = encoded;
			}

			_data["bindings"] = bindings;
		}
		else
		{
			_data.Remove("bindings");
		}

		using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PushWarning($"SaveSystem: 无法写入 {SavePath}（{FileAccess.GetOpenError()}）。");
			return;
		}

		file.StoreString(Json.Stringify(_data, "  "));
	}

	// ------------------------------------------------------------ 取值辅助

	private static Godot.Collections.Dictionary GetDictionary(Godot.Collections.Dictionary dict, string key)
	{
		if (dict != null && dict.TryGetValue(key, out Variant value) && value.VariantType == Variant.Type.Dictionary)
			return value.AsGodotDictionary();

		return new Godot.Collections.Dictionary();
	}

	private static float GetFloat(Godot.Collections.Dictionary dict, string key, float fallback)
	{
		if (dict == null || !dict.TryGetValue(key, out Variant value))
			return fallback;

		return value.VariantType switch
		{
			Variant.Type.Float => (float)value.AsDouble(),
			Variant.Type.Int => value.AsInt32(),
			_ => fallback,
		};
	}

	private static int GetInt(Godot.Collections.Dictionary dict, string key, int fallback)
	{
		if (dict == null || !dict.TryGetValue(key, out Variant value))
			return fallback;

		return value.VariantType switch
		{
			Variant.Type.Int => value.AsInt32(),
			Variant.Type.Float => Mathf.RoundToInt((float)value.AsDouble()),
			_ => fallback,
		};
	}

	private static bool GetBool(Godot.Collections.Dictionary dict, string key, bool fallback)
	{
		if (dict == null || !dict.TryGetValue(key, out Variant value))
			return fallback;

		return value.VariantType == Variant.Type.Bool ? value.AsBool() : fallback;
	}

	private static string GetString(Godot.Collections.Dictionary dict, string key, string fallback)
	{
		if (dict == null || !dict.TryGetValue(key, out Variant value))
			return fallback;

		return value.VariantType == Variant.Type.String ? value.AsString() : fallback;
	}
}
