using Godot;
using System.Collections.Generic;

/// <summary>
/// AI 可视化调试（2.0 设计 §2.9）。F3 开关。
/// 画：顶层状态、槽位、槽位目标点、共享情报位置、令牌持有者、后撤冷却。
/// 颜色：压制=黄、包抄=蓝、支援=绿、后撤=灰、无令牌=白框。
/// </summary>
public partial class AIDebugOverlay : Control
{
	public SquadDirector Source { get; set; }

	private readonly Color _suppress = new(0.95f, 0.83f, 0.28f);
	private readonly Color _flank = new(0.36f, 0.64f, 0.95f);
	private readonly Color _support = new(0.42f, 0.85f, 0.45f);
	private readonly Color _retreat = new(0.62f, 0.62f, 0.62f);
	private readonly Color _shared = new(0.95f, 0.45f, 0.30f);

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
	}

	public override void _Process(double delta)
	{
		if (Visible)
			QueueRedraw();
	}

	public override void _Draw()
	{
		if (Source == null)
			return;

		Camera3D camera = GetViewport()?.GetCamera3D();
		if (camera == null)
			return;

		Font font = ThemeDB.FallbackFont;
		int tokens = Source.Combat != null ? Source.Combat.TokenBudget : 0;
		int holders = Source.Combat?.Holders.Count ?? 0;

		// 左上角被 HUD（剩余敌人 / 目标）占用，面板整体下移，并垫半透明底
		const float top = 112f;
		DrawRect(new Rect2(8f, top, 470f, 30f + Source.Squads.Count * 15f),
			new Color(0f, 0f, 0f, 0.42f), true);

		DrawString(font, new Vector2(16f, top + 18f),
			$"AI DEBUG  squads={Source.Squads.Count}  tokens={holders}/{tokens}  waves={(Source.Combat?.PressureWaveActive ?? false ? "ON" : "off")}",
			HorizontalAlignment.Left, -1f, 14, Colors.White);

		float y = top + 38f;
		foreach (SquadRuntime squad in Source.Squads)
		{
			if (squad.HasSharedContact)
			{
				Vector3 shared = squad.SharedPosition + Vector3.Up * 1.2f;
				if (!camera.IsPositionBehind(shared))
				{
					Vector2 p = camera.UnprojectPosition(shared);
					DrawArc(p, 26f, 0f, Mathf.Tau, 28, _shared, 1.5f);
					DrawString(font, p + new Vector2(30f, 4f),
						$"共享 {squad.SharedConfidence:F2}", HorizontalAlignment.Left, -1f, 11, _shared);
				}
			}

			DrawString(font, new Vector2(16f, y),
				$"[{squad.Id}] {(squad.Broken ? "BREAK" : "OK")}  n={squad.Members.Count}  conf={squad.SharedConfidence:F2}" +
				$"  {(squad.HasSharedContact ? "CONTACT" : "-")}",
				HorizontalAlignment.Left, -1f, 12, squad.Broken ? _retreat : Colors.White);
			y += 14f;

			foreach (EnemyAgent member in squad.Alive)
			{
				Color color = member.Slot switch
				{
					SquadSlot.Flank => _flank,
					SquadSlot.Support => _support,
					SquadSlot.Retreat => _retreat,
					_ => member.IsRetreating ? _retreat : _suppress,
				};

				bool hasToken = Source.Combat != null && Source.Combat.HasToken(member);
				Vector3 head = member.GlobalPosition + Vector3.Up * 2.1f;
				if (camera.IsPositionBehind(head))
					continue;

				Vector2 screen = camera.UnprojectPosition(head);
				DrawCircle(screen, 5f, color);
				if (!hasToken)
					DrawRect(new Rect2(screen - new Vector2(9f, 9f), new Vector2(18f, 18f)), Colors.White, false, 1f);

				string label = $"{member.State}/{member.Slot}" + (hasToken ? " T" : "") +
					$" {member.HealthRatio * 100f:F0}%" +
					(member.IsRetreating ? $" R{member.RetreatTimeLeft:F1}" : "");
				DrawString(font, screen + new Vector2(10f, 4f), label, HorizontalAlignment.Left, -1f, 11, color);

				// 槽位目标点
				if (member.SlotTarget != Vector3.Zero && !camera.IsPositionBehind(member.SlotTarget))
				{
					Vector2 target = camera.UnprojectPosition(member.SlotTarget);
					DrawLine(screen, target, new Color(color, 0.5f), 1.2f);
					DrawArc(target, 5f, 0f, Mathf.Tau, 12, color, 1.2f);
				}
			}
		}
	}
}
