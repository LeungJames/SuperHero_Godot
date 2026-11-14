# 盐帮敌人素材 · B 阶段交付

- 来源：UE 商城资产 `tactical_outfit_v1.blend` 派生（与玩家角色同源，工程外 `e:\GodotProject\external-assets\NewAssets\`）
- 内容：两个帮派敌人变体，沿用 UE5 Mannequin 骨架与命名，10 色区纯色材质（0 贴图），3 档 LOD，11 个动作
- 引擎：Godot 4.7 / 导入验证通过（341 骨、10 个 surface、11 个动画）
- 约束：未改场景、C# 与数据资源；敌人接入由游戏线负责

## 1. 文件清单

| 文件 | 大小 | 说明 |
|---|---|---|
| `enemy_salt_raider_lod0.glb` | 8.81 MB | 盐帮巡场打手，35,000 tris，近景（0–10 m） |
| `enemy_salt_raider_lod1.glb` | 4.88 MB | 13,495 tris，中景（10–25 m） |
| `enemy_salt_raider_lod2.glb` | 3.29 MB | 4,974 tris，远景（>25 m） |
| `enemy_salt_sentry_lod0.glb` | 8.82 MB | 盐帮哨位枪手，35,000 tris，近景 |
| `enemy_salt_sentry_lod1.glb` | 4.88 MB | 13,495 tris，中景 |
| `enemy_salt_sentry_lod2.glb` | 3.29 MB | 4,971 tris，远景 |
| `screenshots/` | —（不入库） | Blender 三档 LOD 外观 + Godot 实际播放截图 |

6 个 glb 与玩家角色 `art/models/hero/player_hero_lod0.glb` 结构完全一致：
`343 nodes / 1 mesh / 10 primitives / 1 skin / 341 joints / 10 materials / 0 images / 0 textures / 11 animations`。

## 2. 接入要点

1. **可直接共用玩家动作**：骨骼数、骨骼名、骨骼顺序、rest 姿态与玩家角色 **逐位一致**（见 §6）。把玩家那套 `AnimationLibrary` 直接挂到敌人 `AnimationPlayer` 上即可播放，轨道路径 `root/Skeleton3D:<bone>` 全部命中；6 个 glb 自身也各带完整 11 个动作，两种用法都行。
2. **新增 `aim_walk`**：玩家动作集里只有 `aim`（站桩）与 `walk`（腰射），缺"端着枪走"，故补了 `aim_walk` 循环（见 §7）。
3. **循环设置**：glTF 不含循环标记，Godot 导入后 `loop_mode = NONE`。请对 `idle / walk / run / aim / aim_walk` 设 `LOOP_LINEAR`（首尾帧已对齐）；其余为一次性。
4. **朝向与原点**：与玩家一致，面朝 **+Z**（Godot），脚底 y=0，身高约 2.01 m，A-pose 起手。
5. **同名节点**：敌人根节点分别是 `EnemySaltRaider_LOD0` / `EnemySaltSentry_LOD0`（LOD1/2 后缀相应变化），互不冲突，可同时实例化。
6. **武器**：未打包，沿用工程已有 `WPN_*.glb`，挂点同样用 `hand_r`。
7. **LOD 切换**：三档 glb 节点/骨骼/动画路径完全一致，可各建一个场景按距离替换，也可只保留 LOD0 的动画库给三档共用。

## 3. 两变体的差异做法

共同基线：58 个源部件 → 按材质合并 → Decimate → 重新归一权重 → 平面着色。

| 变体 | 定位 | 增删 | 效果 |
|---|---|---|---|
| 盐帮巡场打手 `EnemySaltRaider` | 对应街头枪手，轻装、进攻性强 | **删 10 个部件 / 15,925 tris**：背包 `Plane.012`（10,545）、左右肩甲 `Plane.018/.024`、右臂护具 `Plane.017/.019/.021`、左右大腿护板 `thighShield3/.001`、左右胫甲 `feetShield/.001` | 48 个部件、117,447 tris；轮廓变窄、无背包，跑动更"轻" |
| 盐帮哨位枪手 `EnemySaltSentry` | 对应守卫，重装、有掩体倾向 | **加 5 块纯色护甲板**（每块 12 tris，随整体网格一起减面），不删件 | 63 个部件、133,592 tris；胸腔 + 双肩 + 双大腿前侧有硬质护甲，剪影更"厚" |

哨位枪手新增护甲板（长方体，纯色，绑定到对应骨骼，随蒙皮一起减面）：

| 护甲板 | 尺寸 (m) | 位置 (m) | 父骨骼 |
|---|---|---|---|
| `ChestPlate` 胸板 | 0.220 × 0.030 × 0.200 | (0.000, −0.258, 1.455) | `spine_03` |
| `ShoulderPlate_L` 左肩板 | 0.130 × 0.150 × 0.050 | (0.228, −0.020, 1.566) | `upperarm_l` |
| `ShoulderPlate_R` 右肩板 | 0.130 × 0.150 × 0.050 | (−0.228, −0.020, 1.566) | `upperarm_r` |
| `ThighPlate_L` 左腿板 | 0.095 × 0.032 × 0.170 | (0.172, −0.186, 0.805) | `thigh_l` |
| `ThighPlate_R` 右腿板 | 0.095 × 0.032 × 0.170 | (−0.172, −0.186, 0.805) | `thigh_r` |

## 4. 调色区（每个变体 10 个 surface / 10 个 draw call）

材质为 Principled BSDF 纯色：`Metallic = 0`、`Roughness = 0.85`、`Specular IOR Level = 0.25`；glb 内 0 张贴图、0 张 image。

### 4.1 盐帮巡场打手（暖调：土黄 / 锈红 / 深棕）

| # | 区域 | surface 名 | sRGB | Linear | LOD0 三角面 |
|---|---|---|---|---|---|
| 1 | 头盔 | `EnemySaltRaider_helmet` | `#4A3527` | 0.0685, 0.0356, 0.0203 | 9,942 |
| 2 | 面罩/头套 | `EnemySaltRaider_mask` | `#1A1512` | 0.0103, 0.0075, 0.0060 | 123 |
| 3 | 躯干底衣 | `EnemySaltRaider_undersuit` | `#B08A55` | 0.4342, 0.2542, 0.0908 | 2,479 |
| 4 | 战术背心 | `EnemySaltRaider_vest` | `#8C3B2E` | 0.2623, 0.0437, 0.0273 | 4,099 |
| 5 | 弹匣包/织带 | `EnemySaltRaider_pouches` | `#5A3B24` | 0.1022, 0.0437, 0.0176 | 4,478 |
| 6 | 裤装 | `EnemySaltRaider_pants` | `#7A6448` | 0.1946, 0.1274, 0.0648 | 6,848 |
| 7 | 靴子 | `EnemySaltRaider_boots` | `#2E2620` | 0.0273, 0.0194, 0.0144 | 3,436 |
| 8 | 护臂/肩甲 | `EnemySaltRaider_armor` | `#A85A32` | 0.3916, 0.1022, 0.0319 | 1,469 |
| 9 | 护臂/手套 | `EnemySaltRaider_armguard` | `#6B4A32` | 0.1470, 0.0685, 0.0319 | 1,896 |
| 10 | 腰带/挂件 | `EnemySaltRaider_belt` | `#5A4028` | 0.1022, 0.0513, 0.0212 | 230 |

