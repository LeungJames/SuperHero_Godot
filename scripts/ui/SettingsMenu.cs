using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 设置菜单（2.0 设计 §10.1、§10.2、§10.7）。
/// 灵敏度 / 音量 / 画质 / 控制四页；控制页负责键鼠与手柄重绑、冲突检测与恢复默认。
/// 所有改动立即生效并写入 SaveSystem，不触碰任何玩家生命或回血参数。
/// </summary>
public partial class SettingsMenu : Control
{
	public event Action Closed;

	private sealed class RebindRow
	{
		public string Action;
		public Button Key;
		public Button Pad;
	}

	private static readonly (string Id, string Label)[] RebindDefinitions =
	{
		("move_forward", "前进"),
		("move_back", "后退"),
		("move_left", "左移"),
		("move_right", "右移"),
		("jump", "跳跃 / 二段跳"),
		("sprint", "冲刺"),
		("dash", "空中冲刺"),
		("grapple", "钩锁"),
		("crouch", "蹲下 / 滑铲"),
		("walk_slow", "慢走"),
		("fire", "开火"),
		("aim", "瞄准 / 刀格挡"),
		("reload", "换弹"),
		("melee", "近战 / 剑气（长按或双击）"),
		("execution", "处决 / 交互"),
		("camera_mode", "切换视角"),
		("pause", "暂停"),
	};

	private static readonly Dictionary<string, string> LabelById = BuildLabelMap();

	private TabContainer _tabs;
	private Label _status;
	private Button _closeButton;
	private readonly List<RebindRow> _rows = new();

	private string _captureAction;
	private Button _captureButton;
	private string _captureIdleText;

	public override void _Ready()
	{
		Visible = false;
		_tabs = GetNode<TabContainer>("Panel/VBox/Tabs");
		_status = GetNode<Label>("Panel/VBox/Status");
		_closeButton = GetNode<Button>("Panel/VBox/Header/CloseButton");
		_closeButton.Pressed += Close;

		BuildSensitivityTab();
		BuildVolumeTab();
		BuildGraphicsTab();
		BuildControlsTab();
	}

	// ------------------------------------------------------------ 开关

	public void Open()
	{
		if (Visible)
			return;

		Visible = true;
		CancelCapture();
		_status.Text = string.Empty;
		RefreshBindings();
		_tabs.CurrentTab = 0;
		_closeButton.GrabFocus();
	}

	public void Close()
	{
		if (!Visible)
			return;

		CancelCapture();
		// 点击滑条轨道只触发 ValueChanged、不触发 DragEnded，关闭时统一落盘
		SaveSystem.Instance?.SaveSettings();
		Visible = false;
		Closed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible || _captureAction != null)
			return;

