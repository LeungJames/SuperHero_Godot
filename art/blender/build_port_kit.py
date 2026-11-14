"""按 kit_plan.json 生成港口模块化低模资产（GLB，一米制、原点居中、包围盒尺寸与 blockout 盒体完全一致）。

用法：
  blender --background --factory-startup --python art/blender/build_port_kit.py -- <kit_plan.json> <out_dir>

坐标约定：脚本内全部用 Godot 坐标（X 右 / Y 上 / Z 后）建面，导出前统一转 Blender 坐标
(x, y, z)_godot -> (x, -z, y)_blender，因此导入 Godot 后朝向与尺寸无需额外旋转。
"""
import bpy
import bmesh
import json
import math
import os
import sys
from mathutils import Vector

TAU = math.pi * 2


# ---------------------------------------------------------------- 几何累加器
class MB:
    def __init__(self):
        self.v = []
        self.f = []

    def _quad(self, a, b, c, d):
        self.f.append((a, b, c, d))

    def box(self, center, size, u=(1, 0, 0), v=(0, 1, 0)):
        """以中心 + 尺寸建盒；u 为长度轴，v 为第二轴，第三轴 = u × v。"""
        cx, cy, cz = center
        sx, sy, sz = size
        u = Vector(u).normalized()
        v = Vector(v).normalized()
        w = u.cross(v).normalized()
        i = len(self.v)
        for k in (-1, 1):
            for j in (-1, 1):
                for m in (-1, 1):
                    p = Vector((cx, cy, cz)) + u * (k * sx / 2) + v * (j * sy / 2) + w * (m * sz / 2)
                    self.v.append(tuple(p))
        # 顶点顺序：(k,j,m) = (-,-,-) (-,-,+) (-,+,-) (-,+,+) (+,-,-) (+,-,+) (+,+,-) (+,+,+)
        b = i
        self._quad(b + 0, b + 1, b + 3, b + 2)
        self._quad(b + 4, b + 6, b + 7, b + 5)
        self._quad(b + 0, b + 4, b + 5, b + 1)
        self._quad(b + 2, b + 3, b + 7, b + 6)
        self._quad(b + 0, b + 2, b + 6, b + 4)
        self._quad(b + 1, b + 5, b + 7, b + 3)

    def ngon(self, cx, cy, cz, radius, height, n=8, axis="y", phase=None):
        """正 n 棱柱；axis 为柱体轴向。radius 为外接圆半径。"""
        phase = TAU / (2 * n) if phase is None else phase
        b = len(self.v)
        for s in (-1, 1):
            for i in range(n):
                a = phase + TAU * i / n
                dx, dz = radius * math.cos(a), radius * math.sin(a)
                if axis == "y":
                    self.v.append((cx + dx, cy + s * height / 2, cz + dz))
                else:  # 沿 X
                    self.v.append((cx + s * height / 2, cy + dx, cz + dz))
        for i in range(n):
            j = (i + 1) % n
            self._quad(b + i, b + j, b + n + j, b + n + i)
        self.f.append(tuple(b + i for i in range(n)))
        self.f.append(tuple(b + n + i for i in range(n)))

    def trape(self, w, h, d, top_ratio=0.5):
        """沿 X 拉伸的梯形棱柱（隔离墩）。"""
        hz_b, hz_t, hy = d / 2, d * top_ratio / 2, h / 2
        hx = w / 2
        b = len(self.v)
        for s in (-1, 1):
            self.v += [(s * hx, -hy, -hz_b), (s * hx, -hy, hz_b), (s * hx, hy, hz_t), (s * hx, hy, -hz_t)]
        self._quad(b + 0, b + 1, b + 2, b + 3)
        self._quad(b + 4, b + 7, b + 6, b + 5)
        self._quad(b + 0, b + 4, b + 5, b + 1)
        self._quad(b + 1, b + 5, b + 6, b + 2)
        self._quad(b + 2, b + 6, b + 7, b + 3)
        self._quad(b + 3, b + 7, b + 4, b + 0)


def axis_info(w, h, d):
    """返回 (长轴, 长度, 短轴, 短边)；长轴 'x' 或 'z'。"""
    if w >= d:
        return "x", w, "z", d
    return "z", d, "x", w


