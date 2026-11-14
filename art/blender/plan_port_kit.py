"""解析 PortLevel.tscn，把每个 blockout 盒体映射到模块化 kit 家族，产出 kit_plan.json。
用法：python art/blender/plan_port_kit.py <out_json>
"""
import re, json, sys, collections

LEVEL = r"e:\GodotProject\super-hero\scenes\levels\PortLevel.tscn"


def family_of(node):
    n = node
    if n.startswith("Rail_") or n == "Pier_Rail_W":
        return "Rail"
    if n.startswith("Tower_Pile_"):
        return "Post"
    if n.startswith("Tower_Ring_"):
        return "Beam"
    if n == "Crane_Boom":
        return "Beam"
    if n == "Tower_Shaft":
        return "TowerShaft"
    if n == "Crane_Cab":
        return "CraneCab"
    if n == "Crane_Counterweight":
        return "Counterweight"
    if n.startswith("Ramp_"):
        return "Ramp"
    if n.startswith("WH_Wall_") or n.startswith("WH_Lintel_"):
        return "Wall"
    if n.startswith("WH_Shelf_"):
        return "Rack"
    if n.startswith("WH_Crate_"):
        return "Crate"
    if n.startswith("Barrier_"):
        return "Barrier"
    if n.startswith("Barrel_"):
        return "Barrel"
    if n and n[0].isdigit() is not None and n.startswith("Forklift"):
        return "Forklift"
    if n.startswith("Pier_Bollard_"):
        return "Bollard"
    if n.startswith("Backdrop_Ship_Hull"):
        return "Hull"
    if n.startswith("Backdrop_Ship_Bridge"):
        return "ShipBridge"
    if n.startswith("Backdrop_Crane_"):
        return "BgCrane"
    if n.startswith("L2_Support_") or n.startswith("L1a_Post_") or n.startswith("L1b_Post_"):
        return "Post"
    if re.match(r"Roof_[A-E]_Leg_\d", n):
        return "Post"
    if re.match(r"Roof_[A-E]_Body$", n):
        return "Container"
    if re.match(r"Roof_[A-E]$", n):
        return "Deck"
    if n.startswith("Yard_Box") or n.startswith("Truck_Cargo"):
        return "Container"
    if n.endswith("_Cover") or n.endswith("_Cover_A") or n.endswith("_Cover_G"):
        return "Container"
    if n.startswith("L0_Crate") or n.startswith("L1a_EquipmentBox") or n.startswith("L2_Cargo_"):
        return "Container"
    if n == "WH_Roof":
        return "Deck"
    return "Deck"


def fmt(v):
    s = ("%.3f" % v).rstrip("0").rstrip(".")
    return s.replace(".", "p").replace("-", "m")


def main(out_path):
    src = open(LEVEL, encoding="utf-8").read()

    subs = {}
    for m in re.finditer(r'^\[sub_resource type="([^"]+)" id="([^"]+)"\]\n(.*?)(?=\n\[|\Z)', src, re.S | re.M):
        subs[m.group(2)] = (m.group(1), m.group(3))

    def prop(body, key):
        m = re.search(r'^' + re.escape(key) + r' = (.*)$', body, re.M)
        return m.group(1) if m else None

    nodes = []
    for m in re.finditer(r'^\[node name="([^"]+)" type="([^"]+)" parent="([^"]*)"(.*?)\]\n(.*?)(?=\n\[node|\Z)', src, re.S | re.M):
        nodes.append(dict(name=m.group(1), type=m.group(2), parent=m.group(3), body=m.group(5)))

    entries = []
    for n in nodes:
        if n["type"] != "MeshInstance3D":
            continue
        mesh = prop(n["body"], "mesh")
        if not mesh:
            continue
        mid = re.search(r'SubResource\("([^"]+)"\)', mesh).group(1)
        if mid not in subs or subs[mid][0] != "BoxMesh":
            continue
        size = re.search(r'Vector3\(([^)]+)\)', prop(subs[mid][1], "size")).group(1)
        w, h, d = [float(x) for x in size.split(",")]
        mat = prop(n["body"], "material_override")
        mat_id = re.search(r'SubResource\("([^"]+)"\)', mat).group(1) if mat else None
        color = [0.8, 0.8, 0.8]
        if mat_id and mat_id in subs:
            mc = re.search(r'shader_parameter/albedo_color = Color\(([^)]+)\)', subs[mat_id][1])
            if mc:
                color = [float(x) for x in mc.group(1).split(",")][:3]
        fam = family_of(n["parent"])
        glb = "SM_%s_%sx%sx%s" % (fam, fmt(w), fmt(h), fmt(d))
        entries.append(dict(node=n["name"], parent=n["parent"], family=fam,
                            size=[w, h, d], glb=glb, mat=mat_id, color=color))

    out = dict(level=LEVEL, pieces=entries)
    open(out_path, "w", encoding="utf-8").write(json.dumps(out, ensure_ascii=False, indent=1))

    c = collections.Counter((e["family"], e["glb"]) for e in entries)
    fams = collections.Counter(e["family"] for e in entries)
    print("mesh nodes:", len(entries), "unique glb:", len(c))
    for k, v in fams.most_common():
        print("  %-12s %3d uses  %2d variants" % (k, v, len({g for (f, g) in c if f == k})))
    for g, _ in sorted(c):
        print("   ", g)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "kit_plan.json")