		// 键鼠 Esc 由 GameManager 的 pause 动作负责关闭本页；这里只补手柄 B
		if (@event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B })
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	// ------------------------------------------------------------ 按键捕获

	public override void _Input(InputEvent @event)
	{
		if (!Visible || _captureAction == null)
			return;

		switch (@event)
		{
			case InputEventKey key when key.Pressed && !key.Echo:
				GetViewport().SetInputAsHandled();
				if (key.Keycode == Key.Escape)
				{
					CancelCapture();
					_status.Text = "已取消重绑";
					return;
				}

				Commit(new InputEventKey { PhysicalKeycode = key.PhysicalKeycode, Keycode = key.Keycode });
				return;
			case InputEventJoypadButton joy when joy.Pressed:
				GetViewport().SetInputAsHandled();
				Commit(new InputEventJoypadButton { ButtonIndex = joy.ButtonIndex });
				return;
			case InputEventMouseButton mouse when mouse.Pressed:
				GetViewport().SetInputAsHandled();
				Commit(new InputEventMouseButton { ButtonIndex = mouse.ButtonIndex });
				return;
		}
	}

	private void BeginCapture(RebindRow row, Button button)
	{
		CancelCapture();
		_captureAction = row.Action;
		_captureButton = button;
		_captureIdleText = button.Text;
		button.Text = "按任意键…";
		_status.Text = "按下新的按键 / 鼠标键 / 手柄键（Esc 取消）";
	}

	private void Commit(InputEvent evt)
	{
		string action = _captureAction;
		string conflict = SaveSystem.HasInstance ? SaveSystem.Instance.FindConflict(action, evt) : null;
		if (conflict != null)
		{
			_status.Text = $"冲突：{LabelOf(conflict)} 已占用该输入，未修改";
			CancelCapture();
			return;
		}

		var kept = new Godot.Collections.Array<InputEvent>();
		foreach (InputEvent old in InputMap.ActionGetEvents(action))
		{
			// 只替换同一设备类型的绑定，键鼠与手柄各留一套
			if (!SaveSystem.SameDevice(old, evt))
				kept.Add(old);
		}

		kept.Add(evt);

		if (SaveSystem.HasInstance)
		{
			SaveSystem.Instance.StoreBinding(action, kept);
		}
		else
		{
			InputMap.ActionEraseEvents(action);
			foreach (InputEvent item in kept)
				InputMap.ActionAddEvent(action, item);
		}

		_status.Text = $"已绑定：{LabelOf(action)} → {Describe(evt)}";
		CancelCapture();
		RefreshBindings();
	}

	private void CancelCapture()
	{
		if (_captureButton != null)
			_captureButton.Text = _captureIdleText;

		_captureButton = null;
		_captureAction = null;
	}

	private void RefreshBindings()
	{
		foreach (RebindRow row in _rows)
		{
			row.Key.Text = DescribeDevice(row.Action, pad: false);
			row.Pad.Text = DescribeDevice(row.Action, pad: true);
		}
	}

	// ------------------------------------------------------------ 页面构建

	private VBoxContainer AddTab(string title)
	{
		var scroll = new ScrollContainer
		{
			Name = title,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		var box = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		box.AddThemeConstantOverride("separation", 8);

		scroll.AddChild(box);
		_tabs.AddChild(scroll);
		return box;
	}

	private static HBoxContainer AddRow(VBoxContainer parent, string label)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);

		var name = new Label
		{
			Text = label,
			CustomMinimumSize = new Vector2(220, 32),
			VerticalAlignment = VerticalAlignment.Center,
		};

		row.AddChild(name);
		parent.AddChild(row);
		return row;
	}

	private static void AddHint(VBoxContainer parent, string text)
	{
		parent.AddChild(new Label
		{
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Modulate = new Color(1f, 1f, 1f, 0.62f),
		});
	}

	private static void AddSlider(VBoxContainer parent, string label, float min, float max, float step,
		float value, Func<float, string> format, Action<float> onChanged, Action onCommitted)
	{
		HBoxContainer row = AddRow(parent, label);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			Value = value,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(320, 28),
		};

		var readout = new Label
		{
			Text = format(value),
			CustomMinimumSize = new Vector2(80, 32),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
		};

		slider.ValueChanged += v =>
		{
			readout.Text = format((float)v);
			onChanged((float)v);
		};
		slider.DragEnded += _ => onCommitted();

		row.AddChild(slider);
		row.AddChild(readout);
	}

	private static OptionButton AddOption(VBoxContainer parent, string label, string[] items, int selected,
		Action<int> onSelected)
	{
		HBoxContainer row = AddRow(parent, label);

		var option = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(220, 36),
		};

		foreach (string item in items)
			option.AddItem(item);

		option.Selected = Mathf.Clamp(selected, 0, items.Length - 1);
		option.ItemSelected += index => onSelected((int)index);

		row.AddChild(option);
		return option;
	}

	private static void AddCheck(VBoxContainer parent, string label, bool value, Action<bool> onChanged)
	{
		HBoxContainer row = AddRow(parent, label);

		var check = new CheckButton { ButtonPressed = value };
		check.Toggled += pressed => onChanged(pressed);

		row.AddChild(check);
	}

	private void BuildSensitivityTab()
	{
		VBoxContainer box = AddTab("灵敏度");

		AddSlider(box, "鼠标灵敏度", 0.02f, 0.4f, 0.005f, SaveSystem.MouseSensitivitySetting,
			v => $"{v:0.000}",
			v => SaveSystem.Instance?.SetMouseSensitivity(v),
			() => SaveSystem.Instance?.SaveSettings());

		AddSlider(box, "开镜灵敏度倍率", 0.3f, 1f, 0.01f, SaveSystem.AdsSensitivitySetting,
			v => $"{v:0.00}",
			v => SaveSystem.Instance?.SetAdsSensitivityScale(v),
			() => SaveSystem.Instance?.SaveSettings());

		AddSlider(box, "手柄灵敏度", 0.3f, 3f, 0.05f, SaveSystem.GamepadSensitivitySetting,
			v => $"{v:0.00}",
			v => SaveSystem.Instance?.SetGamepadSensitivity(v),
			() => SaveSystem.Instance?.SaveSettings());

		AddHint(box, "手柄视角使用固定的右摇杆轴，灵敏度独立于鼠标；开镜时同样乘以上面的倍率。");
	}

	private void BuildVolumeTab()
	{
		VBoxContainer box = AddTab("音量");

		AddSlider(box, "主音量", 0f, 1f, 0.01f, SaveSystem.HasInstance ? SaveSystem.Instance.MasterVolume : 1f,
			v => $"{v * 100f:0}%",
			v => SaveSystem.Instance?.SetMasterVolume(v),
			() => SaveSystem.Instance?.SaveSettings());

		AddSlider(box, "音效", 0f, 1f, 0.01f, SaveSystem.HasInstance ? SaveSystem.Instance.SfxVolume : 0.9f,
			v => $"{v * 100f:0}%",
			v => SaveSystem.Instance?.SetSfxVolume(v),
			() => SaveSystem.Instance?.SaveSettings());

		AddSlider(box, "环境", 0f, 1f, 0.01f, SaveSystem.HasInstance ? SaveSystem.Instance.AmbientVolume : 0.8f,
			v => $"{v * 100f:0}%",
			v => SaveSystem.Instance?.SetAmbientVolume(v),
			() => SaveSystem.Instance?.SaveSettings());

		AddSlider(box, "无线电", 0f, 1f, 0.01f, SaveSystem.HasInstance ? SaveSystem.Instance.RadioVolume : 0.9f,
			v => $"{v * 100f:0}%",
			v => SaveSystem.Instance?.SetRadioVolume(v),
			() => SaveSystem.Instance?.SaveSettings());
	}

	private void BuildGraphicsTab()
	{
		VBoxContainer box = AddTab("画质");

		AddCheck(box, "全屏", SaveSystem.HasInstance && SaveSystem.Instance.Fullscreen,
			on => SaveSystem.Instance?.SetFullscreen(on));

		AddOption(box, "抗锯齿", new[] { "关闭", "2x MSAA", "4x MSAA" },
			SaveSystem.HasInstance ? SaveSystem.Instance.Msaa : 1,
			index => SaveSystem.Instance?.SetMsaa(index));

		AddCheck(box, "方向光阴影", !SaveSystem.HasInstance || SaveSystem.Instance.Shadows,
			on => SaveSystem.Instance?.SetShadows(on));

		AddHint(box, "分辨率跟随窗口 / 全屏切换；体积雾按关卡高度自动插值，暂不单独开关。");
	}

	private void BuildControlsTab()
	{
		VBoxContainer box = AddTab("控制");

		AddHint(box, "点击键位后按下新的按键 / 鼠标键 / 手柄键即可重绑，Esc 取消。键鼠与手柄各保留一套绑定。");

		foreach ((string id, string label) in RebindDefinitions)
		{
			HBoxContainer row = AddRow(box, label);

			var keyButton = new Button
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(150, 36),
			};
			var padButton = new Button
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(150, 36),
			};

			var entry = new RebindRow { Action = id, Key = keyButton, Pad = padButton };
			keyButton.Pressed += () => BeginCapture(entry, keyButton);
			padButton.Pressed += () => BeginCapture(entry, padButton);

			row.AddChild(keyButton);
			row.AddChild(padButton);
			_rows.Add(entry);
		}

		box.AddChild(new HSeparator());

		var reset = new Button
		{
			Text = "恢复默认绑定",
			CustomMinimumSize = new Vector2(0, 40),
		};
		reset.Pressed += () =>
		{
			SaveSystem.Instance?.ResetBindings();
			CancelCapture();
			RefreshBindings();
			_status.Text = "已恢复默认绑定";
		};
		box.AddChild(reset);
	}

	// ------------------------------------------------------------ 文案

	private static Dictionary<string, string> BuildLabelMap()
	{
		var map = new Dictionary<string, string>();
		foreach ((string id, string label) in RebindDefinitions)
			map[id] = label;

		return map;
	}

	private static string LabelOf(string action) =>
		LabelById.TryGetValue(action, out string label) ? label : action;

	private static string DescribeDevice(string action, bool pad)
	{
		if (!InputMap.HasAction(action))
			return "—";

		foreach (InputEvent evt in InputMap.ActionGetEvents(action))
		{
			bool matches = pad
				? evt is InputEventJoypadButton or InputEventJoypadMotion
				: evt is InputEventKey or InputEventMouseButton;

			if (matches)
				return Describe(evt);
		}

		return "未绑定";
	}

	private static string Describe(InputEvent evt) => evt switch
	{
		InputEventKey key => OS.GetKeycodeString(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode),
		InputEventMouseButton { ButtonIndex: MouseButton.Left } => "鼠标左键",
		InputEventMouseButton { ButtonIndex: MouseButton.Right } => "鼠标右键",
		InputEventMouseButton { ButtonIndex: MouseButton.Middle } => "鼠标中键",
		InputEventMouseButton mouse => $"鼠标键 {(int)mouse.ButtonIndex}",
		InputEventJoypadButton joy => joy.ButtonIndex switch
		{
			JoyButton.A => "手柄 A",
			JoyButton.B => "手柄 B",
			JoyButton.X => "手柄 X",
			JoyButton.Y => "手柄 Y",
			JoyButton.Start => "手柄 Start",
			JoyButton.Back => "手柄 Back",
			JoyButton.LeftShoulder => "手柄 LB",
			JoyButton.RightShoulder => "手柄 RB",
			JoyButton.LeftStick => "手柄 L3",
			JoyButton.RightStick => "手柄 R3",
			JoyButton.DpadUp => "手柄 ↑",
			JoyButton.DpadDown => "手柄 ↓",
			JoyButton.DpadLeft => "手柄 ←",
			JoyButton.DpadRight => "手柄 →",
			_ => $"手柄键 {(int)joy.ButtonIndex}",
		},
		InputEventJoypadMotion axis => axis.Axis switch
		{
			JoyAxis.TriggerLeft => "手柄 LT",
			JoyAxis.TriggerRight => "手柄 RT",
			JoyAxis.LeftX => "左摇杆 X",
			JoyAxis.LeftY => "左摇杆 Y",
			JoyAxis.RightX => "右摇杆 X",
			JoyAxis.RightY => "右摇杆 Y",
			_ => $"手柄轴 {(int)axis.Axis}",
		},
		_ => "未绑定",
	};
}