# ---------------------------------------------------------------- 各家族建模
def build_deck(w, h, d):
    mb = MB()
    mb.box((0, 0, 0), (w, h, d))
    if w * d > 1200:  # 码头大地坪：只保留主体与边缘倒角
        return mb
    kind, L, _, S = axis_info(w, h, d)
    rib_h = min(max(h * 0.55, 0.06), 0.22)
    n = max(2, min(6, int(L / 3.2)))
    for i in range(n):
        t = ((i + 0.5) / n - 0.5) * (L - 0.6)
        if kind == "x":
            mb.box((t, -h / 2 - rib_h * 0.12, 0), (S * 0.9, rib_h, 0.16))
        else:
            mb.box((0, -h / 2 - rib_h * 0.12, t), (0.16, rib_h, S * 0.9))
    # 四角吊装耳板
    for sx in (-1, 1):
        for sz in (-1, 1):
            mb.box((sx * (w / 2 - 0.12), -h * 0.06, sz * (d / 2 - 0.12)), (0.2, h * 0.8, 0.2))
    return mb


def build_rail(w, h, d):
    mb = MB()
    kind, L, _, S = axis_info(w, h, d)
    post = min(0.11, S * 0.7)
    if kind == "x":
        mb.box((0, -h / 2 + 0.06, 0), (L, 0.12, S))           # 踢脚板
        for y in (h / 2 - 0.055, h / 2 - 0.36, h / 2 - 0.68):  # 三道横杆
            mb.box((0, y, 0), (L, 0.085, S * 0.55))
        cnt = max(2, int(round(L / 1.6)) + 1)
        for i in range(cnt):
            x = -L / 2 + 0.07 + (L - 0.14) * i / (cnt - 1)
            mb.box((x, 0, 0), (post, h - 0.02, S * 0.55))
    else:
        mb.box((0, -h / 2 + 0.06, 0), (S, 0.12, L))
        for y in (h / 2 - 0.055, h / 2 - 0.36, h / 2 - 0.68):
            mb.box((0, y, 0), (S * 0.55, 0.085, L))
        cnt = max(2, int(round(L / 1.6)) + 1)
        for i in range(cnt):
            z = -L / 2 + 0.07 + (L - 0.14) * i / (cnt - 1)
            mb.box((0, 0, z), (S * 0.55, h - 0.02, post))
    return mb


def build_wall(w, h, d):
    mb = MB()
    kind, L, _, S = axis_info(w, h, d)
    mb.box((0, 0, 0), (w, h, d))
    rib_h = h * 0.94
    cnt = max(2, int(L / 1.1) + 1)
    for i in range(cnt):
        t = -L / 2 + 0.18 + (L - 0.36) * i / (cnt - 1) if cnt > 1 else 0
        if kind == "x":
            mb.box((t, 0, 0), (0.09, rib_h, S + 0.05))
        else:
            mb.box((0, 0, t), (S + 0.05, rib_h, 0.09))
    if kind == "x":
        mb.box((0, h / 2 - 0.07, 0), (L, 0.14, S + 0.08))
        mb.box((0, -h / 2 + 0.12, 0), (L, 0.24, S + 0.12))
    else:
        mb.box((0, h / 2 - 0.07, 0), (S + 0.08, 0.14, L))
        mb.box((0, -h / 2 + 0.12, 0), (S + 0.12, 0.24, L))
    return mb


def build_post(w, h, d):
    mb = MB()
    mb.box((0, 0, 0), (w, h, d))
    p = max(w, d)
    mb.box((0, -h / 2 + 0.05, 0), (p * 1.7, 0.1, d * 1.7))
    mb.box((0, h / 2 - 0.05, 0), (p * 1.45, 0.1, d * 1.45))
    if h > 6:
        n = max(1, int(h / 5))
        for i in range(n):
            y = -h / 2 + h * (i + 1) / (n + 1)
            mb.box((0, y, 0), (p * 1.3, 0.12, d * 1.3))
    return mb


