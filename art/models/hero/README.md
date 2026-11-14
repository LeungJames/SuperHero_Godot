# 玩家角色素材（Player Hero）· A 阶段交付

- 来源：UE 商城资产 `tactical_outfit_v1.blend`（工程外 `e:\GodotProject\external-assets\NewAssets\`）
- 内容：玩家全身角色，保留 UE5 Mannequin 骨架与命名，10 色区纯色材质，3 档 LOD，10 个动作
- 引擎：Godot 4.7 / 导入验证通过（341 骨、10 个动画、10 个 surface）
- 约束：未改场景、C# 与数据资源；接入由游戏线负责

## 1. 文件清单

| 文件 | 大小 | 说明 |
|---|---|---|
| `player_hero_lod0.glb` | 8.22 MB | 35,000 tris，近景（0–10 m） |
| `player_hero_lod1.glb` | 4.45 MB | 13,499 tris，中景（10–25 m） |
| `player_hero_lod2.glb` | 2.94 MB | 5,000 tris，远景（>25 m） |
| `screenshots/01–05_*.png` | — | Blender 端三档 LOD 外观与色区 |
| `screenshots/06–10_*.png` | — | Godot 4.7 实际导入播放截图 |

三个 glb 结构一致：`343 nodes / 1 mesh / 10 primitives / 1 skin / 341 joints / 10 materials / 0 images / 10 animations`。

## 2. 接入要点

1. **朝向与原点**：模型面朝 **+Z**（Godot），脚底在 y=0，身高 2.01 m，A-pose。
2. **动画循环**：glTF 不含循环标记，Godot 导入后 `loop_mode = NONE`。请对 `idle / walk / run / aim` 设为 `LOOP_LINEAR`（这四个 clip 的首尾帧已对齐，可无缝循环）；其余为一次性。
3. **LOD**：三个 glb 各自带完整 10 个动作，可直接按距离实例化对应 LOD 场景，无需额外共享动画库。
4. **武器**：源资产的狙击枪（5 个 LOD、约 31k tris）**未打包**，工程里已有 `WPN_*.glb` 可直接复用。武器挂点用骨骼 **`hand_r`**：在 `hand_r` 下加 `BoneAttachment3D`，把枪的握把对齐到该骨骼，枪管朝骨架 +Z 方向。
5. **死亡**：`death` 末帧角色向 +Z 反方向（身后）躺倒约 1.7 m，若用 `CharacterBody3D` 请注意碰撞体与镜头切换。

## 3. 调色区（10 个 surface / 10 个 draw call）

材质为 Principled BSDF 纯色：`Metallic = 0`，`Roughness = 0.85`，`Specular IOR Level = 0.25`；glb 内 0 张贴图。

| # | 区域 | 源材质 | sRGB | Linear | LOD0 三角面 |
|---|---|---|---|---|---|
| 1 | 头盔 | `Textureset_1` | `#4A5568` | 0.0685, 0.0908, 0.1384 | 49,636 |
| 2 | 面罩/头套 | `Textureset_1.002` | `#1C1F24` | 0.0116, 0.0137, 0.0176 | 770 |
| 3 | 躯干底衣 | `Texture_set_4` | `#6B7079` | 0.1470, 0.1620, 0.1912 | 4,356 |
| 4 | 战术背心 | `Texture_set_5` | `#4A5741` | 0.0685, 0.0953, 0.0529 | 19,300 |
| 5 | 弹匣包/织带 | `Texture_set_6` | `#333D2E` | 0.0331, 0.0467, 0.0273 | 16,931 |
| 6 | 裤装 | `Texture_set_2` / `.001` | `#363A40` | 0.0369, 0.0423, 0.0513 | 17,129 |
| 7 | 靴子 | `Texture_set_1` | `#262421` | 0.0194, 0.0176, 0.0152 | 8,294 |
| 8 | 肩甲/护具 | `Texture_set_3` | `#9A8A6A` | 0.3231, 0.2542, 0.1441 | 5,952 |
| 9 | 护臂/手套 | `Texture_set_7.003` / `.004` | `#B23B36` | 0.4452, 0.0437, 0.0369 | 7,824 |
| 10 | 腰带/挂件 | `Texture_set_7` | `#4A4034` | 0.0685, 0.0513, 0.0343 | 3,180 |

> 原资产 58 个网格、133,372 tris（已剔除无蒙皮的 `Cube.012` 与狙击枪 5 个 LOD）。合并后每个 LOD 为一个网格、10 个 material slot。

## 4. LOD 与 draw call

| LOD | 三角面 | 顶点 | Surface / Draw call | 文件 | 目标 |
|---|---|---|---|---|---|
| LOD0 | 35,000 | 20,626 | 10 / 10 | 8.22 MB | 30–40k |
| LOD1 | 13,499 | 8,865 | 10 / 10 | 4.45 MB | 12–15k |
| LOD2 | 5,000 | 4,021 | 10 / 10 | 2.94 MB | 4–6k |

- 做法：58 个网格按材质合并 → `Decimate (Collapse)` 到目标面数 → `validate()` 清理 → `Limit Total = 4` + `Normalize All` → 平面着色、删除 UV。
- 面数为三角面，均落在要求区间内；单个 LOD 只有 1 个 SkinnedMesh，10 个 primitive 即 10 个 draw call（不含阴影 pass）。
- 头盔占源资产约 37% 面数（49,636 tris），Decimate 会自动把预算留给高曲率区域，三档 LOD 头盔外形仍可辨（见 `04/05` 截图）。

## 5. 骨骼与权重

