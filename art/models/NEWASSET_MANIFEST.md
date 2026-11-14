# 外部资产改造产物清单（NewAssets）

- 生成日期：2026-11-14
- 源目录：`e:\GodotProject\external-assets\NewAssets`（已整体移出工程；原 202 个文件 / 2,482.6 MiB）
- 追加源目录：`e:\GodotProject\external-assets\NewAssets2`（第二轮评估后仅接入 `lowpolyweaponsset` 的突击步枪，见 §1.3）
- 本轮范围：**只产出改造后的资产文件 + 本清单**。未修改任何场景（`.tscn`）、C# 玩法代码、`data/*.tres`，未做接线。

---

## 0. 通用约定

| 项目 | 约定 |
|---|---|
| 尺度 | 1 单位 = 1 m，全部为真实尺度 |
| 前向/上方 | 与现役 `WPN_P9.glb` 一致：**长度轴 = Y，朝向端（枪口 / 刃尖）= +Y，上方 = +Z**（`Player.tscn` 中 `Pistol` 节点无旋转，故沿用该基准） |
| 材质 | 每个网格只带纯色 Principled BSDF：**无贴图、无 UV、无顶点色**。GLB 内材质仅作占位与保底观感 |
| 面数口径 | 导出后实际渲染三角面（glTF 按锐边分裂顶点后的计数） |
| 法线 | 全部按 **30° 自动平滑**（硬边保留锐利，避免硬表面"塑料发软"），描边依赖该硬边 |
| 格式 | 统一 GLB（不直连 `.blend`） |

### 平涂接入方式（供接入方执行）

工程现有做法是在场景里给 `MeshInstance3D` 指定 `material_override = ShaderMaterial(shaders/toon.gdshader, albedo_color)` 并配 `next_pass` 描边（见 `scenes/levels/PortLevel.tscn`），描边叠层由 `scripts/actors/ModelOutline.cs` 追加。新资产照此接入即可，下表的 `albedo_color` 直接可用；GLB 自带材质会被 `material_override` 覆盖。

---

## 1. 武器（`art/models/`）

### 1.1 `WPN_Knife.glb` — 战术生存刀

| 项目 | 值 |
|---|---|
| 三角面 / 顶点 | **2,272 / 1,199**（原样保留，无需减面） |
| 尺寸 | 0.065 × **0.290** × 0.023 m（原文件长 4.098 m，已按 ×0.0707 缩到真实尺度） |
| 原点 | 握柄中心（距刀柄末端约 20% 全长），刀尖指向 +Y |
| 材质 | 3 个纯色：刃身 / 护手 / 握柄（按刃长 40.7% / 45.4% 分区） |
| 贴图 | 已全部移除（含 4 套未使用的孤立贴图） |
| 建议用途 | 第一人称近战武器外观；或关卡道具 |
| 建议接入位置 | `Player.tscn` → `Head/Camera3D/ViewModel` 下新增节点（如 `MeleeRoot/Model`），配合 `MeleeController` 的挥动时间轴显隐；或 `PortLevel.tscn` 中放在码头、装备箱旁、敌人尸体旁 |

**材质色值（`albedo_color`）**

| 材质 | RGB | 用途 |
|---|---|---|
| `MAT_Knife_Blade` | (0.56, 0.58, 0.62) | 刃身，冷调钢灰 |
| `MAT_Knife_Guard` | (0.24, 0.24, 0.26) | 护手 |
| `MAT_Knife_Grip` | (0.15, 0.14, 0.13) | 握柄，深胶木/橡胶 |

### 1.2 `WPN_SniperRifle.glb` — 狙击枪改造版（独立枪模型）

> ⚠ **务必区分两个同名易混文件**
>
> | 文件 | 三角面 | 内容 | 用途 |
> |---|---:|---|---|
> | `art/models/WPN_Sniper.glb` | 308 | 5 个网格，**含手套手部**（`MAT_Weapon` + `MAT_Glove`） | **现役第一人称 viewmodel**，由 `data/weapons/Sniper.tres` 通过 `res://art/models/WPN_Sniper.glb` 引用。**不要用任何外部资产覆盖它** |
> | `art/models/WPN_SniperRifle.glb` | 3,815 | 单网格、纯色块、无手部、0.919 m | 本轮改造的**独立枪模型**，用于场景道具、第三人称/敌兵持枪或未来武器外观；**不可当作第一人称 viewmodel** |
>
> 历史教训：本轮改造时曾误覆盖 `WPN_Sniper.glb`（覆盖后第一人称将缺失手部），已还原为仓库版本并经 `git checkout` 校验（308 tri / `MAT_Weapon` + `MAT_Glove` / 5 网格，与提交 `4d3a3dd` 一致）。