def build_beam(w, h, d):
    mb = MB()
    kind, L, _, S = axis_info(w, h, d)
    cross = min(w, h, d)
    if cross < 0.45:
        mb.box((0, 0, 0), (w, h, d))
        for s in (-1, 1):
            if kind == "x":
                mb.box((s * (L / 2 - 0.06), 0, 0), (0.12, cross * 1.5, cross * 1.5))
            else:
                mb.box((0, 0, s * (L / 2 - 0.06)), (cross * 1.5, cross * 1.5, 0.12))
        return mb
    # 桁架梁：四根主弦 + 两侧斜撑 + 端封板
    ch = cross * 0.17
    y_off = cross / 2 - ch / 2
    lat_off = S / 2 - ch / 2
    for sy in (-1, 1):
        for sl in (-1, 1):
            if kind == "x":
                mb.box((0, sy * y_off, sl * lat_off), (L, ch, ch))
            else:
                mb.box((sl * lat_off, sy * y_off, 0), (ch, ch, L))
    cnt = max(2, int(L / 2.4))
    step = L / cnt
    ang = math.atan2(cross * 0.9, step)
    diag = math.hypot(cross * 0.9, step)
    for i in range(cnt):
        t = -L / 2 + step * (i + 0.5)
        for s in (-1, 1):
            if kind == "x":
                mb.box((t, 0, s * (S / 2)), (diag, 0.11, 0.13),
                       u=(math.cos(ang), math.sin(ang), 0), v=(0, 0, 1))
            else:
                mb.box((s * (S / 2), 0, t), (diag, 0.13, 0.11),
                       u=(0, math.sin(ang), math.cos(ang)), v=(1, 0, 0))
    # 端封板
    for s in (-1, 1):
        if kind == "x":
            mb.box((s * (L / 2 - 0.05), 0, 0), (0.1, cross * 1.25, S * 1.25))
        else:
            mb.box((0, 0, s * (L / 2 - 0.05)), (S * 1.25, cross * 1.25, 0.1))
    return mb


def build_tower_shaft(w, h, d):
    mb = MB()
    mb.box((0, 0, 0), (w * 0.82, h, d * 0.82))
    cw = 0.62
    for sx in (-1, 1):
        for sz in (-1, 1):
            mb.box((sx * (w / 2 - cw / 2), 0, sz * (d / 2 - cw / 2)), (cw, h, cw))
    # 横环
    levels = max(2, int(h / 7))
    for i in range(1, levels):
        y = -h / 2 + h * i / levels
        mb.box((0, y, 0), (w - 0.1, 0.3, 0.3))
        mb.box((0, y, 0), (0.3, 0.3, d - 0.1))
    # 四面 X 斜撑
    for i in range(levels):
        y0 = -h / 2 + h * i / levels
        y1 = -h / 2 + h * (i + 1) / levels
        bh = y1 - y0
        diag = math.hypot(bh, w * 0.8)
        ang = math.atan2(bh, w * 0.8)
        for sz in (-1, 1):
            for s in (-1, 1):
                c = (0, (y0 + y1) / 2, sz * (d / 2 - 0.1))
                mb.box(c, (diag, 0.16, 0.16),
                       u=(math.cos(s * ang), math.sin(s * ang), 0), v=(0, 0, 1))
        diag2 = math.hypot(bh, d * 0.8)
        ang2 = math.atan2(bh, d * 0.8)
        for sx in (-1, 1):
            for s in (-1, 1):
                c = (sx * (w / 2 - 0.1), (y0 + y1) / 2, 0)
                mb.box(c, (diag2, 0.16, 0.16), u=(0, math.sin(s * ang2), math.cos(s * ang2)), v=(1, 0, 0))
    # 底座
    mb.box((0, -h / 2 + 0.35, 0), (w + 0.35, 0.7, d + 0.35))
    mb.box((0, h / 2 - 0.2, 0), (w + 0.2, 0.4, d + 0.2))
    return mb


def build_crane_cab(w, h, d):
    mb = MB()
    mb.box((0, 0, 0), (w, h, d))
    mb.box((0, -h / 2 + 0.13, 0), (w * 1.12, 0.26, d * 1.12))     # 回转盘
    mb.box((0, h * 0.16, -d / 2 - 0.02), (w * 0.78, h * 0.34, 0.1))  # 前窗带
    mb.box((w / 2 + 0.02, h * 0.16, 0), (0.1, h * 0.3, d * 0.5))     # 侧窗
    mb.box((-w / 2 - 0.02, h * 0.16, 0), (0.1, h * 0.3, d * 0.5))
    mb.box((0, h / 2 + 0.06, -d * 0.1), (w * 1.06, 0.12, d * 0.7))   # 雨檐
    return mb