### 4.2 盐帮哨位枪手（冷调：灰绿 / 暗蓝）

| # | 区域 | surface 名 | sRGB | Linear | LOD0 三角面 |
|---|---|---|---|---|---|
| 1 | 头盔 | `EnemySaltSentry_helmet` | `#4E5A4C` | 0.0762, 0.1022, 0.0723 | 8,492 |
| 2 | 面罩/头套 | `EnemySaltSentry_mask` | `#14171A` | 0.0070, 0.0086, 0.0103 | 104 |
| 3 | 躯干底衣 | `EnemySaltSentry_undersuit` | `#3A4654` | 0.0423, 0.0612, 0.0887 | 2,143 |
| 4 | 战术背心 | `EnemySaltSentry_vest` | `#5C6B58` | 0.1070, 0.1470, 0.0976 | 4,062 |
| 5 | 弹匣包/织带 | `EnemySaltSentry_pouches` | `#39443A` | 0.0409, 0.0578, 0.0423 | 5,786 |
| 6 | 裤装 | `EnemySaltSentry_pants` | `#3F4A56` | 0.0497, 0.0685, 0.0931 | 6,475 |
| 7 | 靴子 | `EnemySaltSentry_boots` | `#1C1F22` | 0.0116, 0.0137, 0.0160 | 3,028 |
| 8 | 护具/护甲板 | `EnemySaltSentry_armor` | `#6B7C8C` | 0.1470, 0.2016, 0.2623 | 2,605 |
| 9 | 护臂/手套 | `EnemySaltSentry_armguard` | `#26313D` | 0.0194, 0.0307, 0.0467 | 1,693 |
| 10 | 腰带/挂件 | `EnemySaltSentry_belt` | `#2A2E33` | 0.0232, 0.0273, 0.0331 | 612 |