改造产物参数：

| 项目 | 值 |
|---|---|
| 三角面 / 顶点 | **3,815 / 2,213**（原始 32,326 → 减面 88%；主体 28,566 → 1,999） |
| 尺寸 | 0.105 × 0.919 × 0.308 m；枪口 +Y |
| 原点 | 包围盒中心 |
| 材质 | 6 个纯色（原 6 个霓虹色改为黄昏色板） |
| 已清除 | 6 根无效骨骼（无顶点组、无 Armature 修改器）、`Anim_Fire`（驱动不了网格）、UV、顶点色 `属性` |
| 减面质量 | 相对原始模型：平均偏差 **0.42 mm**、p95 **1.82 mm**、最大 **6.40 mm**（按 BVH 最近点距离，共 5,368 个顶点采样） |
| 建议用途 | 场景道具（靠墙/挂架/敌人尸体旁）、第三人称/敌兵持枪、或日后做狙击枪外观 |

**材质色值**

| 材质 | RGB | 原霓虹色 |
|---|---|---|
| `MAT_Sniper_Body` | (0.26, 0.27, 0.30) | 蓝 |
| `MAT_Sniper_Frame` | (0.38, 0.40, 0.44) | 品红 |
| `MAT_Sniper_Dark` | (0.13, 0.13, 0.15) | 紫 |
| `MAT_Sniper_Grip` | (0.32, 0.26, 0.20) | 红 |
| `MAT_Sniper_Accent` | (0.72, 0.52, 0.18) | 黄 |
| `MAT_Sniper_Lens` | (0.80, 0.82, 0.84) | 白 |

### 1.3 `WPN_AssaultRifle.glb` — 突击步枪（玩家第一人称 viewmodel）