def build_counterweight(w, h, d):
    mb = MB()
    mb.box((0, 0, 0), (w, h, d))
    for sx in (-1, 1):
        mb.box((sx * w * 0.22, h / 2 + 0.13, 0), (0.16, 0.26, 0.36))  # 吊耳
    for i in range(3):
        t = (i - 1) * d * 0.28
        mb.box((0, 0, t), (w + 0.06, h * 0.9, 0.1))
    return mb


def build_container(w, h, d):
    mb = MB()
    mb.box((0, 0, 0), (w, h, d))
    kind, L, _, S = axis_info(w, h, d)
    rib_h = h * 0.84
    cnt = max(3, int(L / 0.45))
    for i in range(cnt):
        t = -L / 2 + L * (i + 0.5) / cnt
        for s in (-1, 1):
            if kind == "x":
                mb.box((t, 0, s * (S / 2 + 0.01)), (0.09, rib_h, 0.05))
            else:
                mb.box((s * (S / 2 + 0.01), 0, t), (0.05, rib_h, 0.09))
    # 上下横梁 + 角件
    for sy in (-1, 1):
        if kind == "x":
            for s in (-1, 1):
                mb.box((0, sy * (h / 2 - 0.06), s * (S / 2 + 0.015)), (L, 0.12, 0.05))
        else:
            for s in (-1, 1):
                mb.box((s * (S / 2 + 0.015), sy * (h / 2 - 0.06), 0), (0.05, 0.12, L))
    cs = min(0.16, min(w, h, d) * 0.16)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                mb.box((sx * (w / 2 - cs / 2), sy * (h / 2 - cs / 2), sz * (d / 2 - cs / 2)),
                       (cs, cs, cs))
    return mb


def build_rack(w, h, d):
    mb = MB()
    kind, L, _, S = axis_info(w, h, d)
    pw = 0.11
    for sx in (-1, 1):
        for sz in (-1, 1):
            if kind == "x":
                mb.box((sx * (L / 2 - pw / 2), 0, sz * (S / 2 - pw / 2)), (pw, h, pw))
            else:
                mb.box((sz * (S / 2 - pw / 2), 0, sx * (L / 2 - pw / 2)), (pw, h, pw))
    for i in range(4):
        y = -h / 2 + 0.08 + (h - 0.16) * i / 3
        if kind == "x":
            mb.box((0, y, 0), (L - 0.08, 0.09, S - 0.06))
        else:
            mb.box((0, y, 0), (S - 0.06, 0.09, L - 0.08))
    # 背板斜撑
    if kind == "x":
        mb.box((0, 0, -S / 2 + pw / 2), (L * 0.92, 0.08, pw))
    else:
        mb.box((-S / 2 + pw / 2, 0, 0), (pw, 0.08, L * 0.92))
    return mb


def build_crate(w, h, d):
    mb = MB()
    mb.box((0, 0, 0), (w * 0.96, h * 0.96, d * 0.96))
    bw = 0.1
    for sx in (-1, 1):
        for sy in (-1, 1):
            mb.box((0, sy * (h / 2 - bw / 2), sx * (d / 2 - bw / 2)), (w, bw, bw))
        for sz in (-1, 1):
            mb.box((sx * (w / 2 - bw / 2), sy * (h / 2 - bw / 2), 0), (bw, bw, d))
    for sz in (-1, 1):
        mb.box((sz * (d / 2 - bw / 2), 0, 0), (bw, h, bw))
        # 正面斜板
        diag = math.hypot(w * 0.9, h * 0.9)
        ang = math.atan2(h * 0.9, w * 0.9)
        mb.box((0, 0, sz * (d / 2 + 0.01)), (diag, 0.06, 0.12),
               u=(math.cos(ang), math.sin(ang), 0), v=(0, 0, 1))
    return mb


def build_barrier(w, h, d):
    mb = MB()
    mb.trape(w, h, d, 0.52)
    for s in (-1, 1):
        mb.box((s * (w / 2 + 0.02), 0, 0), (0.04, h * 0.86, d * 0.42))
    return mb


def build_barrel(w, h, d):
    mb = MB()
    r = min(w, d) / 2
    mb.ngon(0, 0, 0, r * 0.94, h, n=8)
    for y in (-h / 2 + 0.14, h / 2 - 0.14):
        mb.ngon(0, y, 0, r, 0.09, n=8)
    mb.ngon(0, h / 2 - 0.02, 0, r * 0.6, 0.06, n=8)
    return mb