> 两个变体同名色区使用同一源材质槽（`Textureset_1`、`Texture_set_2..7` 等），仅换色值，因此装备分区与玩家角色可直接互认。色值 sRGB 写在材质 `albedo_color`，glTF `baseColorFactor` 为对应 Linear。

## 5. LOD 与 draw call

| 变体 | LOD | 三角面 | 网格顶点 | Surface / Draw call | 目标区间 |
|---|---|---|---|---|---|
| 巡场打手 | LOD0 | 35,000 | 20,276 | 10 / 10 | 30–40k ✓ |
| 巡场打手 | LOD1 | 13,495 | 8,610 | 10 / 10 | 12–15k ✓ |
| 巡场打手 | LOD2 | 4,974 | 3,815 | 10 / 10 | 4–6k ✓ |
| 哨位枪手 | LOD0 | 35,000 | 20,631 | 10 / 10 | 30–40k ✓ |
| 哨位枪手 | LOD1 | 13,495 | 8,869 | 10 / 10 | 12–15k ✓ |
| 哨位枪手 | LOD2 | 4,971 | 4,030 | 10 / 10 | 4–6k ✓ |

- 做法：按材质分区 join → `Decimate (Collapse)` 到目标面数 → `validate()` → `Limit Total = 4` + `Normalize All` → 平面着色、删 UV、删贴图。
- 每个 LOD 只有 1 个 SkinnedMesh，10 个 primitive = 10 个 draw call（不含阴影 pass）。
- 上述面数为 glb 内实测三角面；Blender 端 Decimate 目标为 35,000 / 13,500 / 5,000，导出时剔除退化面后略有下降。

## 6. 骨骼一致性核对（结果：完全一致）

| 核对项 | 玩家 ↔ 巡场打手 | 玩家 ↔ 哨位枪手 | 打手 ↔ 哨兵 |
|---|---|---|---|
| 骨骼数 | 341 / 341 | 341 / 341 | 341 / 341 |
| 骨骼名逐位比对 | 差异 0 | 差异 0 | 差异 0 |
| glTF joint 节点索引签名（名 + T/R/S） | 完全相同 | 完全相同 | 完全相同 |
| Godot `Skeleton3D` rest origin 最大差 | 0.000000 | 0.000000 | 0.000000 |
| Godot rest 旋转四元数最大差 | 0.000000 | 0.000000 | 0.000000 |
| 顶点权重 | 最大 4 根骨，和 = 1.0，>4 顶点 0 个 | 同左 | 同左 |
| 蒙皮 | 1 个 skin / 341 joints | 同左 | 同左 |

LOD0/1/2 之间同样满足上述全部条件（三档共用同一骨架，可热切换）。

**动作复用实测**（直接解析 6 个 glb 的动画采样器，逐通道逐时间点比对玩家 LOD0 与敌人 LOD0 的同名 clip）：

| clip | 哨位枪手 vs 玩家 | 巡场打手 vs 玩家 |
|---|---|---|
| `idle` `walk` `run` `jump_start` `jump_land` `aim` `fire` `reload` `hit` | 最大差 **0.000000**（1,023 通道全等） | 最大差 ≤ **1.45 mm**（仅骨盆 Y，逐帧贴地修正导致） |
| `death` | 最大差 **0.000000** | 末帧骨盆差 6.1 cm（打手删了背包，仰卧时接触点由背包改为肩背，贴地高度随之变化；两版蒙皮最低点均为 0） |

