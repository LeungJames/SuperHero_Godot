"""把 PortLevel.tscn 里 blockout 的 BoxMesh 外观替换为 kit 低模实例。

只改 MeshInstance3D 节点：StaticBody3D / CollisionShape3D / NavigationRegion3D 的
transform、形状与导航多边形一个字都不动（脚本对它们完全透明）。

用法：python art/blender/apply_port_kit.py <kit_plan.json> [tscn_path]
"""
import json
import re
import sys

DEFAULT_LEVEL = r"e:\GodotProject\super-hero\scenes\levels\PortLevel.tscn"
NODE_RE = re.compile(
    r'(?m)^\[node name="(?P<name>[^"]+)" type="(?P<type>[^"]+)" parent="(?P<parent>[^"]*)"(?P<hdr>[^\]]*)\]\n'
    r'(?P<body>.*?)(?=\n\[node |\Z)', re.S)
SUB_RE = re.compile(r'(?m)^\[sub_resource type="(?P<type>[^"]+)" id="(?P<id>[^"]+)"\]\n(?P<body>.*?)(?=\n\[|\Z)', re.S)


def main(plan_path, level_path=DEFAULT_LEVEL):
    plan = json.load(open(plan_path, encoding="utf-8"))
    src = open(level_path, encoding="utf-8").read()

    pieces = {p["node"]: p for p in plan["pieces"]}
    assert len(pieces) == len(plan["pieces"]), "节点名不唯一，无法按名替换"

    # 每个 glb 一条 ext_resource
    ext_ids = {}
    for p in plan["pieces"]:
        if p["glb"] not in ext_ids:
            ext_ids[p["glb"]] = "kit_%02d" % len(ext_ids)
    ext_lines = ['[ext_resource type="PackedScene" path="res://art/models/kit/%s.glb" id="%s"]'
                 % (g, i) for g, i in ext_ids.items()]

    replaced = 0
    out = []
    pos = 0
    for m in NODE_RE.finditer(src):
        name, typ, parent, hdr, body = (m.group("name"), m.group("type"), m.group("parent"),
                                       m.group("hdr"), m.group("body"))
        if typ != "MeshInstance3D" or name not in pieces:
            continue
        p = pieces[name]
        out.append(src[pos:m.start()])
        mat = ""
        if p["mat"]:
            mat = 'material_override = SubResource("%s")\n' % p["mat"]
        out.append(
            '[node name="%s" parent="%s"%s instance=ExtResource("%s")]\n'
            '\n'
            '[node name="%s" parent="%s/%s" index="0"]\n'
            '%s'
            % (name, parent, hdr, ext_ids[p["glb"]], p["glb"], parent, name, mat))
        pos = m.end()
        replaced += 1
    out.append(src[pos:])
    text = "".join(out)

    # 在第一个 sub_resource 之前插入新的 ext_resource
    idx = text.find("\n[sub_resource ")
    text = text[:idx + 1] + "\n".join(ext_lines) + "\n" + text[idx + 1:]

    # 删除不再被引用的 BoxMesh 子资源
    used = set(re.findall(r'SubResource\("([^"]+)"\)', text))
    removed = 0

    def drop(m):
        nonlocal removed
        if m.group("type") == "BoxMesh" and m.group("id") not in used:
            removed += 1
            return ""
        return m.group(0) + "\n"

    text = SUB_RE.sub(drop, text)

    n_ext = len(re.findall(r'(?m)^\[ext_resource ', text))
    n_sub = len(re.findall(r'(?m)^\[sub_resource ', text))
    text = re.sub(r'^\[gd_scene load_steps=\d+', '[gd_scene load_steps=%d' % (n_ext + n_sub + 1), text, 1)

    open(level_path, "w", encoding="utf-8", newline="\n").write(text)
    print("replaced mesh nodes: %d / %d" % (replaced, len(pieces)))
    print("ext_resources: %d, sub_resources: %d (removed %d unused BoxMesh)" % (n_ext, n_sub, removed))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else DEFAULT_LEVEL)