def build_bollard(w, h, d):
    mb = MB()
    r = min(w, d) / 2
    mb.ngon(0, -h / 2 + 0.03, 0, r, 0.06, n=8)
    mb.ngon(0, 0, 0, r * 0.78, h, n=8)
    mb.ngon(0, h / 2 - 0.04, 0, r, 0.08, n=8)
    return mb


def build_ramp(w, h, d):
    mb = MB()
    mb.box((0, 0, 0), (w * 0.94, h * 0.6, d))
    for s in (-1, 1):
        mb.box((s * (w / 2 - 0.06), 0, 0), (0.12, h, d))       # 边梁
    cnt = max(3, int(d / 0.9))
    for i in range(cnt):
        z = -d / 2 + d * (i + 0.5) / cnt
        mb.box((0, h / 2 + 0.012, z), (w * 0.84, 0.05, 0.07))  # 防滑条
    return mb


def build_forklift(w, h, d):
    mb = MB()
    mb.box((0, -h * 0.14, d * 0.08), (w * 0.86, h * 0.36, d * 0.5))           # 车身
    mb.box((0, -h * 0.2, d * 0.38), (w * 0.8, h * 0.26, d * 0.16))           # 配重
    for s in (-1, 1):
        mb.box((s * w * 0.3, -h * 0.02, -d / 2 + 0.09), (0.1, h * 0.72, 0.12))  # 门架
        mb.box((s * w * 0.3, -h / 2 + 0.1, -d / 2 + 0.75), (0.12, 0.08, 1.05))  # 货叉
        mb.ngon(s * w * 0.38, -h / 2 + 0.26, -d * 0.22, h * 0.15, 0.16, n=8, axis="x")
    mb.box((0, h * 0.28, d * 0.06), (w * 0.86, 0.09, d * 0.42))               # 护顶
    for s in (-1, 1):
        for t in (-1, 1):
            mb.box((s * w * 0.36, h * 0.12, d * (0.06 + t * 0.16)), (0.07, h * 0.34, 0.07))
    return mb


def build_hull(w, h, d):
    mb = MB()
    mb.box((0, -h * 0.16, 0), (w * 0.92, h * 0.62, d))            # 船体
    mb.box((0, h * 0.18, 0), (w, h * 0.3, d))                     # 舷墙
    mb.box((0, h * 0.33, 0), (w * 0.99, 0.16, d * 1.01))          # 甲板线
    for i in range(3):
        mb.box(((i - 1) * w * 0.28, h * 0.36, 0), (w * 0.16, 0.5, d * 0.55))  # 舱口盖
    for s in (-1, 1):
        mb.box((0, h * 0.3, s * (d / 2 + 0.06)), (w, 0.1, 0.1))
    return mb


def build_ship_bridge(w, h, d):
    mb = MB()
    mb.box((0, -h * 0.1, 0), (w, h * 0.8, d))
    mb.box((0, h * 0.42, 0), (w * 1.06, 0.18, d * 1.06))         # 顶层平台
    mb.box((w / 2 + 0.03, h * 0.2, 0), (0.1, h * 0.26, d * 0.72))  # 前窗带
    mb.box((-w / 2 - 0.03, h * 0.2, 0), (0.1, h * 0.26, d * 0.72))
    mb.box((0, h * 0.62, d * 0.22), (w * 0.2, h * 0.28, d * 0.24))  # 烟囱
    for s in (-1, 1):
        mb.box((0, h * 0.3, s * (d / 2 + 0.04)), (w, 0.09, 0.09))
    return mb


