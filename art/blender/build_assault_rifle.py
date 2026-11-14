"""把外部 lowpolyweaponsset 里的 AssaultRifle_01 改造成玩家第一人称武器。

与 art/blender/build_rigs.py 产出的四把 viewmodel 完全同构：
  Body / Slide / Mag / HandL / HandR + SlidePivot / MagPivot / HandLPivot / HandRPivot
手套直接复用 WPN_P9.blend 里的现成分件（只做平移，不重建）。

坐标沿用 Blender：+Y 向前（枪口），+Z 向上；导出到 Godot 后 -Z 向前。
原点对齐到握把位置（与 WPN_SMG / WPN_Shotgun / WPN_Sniper 的握把中心一致）。

用法：blender --background --factory-startup --python art/blender/build_assault_rifle.py
输出：art/models/WPN_AssaultRifle.glb
"""
import bpy
import bmesh
import os

OUT = r"e:\GodotProject\super-hero\art\models"
AR_BLEND = r"e:\GodotProject\external-assets\NewAssets2\lowpolyweaponsset.blend"
P9_BLEND = r"e:\GodotProject\super-hero\art\blender\WPN_P9.blend"

# ---- 与 WPN_SMG 一致的握把/手部基准 ----
GRIP_CENTER = (0.0, -0.08, -0.11)       # 目标握把中心（SMG 机身握把盒中心）
P_HAND_L = (-0.123, -0.06, -0.212)      # 支撑手枢轴相对"握点"的偏移（沿用 build_rigs.py）
P_HAND_R = (0.093, -0.06, -0.215)

# 源模型里的握把中心（Blender 世界坐标，未旋转）
SRC_GRIP = (-0.049, 0.135, 0.104)


def parts_of(me, mw=None):
    """按连通体积拆分，返回 [(verts, faces)]，face = (顶点下标元组, 材质下标)。"""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    seen = set()
    out = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack = [v]
        seen.add(v.index)
        comp = []
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.link_edges:
                o = e.other_vert(cur)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        local = {vv: i for i, vv in enumerate(comp)}
        faces = []
        done = set()
        for vv in comp:
            for f in vv.link_faces:
                if f.index in done:
                    continue
                done.add(f.index)
                faces.append((tuple(local[x] for x in f.verts), f.material_index))
        verts = []
        for x in comp:
            co = mw @ x.co if mw else x.co
            verts.append((co[0], co[1], co[2]))
        out.append((verts, faces))
    bm.free()
    return out


def bbox(part):
    xs = part[0]
    mn = [min(c[i] for c in xs) for i in range(3)]
    mx = [max(c[i] for c in xs) for i in range(3)]
    return mn, mx, [(mn[i] + mx[i]) / 2 for i in range(3)], [mx[i] - mn[i] for i in range(3)]


def make_object(name, parts, mats, origin=(0.0, 0.0, 0.0)):
    verts, faces, fmats = [], [], []
    for pv, pf in parts:
        base = len(verts)
        verts += [(v[0] - origin[0], v[1] - origin[1], v[2] - origin[2]) for v in pv]
        for f, mi in pf:
            faces.append(tuple(base + i for i in f))
            fmats.append(mi)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    for m in mats:
        me.materials.append(m)
    for i, poly in enumerate(me.polygons):
        poly.material_index = fmats[i]
    me.validate()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def pivot(name, world):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_size = 0.05
    bpy.context.collection.objects.link(ob)
    ob.location = world
    return ob


def attach(ob, parent):
    if parent is not None:
        ob.parent = parent


