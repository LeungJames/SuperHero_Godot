# SuperHero · 黄昏港口

Godot 4.7（Forward+ / D3D12 / Jolt 物理）+ C# 的第一人称短局射击。
从 42m 吊塔顶一路向下穿越脚手架、运输臂与集装箱屋顶，用一把 12 发 P-9 清掉 8 个持枪黑帮，
清场后直升机降落、自动登机撤离。

设计文档见 [doc/superhero-design.md](doc/superhero-design.md)。

## 运行环境

- Godot 4.7.2 stable（.NET / mono 版）
- .NET SDK 8.0（assembly 名 `SuperHero`，见 `project.godot` 的 `[dotnet]`）
- Windows + D3D12（`rendering_device/driver.windows="d3d12"`），Jolt 物理

```powershell
dotnet build SuperHero.csproj
# 编辑器打开工程后 F5，或：
Godot_v4.7.2-stable_mono_win64.exe --path .
```

## 操作

| 按键 | 行为 |
| --- | --- |
| W A S D | 移动 |
| Shift | 冲刺 |
| Ctrl | 静步（不发出脚步声） |
| 空格 | 跳跃（含土狼时间与跳跃缓冲） |
| 鼠标左键 | 射击（半自动） |
| 鼠标右键 | 开镜（ADS） |
| R | 换弹（2.0s） |
| Esc | 暂停 / 继续 |
| Enter | 结算界面重新开始 |

## 规则要点

- 玩家 100 血，中弹 4 发即死；**没有回血**，没有医疗包。
- 爆头 ×2.5、躯干 ×1.0、四肢 ×0.75；手枪弹匣 12 发，备弹 36 发，可拾取敌人掉落的弹药。
- 摔伤：安全落差 3.5m，超出部分按米扣血；落水即死。
- 最后一名敌人死亡当帧判胜，随后播放不可跳过的 12.5s 直升机撤离演出。

## 工程结构

```
scenes/            场景：actors / enemies / levels / fx / ui
scripts/
  core/            GameManager、EnemyRegistry、NoiseEmitter、DamageInfo
  player/          PlayerController、PlayerCamera、PlayerHealth
  weapons/         PistolController、PistolData、HitscanResolver
  enemies/         EnemyData、EnemyAgent、EnemyHealth、Hitbox
  level/           VictorySequenceController、EnvironmentController、WaterKillVolume
  ui/              HudController、ResultsPanel
  audio/           ProceduralAudio（运行时合成）、AudioDirector（调度）
  fx/              Tracer、ImpactEffect、AmmoPickup、DeathBurst
data/              PlayerData / P9 / 两种敌人 / VictorySequenceConfig（.tres）
art/models/        Blender 导出的 .glb（WPN_P9、两种敌人、直升机）
art/blender/       .blend 源文件
shaders/           toon.gdshader（两阶卡通）、outline.gdshader（反向外壳描边）
```

约定：**业务逻辑写在 C#，`.tscn` 与 Resource 只承担节点结构、参数配置与视图**。
数值调平改 `data/**/*.tres`，不要改状态机。

## 美术与音频

- 第一人称手臂 + P9、两种敌人、直升机由 Blender 自制低模（`.blend` 与 `.glb` 均在 `art/`）。
- 关卡结构与 blockout 尺寸一致，材质统一使用 toon + 描边，色板取自设计文档 §8.1。
- **没有任何外部音频素材**：全部音效（脚步 / 枪声 / 换弹 / 空仓 / 受击 / 风声 / 海浪 / 直升机 / 无线电）
  在运行时由 `ProceduralAudio` 合成并缓存为 `AudioStreamWav`，`AudioDirector` 负责播放调度。

## 里程碑

| Tag | 内容 |
| --- | --- |
| `m1-player-movement` | 移动、跳跃、摔伤、第一人称相机 |
| `m2-pistol-shooting` | 手枪射击、ADS、换弹、弹药拾取 |
| `m3-enemies` | 两种持枪敌人、垂直感知、掩体行为 |
| `m4-port-level` | 纵向下行港口 blockout 与 8 个敌人落位 |
| `m5-game-flow-ui` | 全歼即胜、直升机演出、HUD 与结算 |
| `m6-art` | Blender 低模、美漫描边与黄昏光照 |
| `m7-polish` | 程序生成音效、死亡特效、高度光照与交付打磨 |