def build_bg_crane(w, h, d):
    mb = MB()
    cw = 0.35
    for sx in (-1, 1):
        for sz in (-1, 1):
            mb.box((sx * (w / 2 - cw / 2), 0, sz * (d / 2 - cw / 2)), (cw, h, cw))
    levels = max(2, int(h / 6))
    for i in range(1, levels + 1):
        y = -h / 2 + h * i / levels
        mb.box((0, y, 0), (w - 0.1, 0.22, 0.22))
        mb.box((0, y, 0), (0.22, 0.22, d - 0.1))
    for i in range(levels):
        y0 = -h / 2 + h * i / levels
        bh = h / levels
        diag = math.hypot(bh, w * 0.85)
        ang = math.atan2(bh, w * 0.85)
        for sz in (-1, 1):
            mb.box((0, y0 + bh / 2, sz * (d / 2 - 0.12)), (diag, 0.1, 0.1),
                   u=(math.cos(ang), math.sin(ang), 0), v=(0, 0, 1))
            mb.box((0, y0 + bh / 2, sz * (d / 2 - 0.12)), (diag, 0.1, 0.1),
                   u=(math.cos(ang), -math.sin(ang), 0), v=(0, 0, 1))
    # 顶部臂架
    mb.box((0, h / 2 - 0.8, -d * 1.6), (0.35, 0.45, d * 3.4),
           u=(0, 0, 1), v=(1, 0, 0))
    mb.box((0, h / 2 - 0.85, d * 0.7), (0.9, 0.8, d * 1.1))      # 配重臂
    mb.box((0, h / 2 - 1.5, -d * 0.55), (1.4, 1.3, 1.7))         # 机房
    return mb


BUILDERS = dict(
    Deck=build_deck, Rail=build_rail, Wall=build_wall, Post=build_post, Beam=build_beam,
    TowerShaft=build_tower_shaft, CraneCab=build_crane_cab, Counterweight=build_counterweight,
    Container=build_container, Rack=build_rack, Crate=build_crate, Barrier=build_barrier,
    Barrel=build_barrel, Bollard=build_bollard, Ramp=build_ramp, Forklift=build_forklift,
    Hull=build_hull, ShipBridge=build_ship_bridge, BgCrane=build_bg_crane,
)


def make_object(name, mb, color):
    verts = [(x, -z, y) for (x, y, z) in mb.v]   # Godot -> Blender
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], mb.f)
    me.validate()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    mat = bpy.data.materials.get("M_Kit")
    if mat is None:
        mat = bpy.data.materials.new("M_Kit")
        mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1.0)
    mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.85
    me.materials.append(mat)
    return obj


def apply_bevel(obj, size):
    width = min(0.03, min(size) * 0.1)
    if width < 0.005:
        return
    m = obj.modifiers.new("Bevel", 'BEVEL')
    m.width = width
    m.segments = 1
    m.limit_method = 'ANGLE'
    m.angle_limit = math.radians(35)
    m.use_clamp_overlap = True
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=m.name)
    obj.select_set(False)


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    plan_path, out_dir = argv[0], argv[1]
    os.makedirs(out_dir, exist_ok=True)
    plan = json.load(open(plan_path, encoding="utf-8"))

    # 清空默认场景
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)

    pieces = {}
    for e in plan["pieces"]:
        pieces.setdefault(e["glb"], dict(glb=e["glb"], family=e["family"],
                                         size=e["size"], color=e["color"]))

    stats = []
    for name in sorted(pieces):
        p = pieces[name]
        w, h, d = p["size"]
        mb = BUILDERS[p["family"]](w, h, d)
        obj = make_object(name, mb, p["color"])
        apply_bevel(obj, (w, h, d))
        tris = tri_count(obj)
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.export_scene.gltf(
            filepath=os.path.join(out_dir, name + ".glb"),
            export_format='GLB',
            use_selection=True,
            export_apply=True,
            export_yup=True,
            export_cameras=False,
            export_lights=False,
            export_materials='EXPORT',
            export_animations=False,
        )
        obj.select_set(False)
        stats.append(dict(glb=name, family=p["family"], size=[round(x, 4) for x in p["size"]],
                          tris=tris, verts=len(obj.data.vertices)))
        bpy.data.objects.remove(obj, do_unlink=True)

    use = {}
    for e in plan["pieces"]:
        use[e["glb"]] = use.get(e["glb"], 0) + 1
    total = sum(s["tris"] * use[s["glb"]] for s in stats)
    for s in stats:
        s["uses"] = use[s["glb"]]
        s["tris_total"] = s["tris"] * s["uses"]
    json.dump(stats, open(os.path.join(out_dir, "kit_stats.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("KIT_OK pieces=%d tris_unique=%d tris_total=%d" %
          (len(stats), sum(s["tris"] for s in stats), total))
    for s in stats:
        print("  %-34s %6d tris x%2d = %7d" % (s["glb"], s["tris"], s["uses"], s["tris_total"]))


main()