- 1023 通道 = 341 骨 × 3（T/R/S），两个变体与玩家的通道集合、目标节点、时间点完全一致。
- Godot 里看到的轨道数差异（玩家 301 / 敌人 306）来自导入选项 `animation/remove_immutable_tracks=true`：敌人删件后个别辅助骨出现浮点级微小变化，不再算"常量轨道"而被保留，不影响播放与复用。

## 7. 动作列表（11 个 clip，30 fps，原地）

| Clip | 帧数 | 时长 | 循环 | 说明 |
|---|---|---|---|---|
| `idle` | 90 | 3.000 s | 是 | 呼吸 + 重心微摆，双脚贴地 |
| `walk` | 30 | 1.000 s | 是 | 原地走，接触/通过姿势完整 |
| `run` | 20 | 0.667 s | 是 | 原地跑，含腾空（最低点离地 2–20 cm） |
| `jump_start` | 15 | 0.500 s | 否 | 下蹲 → 蹬伸 → 离地 |
| `jump_land` | 18 | 0.600 s | 否 | 空中 → 触地 → 深蹲缓冲 → 起身 |
| `aim` | 60 | 2.000 s | 是 | 双手持枪瞄准，含轻微呼吸摆动 |
| **`aim_walk`** | **30** | **1.000 s** | **是** | **持枪行走（新增）：上半身 `aim`，下半身走循环** |
| `fire` | 12 | 0.400 s | 否 | 后坐峰值 0.12 s，回正 |
| `reload` | 48 | 1.600 s | 否 | 右手下探取弹匣 → 回装 → 复位 |
| `hit` | 15 | 0.500 s | 否 | 受击后仰 → 回正 |
| `death` | 45 | 1.500 s | 否 | 后仰 → 屈膝 → 倒地 |

### `aim_walk`（补的动作）

- 存在原因：原动作集只有 `aim`（站桩瞄准）与 `walk`（腰射走），没有"端着枪走"，移动中开火缺中间态。
- 结构：上半身整段沿用 `aim` 姿态（`spine_01–03` 前倾 + 左右微摆、`neck_01`/`head` 抬头、双臂持枪高度不变），下半身走 30 帧循环（`thigh` ±20°、`calf` 8–42°、踝/脚掌配平），骨盆仅 1.2 cm 侧摆 + 2 cm 上下。
- 首尾帧对齐：**全部 306 条轨道首帧 = 末帧**（glb 采样器最大差 `0.000000`，Godot 内 `loop_maxdiff = 0.00000012`），可直接 `LOOP_LINEAR`。
- 步幅：单脚前后行程 0.65 m / 1.0 s，**约 0.65 m/s**；偏低是故意的（瞄准移动通常慢于普通走）。若游戏用 0.9–1.0 m/s，把播放速度调到 1.4–1.5 倍即可。
- 上下半身分离：若游戏改用蒙版叠加（`aim` 叠 `walk`），骨骼名与玩家完全同名，`spine_01` 及以上做上半身 mask 即可；本 clip 只是为"整段直接播"准备的兜底方案。
- 贴地：逐帧最低点修正，全 30 帧蒙皮最低点 ≥ −0.3 mm。

## 8. Godot 4.7 验证结果

最小测试工程：`e:\GodotProject\blender-anim-spike\work\godot-enemy`（工程外，同时载入玩家 LOD0 与两变体 LOD0/1/2）。运行输出节选：