def new_material(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    bsdf.inputs["Metallic"].default_value = 0.0
    m.diffuse_color = (*rgb, 1.0)
    return m


def export(name, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=os.path.join(OUT, name + ".glb"),
        export_format='GLB',
        use_selection=True,
        export_apply=False,
        export_yup=True,
        export_skins=False,
        export_animations=False,
        export_cameras=False,
        export_lights=False,
    )


def main():
    # ---- 1. 先取 WPN_P9 的手套分件（打开文件会清空数据，所以先抽出纯坐标） ----
    bpy.ops.wm.open_mainfile(filepath=P9_BLEND)
    p9 = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    p9_mats = list(p9.data.materials)
    glove_src = next(m for m in p9_mats if m and m.name == "MAT_Glove")
    glove_rgb = tuple(glove_src.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value)[:3]

    left, right = [], []
    for p in parts_of(p9.data, p9.matrix_world):
        mat_set = {mi for _, mi in p[1]}
        if not any(p9_mats[mi] and p9_mats[mi].name == "MAT_Glove" for mi in mat_set):
            continue
        mn, mx, ctr, size = bbox(p)
        if size[1] > 0.2:
            (left if ctr[0] < 0 else right).append(p)
        else:
            (left if ctr[0] < -0.01 else right).append(p)

    # ---- 2. 取源模型（AR 本体 + 弹匣 + 扳机） ----
    bpy.ops.wm.open_mainfile(filepath=AR_BLEND)
    ar = bpy.data.objects["AssaultRifle_01"]
    mag_ob = bpy.data.objects["Magazine_01"]
    trig_ob = bpy.data.objects["Trigger_01"]

    ar_parts = parts_of(ar.data, ar.matrix_world)
    mag_parts = parts_of(mag_ob.data, mag_ob.matrix_world)
    trig_parts = parts_of(trig_ob.data, trig_ob.matrix_world)
    print("SRC parts ar=%d mag=%d trig=%d" % (len(ar_parts), len(mag_parts), len(trig_parts)))

    # ---- 3. 材质：4 个源槽 -> 4 个纯色；弹匣/手套另给 ----
    m_body = new_material("MAT_AR_Body", (0.26, 0.27, 0.30))
    m_trim = new_material("MAT_AR_Trim", (0.15, 0.15, 0.16))
    m_metal = new_material("MAT_AR_Metal", (0.39, 0.40, 0.43))
    m_lens = new_material("MAT_AR_Lens", (0.80, 0.82, 0.84))
    m_mag = new_material("MAT_Mag", (0.36, 0.37, 0.40))
    m_glove = new_material("MAT_Glove", glove_rgb)
    mats = [m_body, m_trim, m_metal, m_lens, m_mag, m_glove]
    idx_mag = 4
    idx_glove = 5

    # 源槽顺序：0 Color_01 / 1 Color_02 / 2 Color_03 / 3 Glass -> 依次对应 0..3

    # ---- 3. 变换：绕 Z 转 180°（枪口 -Y -> +Y）+ 平移（握把中心 -> GRIP_CENTER） ----
    # p' = (-x, -y, z) + T
    tx = GRIP_CENTER[0] - (-SRC_GRIP[0])
    ty = GRIP_CENTER[1] - (-SRC_GRIP[1])
    tz = GRIP_CENTER[2] - SRC_GRIP[2]

    def xform(part):
        vs = [(-v[0] + tx, -v[1] + ty, v[2] + tz) for v in part[0]]
        return (vs, part[1])

    ar_parts = [xform(p) for p in ar_parts]
    mag_parts = [xform(p) for p in mag_parts]
    trig_parts = [xform(p) for p in trig_parts]

    # ---- 4. 拆出枪机（拉机柄）：源坐标中位于机匣后上方的小分件 ----
    target = (0.177, 0.197)  # 源坐标 (Y, Z)，对应 C10
    slide_parts, body_parts = [], []
    for p in ar_parts:
        mn, mx, ctr, size = bbox(p)
        src_ctr_y = -ctr[1] + ty          # 反推源 Y
        near = abs(src_ctr_y - target[0]) < 0.02 and abs(ctr[2] - (target[1] + tz)) < 0.02
        if near and max(size) < 0.08:
            slide_parts.append(p)
        else:
            body_parts.append(p)
    assert len(slide_parts) == 1, "枪机分件识别失败：%d" % len(slide_parts)

    body_parts += trig_parts  # 扳机并入机身（与现有四把枪一致，不单独做枢轴）
    # 枪机换金属色
    slide_parts = [(vs, [(f, 2) for f, _ in fs]) for vs, fs in slide_parts]
    # 弹匣统一为一个材质
    mag_parts = [(vs, [(f, idx_mag) for f, _ in fs]) for vs, fs in mag_parts]

    # ---- 5. 枢轴位置 ----
    p_slide = tuple(bbox(slide_parts[0])[2])

    mag_mn = [min(v[i] for vs, _ in mag_parts for v in vs) for i in range(3)]
    mag_mx = [max(v[i] for vs, _ in mag_parts for v in vs) for i in range(3)]
    p_mag = (0.0, (mag_mn[1] + mag_mx[1]) / 2, mag_mx[2])  # 弹匣顶部中心

    p_hand_r = P_HAND_R
    # 左手握护木：取面数最多的分件（护木）中心，按 SMG 的手部关系前移 0.05、抬高 0.042
    guard = max(body_parts, key=lambda p: sum(len(f) - 2 for f, _ in p[1]))
    g_mn, g_mx, g_ctr, g_size = bbox(guard)
    p_hand_l = (P_HAND_L[0], g_ctr[1] + 0.05 + P_HAND_L[1], g_ctr[2] + 0.042 + P_HAND_L[2])

    # 左手整体平移到护木上（与 build_rigs.py 的 shifted 一致）
    shift_l = (p_hand_l[0] - P_HAND_L[0], p_hand_l[1] - P_HAND_L[1], p_hand_l[2] - P_HAND_L[2])
    left = [([(v[0] + shift_l[0], v[1] + shift_l[1], v[2] + shift_l[2]) for v in vs], fs)
            for vs, fs in left]

    # ---- 6. 手套改为索引，坐标已在第 1 步取好 ----
    left = [(vs, [(f, idx_glove) for f, _ in fs]) for vs, fs in left]
    right = [(vs, [(f, idx_glove) for f, _ in fs]) for vs, fs in right]

    # ---- 7. 组装 ----
    meshes = [
        make_object("Body", body_parts, mats),
        make_object("Slide", slide_parts, mats, p_slide),
        make_object("Mag", mag_parts, mats, p_mag),
        make_object("HandL", left, mats, p_hand_l),
        make_object("HandR", right, mats, p_hand_r),
    ]
    pivots = [
        pivot("SlidePivot", p_slide),
        pivot("MagPivot", p_mag),
        pivot("HandLPivot", p_hand_l),
        pivot("HandRPivot", p_hand_r),
    ]
    for mesh, pv in zip(meshes[1:], pivots):
        attach(mesh, pv)

    export("WPN_AssaultRifle", meshes + pivots)

    print("AR_OK slide_pivot=%s mag_pivot=%s handL=%s handR=%s" %
          (tuple(round(v, 3) for v in p_slide), tuple(round(v, 3) for v in p_mag),
           tuple(round(v, 3) for v in p_hand_l), tuple(round(v, 3) for v in p_hand_r)))
    print("AR_PARTS body=%d slide=%d mag=%d handL=%d handR=%d" %
          (len(body_parts), len(slide_parts), len(mag_parts), len(left), len(right)))


main()
