"""把现有手枪 / 敌人低模拆成"枢轴空物体 + 分件网格"的节点层级（不用蒙皮）。

导出后在 Godot 里就是一棵普通 Node3D 树：旋转 / 位移枢轴即可驱动动作，
不依赖蒙皮的绑定矩阵，行为可预测、也不增加材质分段数。

用法：blender --background --factory-startup --python art/blender/build_rigs.py
导出：覆盖 art/models/*.glb（世界坐标不变）。
"""
import bpy
import bmesh
import os

OUT = r"e:\GodotProject\super-hero\art\models"


def open_mesh(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ob.select_set(False)
    return ob


def parts_of(me):
    """返回 [(verts, faces)]，face = (顶点下标元组, 材质下标)。"""
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
        out.append(([tuple(x.co) for x in comp], faces))
    bm.free()
    return out


def bbox(part):
    xs = part[0]
    mn = [min(c[i] for c in xs) for i in range(3)]
    mx = [max(c[i] for c in xs) for i in range(3)]
    return mn, mx, [(mn[i] + mx[i]) / 2 for i in range(3)], [mx[i] - mn[i] for i in range(3)]


def make_object(name, parts, mats, origin=(0.0, 0.0, 0.0)):
    """网格顶点直接烘焙到"相对枢轴"的坐标里，节点树只做纯平移/旋转父子关系，
    不依赖 matrix_parent_inverse，导出结果与 Blender 里看到的一致。"""
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


def box_part(center, size, mat):
    cx, cy, cz = center
    sx, sy, sz = [s / 2 for s in size]
    v = [(cx + dx * sx, cy + dy * sy, cz + dz * sz)
         for dx in (-1, 1) for dy in (-1, 1) for dz in (-1, 1)]
    # 顺序：(dx,dy,dz) = (-,-,-)(-,-,+)(-,+,-)(-,+,+)(+,-,-)(+,-,+)(+,+,-)(+,+,+)
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return v, [(q, mat) for q in f]


def pivot(name, world, parent=None, parent_world=(0, 0, 0)):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_size = 0.05
    bpy.context.collection.objects.link(ob)
    if parent is None:
        ob.location = world
    else:
        ob.location = (world[0] - parent_world[0], world[1] - parent_world[1],
                       world[2] - parent_world[2])
        ob.parent = parent
    return ob


def attach(ob, parent):
    if parent is not None:
        ob.parent = parent


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


# ---------------------------------------------------------------- 武器
def build_weapon():
    ob = open_mesh(r"e:\GodotProject\super-hero\art\blender\WPN_P9.blend")
    mats = list(ob.data.materials)
    parts = parts_of(ob.data)

    body, slide, hands = [], [], {"l": [], "r": []}
    for p in parts:
        mn, mx, ctr, size = bbox(p)
        mat_set = {mi for _, mi in p[1]}
        glove = any(mats[mi] and mats[mi].name == "MAT_Glove" for mi in mat_set)
        if glove and size[1] > 0.3:
            hands["r" if ctr[0] > 0 else "l"].append(p)
        elif glove and ctr[0] < -0.02:
            hands["l"].append(p)
        elif glove:
            hands["r"].append(p)
        elif abs(ctr[0]) < 0.01 and abs(ctr[2] - 0.026) < 0.02 and size[1] > 0.18:
            slide.append(p)
        else:
            body.append(p)

    # 弹匣：塞进握把、底板略露出（原模型没有弹匣体，不会重影）
    mats.append(mats[0])
    mag = [box_part((0.0, -0.005, -0.10), (0.026, 0.055, 0.13), 0)]

    p_slide = (0.0, 0.075, 0.026)
    p_mag = (0.0, -0.005, -0.035)
    p_hand_l = (-0.123, -0.06, -0.212)
    p_hand_r = (0.093, -0.06, -0.215)

    meshes = [
        make_object("Body", body, mats),
        make_object("Slide", slide, mats, p_slide),
        make_object("Mag", mag, mats, p_mag),
        make_object("HandL", hands["l"], mats, p_hand_l),
        make_object("HandR", hands["r"], mats, p_hand_r),
    ]

    pivots = [
        pivot("SlidePivot", p_slide),
        pivot("MagPivot", p_mag),
        pivot("HandLPivot", p_hand_l),
        pivot("HandRPivot", p_hand_r),
    ]
    attach(meshes[1], pivots[0])
    attach(meshes[2], pivots[1])
    attach(meshes[3], pivots[2])
    attach(meshes[4], pivots[3])

    export("WPN_P9", meshes + pivots)
    print("WEAPON_OK body=%d slide=%d handL=%d handR=%d" %
          (len(body), len(slide), len(hands["l"]), len(hands["r"])))


# ---------------------------------------------------------------- 敌人
def build_enemy(blend, name):
    ob = open_mesh(blend)
    mats = list(ob.data.materials)
    parts = parts_of(ob.data)

    legs = []
    for p in parts:
        mn, mx, ctr, size = bbox(p)
        if size[2] > 0.5 and ctr[2] < 0.9:
            legs.append((p, mn, mx, ctr, size))
    assert len(legs) == 2, "腿部识别失败：%d" % len(legs)
    leg_top = max(l[2][2] for l in legs)
    leg_bottom = min(l[1][2] for l in legs)
    knee = (leg_top + leg_bottom) / 2

    groups = {k: [] for k in ("hips", "torso", "head", "thigh_l", "shin_l", "thigh_r", "shin_r")}
    leg_ids = {id(l[0]) for l in legs}

    for p in parts:
        if id(p) in leg_ids:
            continue
        mn, mx, ctr, size = bbox(p)
        if ctr[2] > 1.5:
            groups["head"].append(p)
        elif ctr[2] > 1.05:
            groups["torso"].append(p)
        elif ctr[2] > leg_top - 0.16:
            groups["hips"].append(p)
        else:
            groups["torso"].append(p)

    # 腿改成上下两段盒体（保留原截面与材质），膝盖处才有独立枢轴
    for p, mn, mx, ctr, size in legs:
        side = "l" if ctr[0] < 0 else "r"
        mat = max({mi: 0 for _, mi in p[1]}, key=lambda k: sum(1 for _, mi in p[1] if mi == k))
        cx = (mn[0] + mx[0]) / 2
        cy = (mn[1] + mx[1]) / 2
        sx = mx[0] - mn[0]
        sy = mx[1] - mn[1]
        groups["thigh_" + side].append(box_part((cx, cy, (knee + leg_top) / 2),
                                                (sx, sy, leg_top - knee), mat))
        groups["shin_" + side].append(box_part((cx, cy, (leg_bottom + knee) / 2),
                                               (sx, sy, knee - leg_bottom), mat))

    hip = leg_top + 0.02
    p_hips = (0.0, 0.0, hip)
    p_torso = (0.0, 0.0, hip + 0.1)
    p_head = (0.0, 0.0, hip + 0.63)
    p_thigh_l = (-0.115, 0.0, leg_top - 0.04)
    p_shin_l = (-0.115, 0.0, knee)
    p_thigh_r = (0.115, 0.0, leg_top - 0.04)
    p_shin_r = (0.115, 0.0, knee)

    meshes = {
        "hips": make_object("Hips", groups["hips"], mats, p_hips),
        "torso": make_object("Torso", groups["torso"], mats, p_torso),
        "head": make_object("Head", groups["head"], mats, p_head),
        "thigh_l": make_object("ThighL", groups["thigh_l"], mats, p_thigh_l),
        "shin_l": make_object("ShinL", groups["shin_l"], mats, p_shin_l),
        "thigh_r": make_object("ThighR", groups["thigh_r"], mats, p_thigh_r),
        "shin_r": make_object("ShinR", groups["shin_r"], mats, p_shin_r),
    }

    hips = pivot("HipsPivot", p_hips)
    torso = pivot("TorsoPivot", p_torso, hips, p_hips)
    head = pivot("HeadPivot", p_head, torso, p_torso)
    thigh_l = pivot("ThighLPivot", p_thigh_l, hips, p_hips)
    shin_l = pivot("ShinLPivot", p_shin_l, thigh_l, p_thigh_l)
    thigh_r = pivot("ThighRPivot", p_thigh_r, hips, p_hips)
    shin_r = pivot("ShinRPivot", p_shin_r, thigh_r, p_thigh_r)

    attach(meshes["hips"], hips)
    attach(meshes["torso"], torso)
    attach(meshes["head"], head)
    attach(meshes["thigh_l"], thigh_l)
    attach(meshes["shin_l"], shin_l)
    attach(meshes["thigh_r"], thigh_r)
    attach(meshes["shin_r"], shin_r)

    export(name, list(meshes.values()) + [hips, torso, head, thigh_l, shin_l, thigh_r, shin_r])
    print("ENEMY_OK %s knee=%.3f parts=%s" %
          (name, knee, {k: len(v) for k, v in groups.items()}))


build_weapon()
build_enemy(r"e:\GodotProject\super-hero\art\blender\SM_StreetRunner.blend", "SM_StreetRunner")
build_enemy(r"e:\GodotProject\super-hero\art\blender\SM_Holder.blend", "SM_Holder")