| 项目 | 数值 |
|---|---|
| 骨架 | 原资产 `root`，341 根骨，UE5 Mannequin 命名（pelvis / spine_01–05 / neck_01–02 / head / clavicle / upperarm / lowerarm / hand / thigh / calf / foot / ball） |
| 参与权重 | 294 根；其余 47 根无权重（保留以维持骨架完整，供后续装备/变体使用） |
| 动画打关键帧 | 25 根主骨（其余由父子继承） |
| 单顶点权重 | 最大 4，已归一化；抽样与全量检查均无 >4 顶点 |
| glTF skin | 1 个 skin，341 joints（Godot 中 `Skeleton3D` 341 骨） |
| 顶点组 | 保留源资产的顶点组命名，变体与同一骨架的敌人可直接复用 |

重定向方法沿用已验证的**世界空间增量**公式：

```
D = P_src * R_src^-1
P_tgt = D * R_tgt
```

再按父子顺序求 `matrix_basis` 逐帧烘焙（30 fps，线性插值）。源骨架 `MannySourceRig` 只承载动作，不导出。

## 6. 动作列表

| Clip | 帧数 @30fps | 时长 | 循环 | 说明 |
|---|---|---|---|---|
| `idle` | 90 | 3.000 s | 是 | 呼吸 + 重心微摆，双脚贴地 |
| `walk` | 30 | 1.000 s | 是 | 原地走，接触/通过姿势完整 |
| `run` | 20 | 0.667 s | 是 | 原地跑，含腾空阶段（最低点离地 2–20 cm） |
| `jump_start` | 15 | 0.500 s | 否 | 下蹲 → 蹬伸 → 离地，末帧 pelvis +36 cm |
| `jump_land` | 18 | 0.600 s | 否 | 空中 → 触地 → 深蹲缓冲 → 起身 |
| `aim` | 60 | 2.000 s | 是 | 双手持枪瞄准，含轻微呼吸摆动 |
| `fire` | 12 | 0.400 s | 否 | 后坐峰值在 0.12 s，回正 |
| `reload` | 48 | 1.600 s | 否 | 右手下探取弹匣 → 回装 → 复位 |
| `hit` | 15 | 0.500 s | 否 | 受击后仰 → 回正 |
| `death` | 45 | 1.500 s | 否 | 后仰 → 屈膝 → 倒地，末帧躺姿高度 0 |

- 所有 clip 的骨盆都由逐帧“最低点贴地”修正，蒙皮最低点不低于 0（`run` 腾空帧除外）。
- 位移全部原地（in-place），前进由游戏线驱动。
- 步幅参考：`walk` 单脚前后行程 **0.70 m**（Godot 实测 `foot_l` z 32.57 → −37.01 cm），1.0 s 周期约合 0.70 m/s；`run` 单脚行程 1.18 m，0.667 s 约合 1.77 m/s。移动速度请按此比例设置，或等比调整动画播放速度。

## 7. Godot 4.7 验证结果

最小测试工程：`e:\GodotProject\blender-anim-spike\work\godot-hero`（工程外）。运行输出：

```
ANIMS=["aim","death","fire","hit","idle","jump_land","jump_start","reload","run","walk"]
ANIM idle length=3.000 tracks=301 loop=0
...
BONES=341
MESH PlayerHero_LOD0 skin=true surfaces=10
SURFACE_COLORS ["4a4034","4a5568","6b7079","363a40","9a8a6a","4a5741","333d2e","262421","1c1f24","b23b36"]
GODOT_FOOT walk t=0.00 foot_l=(16.001,10.494,32.574)  foot_r=(-16.447,15.628,-37.665)
GODOT_FOOT walk t=0.50 foot_l=(16.384,15.178,-37.007) foot_r=(-15.933,10.330,31.789)
player_hero_lod1 anim=true bones=341 anims=10
player_hero_lod2 anim=true bones=341 anims=10
```

- 三档 LOD 均正确导入，蒙皮无炸裂、无逆绑定矩阵错位，左右脚反相（t=0 / t=0.5 镜像）。
- 截图 `06–10` 为 Godot 实际渲染；`09/10` 是逐 clip 抽帧拼图。
- 截图中早期版本的细小白点已确认是 Godot 方向光 **shadow acne**，关闭阴影后消失，与网格/权重无关。

## 8. 已知限制

1. **无 IK / 脚掌锁定**：贴地靠逐帧骨盆高度修正，接触帧不打滑，但移动速度需与步幅匹配，否则会有滑步。
2. **手指未摆姿**：`aim/fire/reload` 的手型是近似持枪，手指沿用原资产静置姿态；需要精确握持时再补手部姿势或加 `hand_r` 挂点。
3. **武器未打包**：见 §2.4，使用工程已有 `WPN_*.glb`。
4. **LOD1/2 也带完整动画**：换取“一个 glb 就能播”的便利，代价是每档多约 2–4 MB；若在意体积，可只保留 LOD0 的动画，LOD1/2 用 `AnimationLibrary` 复用（三者节点路径一致）。
5. **贴图全部丢弃**：符合项目低模平涂风格；若某天需要细节，可给对应色区单独换材质。

## 9. 复现（工程外临时目录）

```
E:\Blender\blender.exe -b work\tactical_source.blend --python work\hero_lods.py -- ^
  work\hero_lod.blend work\renders_hero work\hero_lod_stats.json

E:\Blender\blender.exe -b work\hero_lod.blend --python work\hero_anim.py -- ^
  work\hero_final.blend work\renders_clips work\glb work\hero_anim_stats.json

Godot_v4.7.2-stable_mono_win64_console.exe --headless --editor --quit --path work\godot-hero
Godot_v4.7.2-stable_mono_win64_console.exe --path work\godot-hero --resolution 800x900
```

工作目录：`e:\GodotProject\blender-anim-spike\work\`。