来源：`external-assets/NewAssets2/lowpolyweaponsset.blend` 的 `AssaultRifle_01` + `Magazine_01` + `Trigger_01`；**手套直接复用 `art/blender/WPN_P9.blend` 的现成分件**（只做平移，未重建）。构建脚本：[build_assault_rifle.py](file:///e:/GodotProject/super-hero/art/blender/build_assault_rifle.py)。

| 项目 | 值 |
|---|---|
| 三角面 / 顶点 | **3,114 / 5,254**（Body 2,690 / Slide 48 / Mag 188 / HandL 72 / HandR 116；flat 着色按面分裂顶点） |
| 尺寸 | 0.061 × **0.852** × 0.246 m（真实尺度，未缩放） |
| 原点 | **握把中心**（与 `WPN_SMG / WPN_Shotgun / WPN_Sniper` 的握把基准一致）；枪口 +Y，导出后即 Godot 的 **−Z 前向** |
| 节点树 | `Body`（根）／`SlidePivot → Slide`／`MagPivot → Mag`／`HandLPivot → HandL`／`HandRPivot → HandR` |
| 枢轴位置（m） | `SlidePivot` (0, −0.122, −0.017) ／ `MagPivot` (0, 0.061, −0.087) ／ `HandLPivot` (−0.123, 0.188, −0.187) ／ `HandRPivot` (0.093, −0.06, −0.215) |
| 可动件语义 | `Slide` = 拉机柄（机匣后上方分件，换弹时沿 −Y 后坐）；`Mag` = 原模型弹匣本体，枢轴在弹匣顶部，换弹时向下脱出 |
| 材质 | 6 个纯色，**无贴图 / 无 UV / 无顶点色**；原 `Glass_01` 已改为不透明镜片色 |
| 贴图 | 源文件 4 个近黑纯色 + 1 个玻璃，已全部替换（源文件本身零贴图） |

**材质色值（`albedo_color`）**

| 材质 | RGB | 用途 |
|---|---|---|
| `MAT_AR_Body` | (0.26, 0.27, 0.30) | 机匣、枪托、镜体 |
| `MAT_AR_Trim` | (0.15, 0.15, 0.16) | 护木、导轨等深色件 |
| `MAT_AR_Metal` | (0.39, 0.40, 0.43) | 枪管、前握把、拉机柄 |
| `MAT_Mag` | (0.36, 0.37, 0.40) | 弹匣（与现役三把长枪同名同色） |
| `MAT_Glove` | (0.16, 0.16, 0.17) | 手套（沿用 `WPN_P9.blend`） |
| `MAT_AR_Lens` | (0.80, 0.82, 0.84) | 瞄准镜镜片（原玻璃改不透明） |

| 项目 | 说明 |
|---|---|
| 建议用途 | 玩家第一人称第 5 把武器（突击步枪）；也可临时当敌兵持枪/场景道具 |
| 建议接入位置 | `Player.tscn → Head/Camera3D/ViewModel/Weapon`：新增 `data/weapons/*.tres`（`WeaponData.Viewmodel` 指向本 GLB），由 `WeaponController` 实例化并挂 `ModelOutline`；`MuzzleOffset` 建议 ≈ `(0, 0.015, −0.55)`（枪口在原点前 0.533 m，留 17 mm 余量） |
| 接线状态 | **未接线**（本轮按要求只产出资产；`.tres`、场景、C# 均未改） |
| 与现有资产的关系 | 与 `WPN_SMG` 同构但更长（0.852 m）；`WPN_Sniper.glb` 仍是现役狙击 viewmodel，**不可被本文件覆盖** |

---

## 2. 场景道具（`art/models/props/`）

全部从 `tactical_outfit_v1.blend` 拆出：已烘焙骨骼姿态并删除蒙皮、应用缩放（原对象带 0.01 缩放）、去贴图去 UV、原点置于**底面中心**（可直接贴地摆放）。

| 文件 | 三角面 | 尺寸（m） | 材质 | 建议用途 |
|---|---:|---|---|---|
| `SM_SniperRifle_0p11x1p2x0p26.glb` | 694 | 0.107 × 1.203 × 0.262 | 1 色 (0.26,0.27,0.30) | 货架/装备箱上的备用枪、敌人背枪 |
| `SM_KneePad_0p16x0p12x0p16.glb` | 483 | 0.156 × 0.116 × 0.159 | 1 色 (0.15,0.15,0.16) | 装备箱旁散落护具、敌人配饰 |
| `SM_MagPouch_0p18x0p17x0p19.glb` | 672 | 0.177 × 0.168 × 0.191 | 1 色 (0.34,0.33,0.26) | 弹药补给点、箱内散件 |
| `SM_Canteen_0p11x0p11x0p12.glb` | 782 | 0.112 × 0.112 × 0.124 | 2 色 (0.48,0.46,0.38) / (0.16,0.16,0.17) | 休息区、仓库角落杂物 |
| `SM_Belt_0p35x0p18x0p03.glb` | 48 | 0.353 × 0.175 × 0.032 | 1 色 (0.17,0.17,0.18) | 挂架/桌面小件，或与其它装备组合 |

**建议接入位置**：`PortLevel.tscn` 关卡装饰层（与 `SM_Rack_*`、`SM_Crate_*`、`SM_Container_*` 同级）；建议以 `StaticBody3D` + `CollisionShape3D`（或纯 `Node3D` + `MeshInstance3D`，若不参与碰撞）放置，材质用上表色值套 `toon.gdshader`。

---

## 3. 音乐（`audio/music/`）

10 首 lofi，全部经**首尾静音裁剪 + 响度归一（目标 −19 dBFS RMS，峰值 ≤ −1 dBFS，超限部分 tanh 软限幅）+ Ogg Vorbis 128 kbps 转码**。

| 文件 | 原时长 | 裁剪后 | 成品时长 | 归一增益 | 体积 |
|---|---:|---:|---:|---:|---:|
| `chillin.ogg` | 2:14.6 | 2:14.6 | 2:14.8 | +2.4 dB | 2,055 KiB |
| `dream.ogg` | 2:50.0 | 2:49.4 | 2:49.8 | −1.9 dB | 1,760 KiB |
| `evening.ogg` | 2:58.5 | 2:58.5 | 2:58.8 | +3.3 dB | 2,064 KiB |
| `feelings.ogg` | 2:13.0 | 2:13.0 | 2:13.3 | −5.8 dB | 2,101 KiB |
| `onlychill.ogg` | 2:54.5 | **2:22.1** | 2:22.3 | −8.4 dB | 1,546 KiB |
| `piano.ogg` | 2:33.6 | 2:32.6 | 2:32.8 | −1.6 dB | 1,992 KiB |
| `ride.ogg` | 3:10.7 | 3:06.1 | 3:06.5 | −7.4 dB | 2,136 KiB |
| `sad.ogg` | 2:59.1 | 2:58.4 | 2:58.8 | −1.6 dB | 2,534 KiB |
| `thekey.ogg` | 2:34.3 | 2:34.3 | 2:34.5 | +5.4 dB | 1,943 KiB |
| `work.ogg` | 2:59.2 | 2:58.7 | 2:59.0 | +3.8 dB | 1,811 KiB |
| **合计** | **27:27** | 26:50 | **26:50** | — | **19.5 MiB** |

- 响度：全部归一至 **−19.0 ~ −19.4 dBFS RMS**（原曲间差 14 dB），峰值 ≤ −2.8 dBFS；峰值超限部分用 tanh 软限幅（每首 ≤1.2% 采样，无削顶失真）。
- 格式：Ogg Vorbis **128 kbps / 2 声道 / 44.1 kHz**；已剥离 Blender 编码时附带的 Theora 视频流，仅保留 Vorbis 逻辑流（Godot 可识别为 `AudioStreamOggVorbis`）。
- 首尾静音：按 −50 dBFS 阈值裁剪并保留 0.15 s 余量；成品相对裁剪源仍有 **0~0.35 s** 的编码量化余量（Blender VSE 按 1/4 秒对齐），接循环点时可忽略。
- **循环**：原曲均非无缝循环（首尾有淡入淡出、首尾相关性 ≈0），若要做无缝 BGM 需在 Godot 导入设置里指定 `loop_begin/loop_end` 或加双轨交叉淡化。

**用途建议**

| 场景 | 推荐曲目 | 说明 |
|---|---|---|
| 主菜单 / 任务简报 | `evening` / `thekey` / `piano` | 安静、动态小，适合循环播放的低强度底乐 |
| 撤离结算 | `ride` / `work` | 收尾感更强 |
| 关卡内战斗 | 不建议 | 曲目无段落分层（无 alert/战斗变奏），且会与现有程序化风/浪/无线电三层环境音打架 |

---

## 4. 验证记录

- 全部 GLB 通过容器校验：`glTF` magic、version 2、长度自洽、**无内嵌贴图（images=0）**。
- 三角面/尺寸/材质均在 Blender 中重新导入后复核（数值见各表）。
- 刀具与护膝做了实体渲染目视确认：刀为刃尖 +Y、锯齿脊朝上、三色分区正确；护膝无蒙皮烘焙扭曲。
- 音频：全部 2 声道 / 44.1 kHz / 结尾无 Theora 视频流（Blender VSE 会附带 Theora 流，已按 Ogg 页级剥离，只保留 Vorbis 逻辑流）。
- 未验证：未在游戏内运行确认朝向与描边宽度（本轮不接线）。`*.glb.import` 由正在运行的 Godot 编辑器自动生成，非手工修改。

---

## 5. 未处理项（按用户指示）

| 项 | 状态 |
|---|---|
| 角色 `military_mercenary_bandit` | 未改造，等动画方案确定 |
| 手枪 `Pistol_001` | 未采用、未改造，随目录移出 |
| `textures/`（60 张 4K，985.9 MiB） | 未取用，随目录移出（内容已打包在原 `.blend` 内，属重复副本） |
| 场景 / C# / 数据资源 | 一行未改 |

## 6. 工程影响

- `NewAssets/` 已从工程移出至 `e:\GodotProject\external-assets\NewAssets`（同盘 E:，避免跨盘复制 2.4 GB）。
- 清理了仅属于 NewAssets 的 Godot 导入缓存：`.godot/imported` 从 **2,303.6 MiB / 388 文件** 降到 **92.4 MiB / 188 文件**；工程内已无 `NewAssets` 的 `.import` 引用。