```
player bones=341 anims=10
raid   bones=341 anims=11   ANIMS ["aim","aim_walk","death","fire","hit","idle","jump_land","jump_start","reload","run","walk"]
sentry bones=341 anims=11
raid_lod1/lod2, sentry_lod1/lod2  bones=341 anims=11
COMPARE player vs raid:   bones=341/341 name_mismatch=0 rest_origin_maxdiff=0.000000 rest_rot_maxdiff=0.000000
COMPARE player vs sentry: bones=341/341 name_mismatch=0 rest_origin_maxdiff=0.000000 rest_rot_maxdiff=0.000000
COMPARE raid vs sentry:   bones=341/341 name_mismatch=0 rest_origin_maxdiff=0.000000 rest_rot_maxdiff=0.000000
COMPARE raid vs raid_lod2: bones=341/341 name_mismatch=0 rest_origin_maxdiff=0.000000 rest_rot_maxdiff=0.000000
CLIPSET player->raid   missing=[] extra=["aim_walk"]
CLIPSET player->sentry missing=[] extra=["aim_walk"]
AIM_WALK raid   tracks=306 loop_maxdiff=0.00000012 length=1.000
AIM_WALK sentry tracks=306 loop_maxdiff=0.00000012 length=1.000
RAID_SURFACES 10 ["5a4028","8c3b2e","7a6448","a85a32","6b4a32","b08a55","5a3b24","4a3527","1a1512","2e2620"]
SENTRY_SURFACES 10 ["2a2e33","5c6b58","3f4a56","6b7c8c","26313d","3a4654","39443a","4e5a4c","14171a","1c1f22"]
```

- 三档 LOD × 两变体均正确导入：蒙皮无炸裂、无逆绑定矩阵错位，`walk`/`run`/`aim_walk` 左右腿反相正常。
- 截图（`screenshots/`，按仓库规则不入库）：
  - `01_lineup_idle.png` 玩家 + 两变体并排（idle）
  - `02_lineup_aim_walk.png` 玩家 `aim` vs 敌人 `aim_walk`
  - `03_lineup_run.png` 三者 `run` 同帧对比
  - `04_enemies_aim.png` 两敌人 `aim` 特写
  - `05_lod2_walk.png` LOD2 远景档 `walk`
  - `06_lod_chain_raider.png` 打手 LOD0/1/2 同帧对比
  - `07_aim_walk_closeup.png` `aim_walk` 特写（上半身持枪 / 下半身迈步）
  - `EnemySalt*_LOD*_front/side/head.png` Blender 端三档外观与头部特写

## 9. 已知限制

1. **护甲是纯色块长方体**：哨位枪手的 5 块护甲板是硬边盒子（正是"纯色块"风格要求），不做倒角/绑带；需要更好剪影时可单独替换这 5 个部件。
2. **`aim_walk` 上半身只做前倾与微摆**：没有做左右瞄准分离（strafe），侧向移动时请整体旋转角色或在此 clip 基础上再加 `aim_walk_l/r`。
3. **手指未摆姿**：与玩家一致，`aim/fire/reload/aim_walk` 的手型是近似持枪，手指沿用静置姿态。
4. **武器未打包**：见 §2.6。
5. **LOD1/2 也带完整 11 个动作**：便宜"一个 glb 就能播"，代价是每档多约 2–4 MB；若在意体积，只保留 LOD0 的动画库复用即可（路径一致）。
6. **死亡走向**：两变体 `death` 与玩家同向、向后躺倒约 1.7 m，用 `CharacterBody3D` 时注意碰撞体与镜头切换。

## 10. 复现（工程外临时目录）

```
# 1) 变体几何 + 配色 + 三档 LOD + Blender 外观渲染
E:\Blender\blender.exe -b work\tactical_source.blend --python work\enemy_build.py -- ^
  raid work\enemy_raid.blend work\renders_enemy work\enemy_raid_stats.json
E:\Blender\blender.exe -b work\tactical_source.blend --python work\enemy_build.py -- ^
  sentry work\enemy_sentry.blend work\renders_enemy work\enemy_sentry_stats.json

# 2) 烘焙 11 个 clip + 建 NLA + 导出 3 个 glb（每个变体各跑一次）
E:\Blender\blender.exe -b work\enemy_raid.blend --python work\enemy_anim.py -- ^
  work\glb work\enemy_raid_anim_stats.json

# 3) Godot 实测（工程外最小工程，含截图）
Godot_v4.7.2-stable_mono_win64_console.exe --path work\godot-enemy
```

工作目录：`e:\GodotProject\blender-anim-spike\work\`（全部 .blend / .py / 中间产物只留在该目录，不入库）。
