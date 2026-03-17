"""Generate a wine-red themed magnifier icon for the WindowsMagnifier app."""
import struct
import math
import zlib

# ── 主题色 ──────────────────────────────────────────────
BG_DARK    = (0x18, 0x18, 0x1E)   # 深色背景
WINE_DARK  = (0x5C, 0x14, 0x28)   # 酒红暗色
WINE_MID   = (0x8B, 0x1E, 0x3C)   # 酒红主色
WINE_LIGHT = (0xC0, 0x30, 0x5A)   # 酒红亮色
GLASS_BG   = (0x22, 0x22, 0x2E)   # 镜片内部深色
WHITE      = (0xFF, 0xFF, 0xFF)

def lerp(a, b, t):
    return a + (b - a) * t

def blend(fg, fg_a, bg):
    """Alpha-blend fg onto bg."""
    fa = fg_a / 255.0
    return tuple(int(lerp(bg[i], fg[i], fa)) for i in range(3))

def smooth_step(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3 - 2 * t)

def create_png(size):
    s = size
    pixels = bytearray(s * s * 4)

    # 几何参数（按比例）
    cx = s * 0.42          # 圆心略偏左上，给把手留空间
    cy = s * 0.42
    lens_r   = s * 0.30    # 镜片内径
    ring_w   = s * 0.07    # 镜框宽度
    ring_r   = lens_r + ring_w  # 镜框外径
    bg_r     = ring_r + s * 0.03  # 背景圆半径

    # 把手参数（45° 方向延伸到右下）
    hx0 = cx + lens_r * 0.72
    hy0 = cy + lens_r * 0.72
    hlen  = s * 0.30
    hwid  = s * 0.065
    hx1 = hx0 + hlen * 0.7071
    hy1 = hy0 + hlen * 0.7071

    aa = 1.2  # 抗锯齿半径

    for y in range(s):
        for x in range(s):
            idx = (y * s + x) * 4
            px, py = x + 0.5, y + 0.5
            dx, dy = px - cx, py - cy
            dist = math.hypot(dx, dy)

            # ── 背景圆（深色底盘）──
            bg_alpha = int(255 * smooth_step(bg_r + aa, bg_r - aa, dist))

            if bg_alpha == 0:
                continue  # 全透明

            # 基础像素 = 背景色
            r, g, b = BG_DARK
            a = bg_alpha

            # ── 镜片内部（深色半透明玻璃）──
            lens_a = smooth_step(lens_r + aa, lens_r - aa, dist)
            if lens_a > 0:
                # 镜片内部径向渐变（中心稍亮）
                rad_t = 1.0 - min(dist / lens_r, 1.0)
                gc = tuple(int(GLASS_BG[i] + (0x30 * rad_t)) for i in range(3))
                r, g, b = blend(gc, int(lens_a * 200), (r, g, b))

            # ── 镜框（酒红环）──
            in_ring = (ring_r - aa < dist < ring_r + aa) or (lens_r - aa < dist < lens_r + aa)
            ring_inner_a = smooth_step(lens_r - aa, lens_r + aa, dist)  # 内边
            ring_outer_a = smooth_step(ring_r + aa, ring_r - aa, dist)  # 外边
            ring_mask = ring_outer_a * (1.0 - ring_inner_a)
            if ring_mask > 0.01:
                # 镜框渐变：顶部偏亮，底部偏暗（金属感）
                angle = math.atan2(dy, dx)
                t_shine = 0.5 + 0.5 * math.cos(angle - math.pi * 1.25)
                rc = tuple(int(lerp(WINE_DARK[i], WINE_LIGHT[i], t_shine * 0.6)) for i in range(3))
                r, g, b = blend(rc, int(ring_mask * 255), (r, g, b))

            # ── 镜框高光（左上弧）──
            highlight_a = smooth_step(ring_r + aa * 0.3, ring_r - aa * 0.3, dist) * \
                          (1.0 - smooth_step(lens_r + aa * 0.3, lens_r - aa * 0.3, dist))
            angle_hl = math.atan2(dy, dx)
            hl_strength = max(0.0, math.cos(angle_hl - math.pi * 1.4)) ** 2
            if highlight_a > 0 and hl_strength > 0.05:
                r, g, b = blend(WHITE, int(highlight_a * hl_strength * 120), (r, g, b))

            # ── 十字准线（镜片内细线）──
            cross_w = max(0.8, s * 0.018)
            cross_len = lens_r * 0.52
            in_cross_h = abs(dy) < cross_w and abs(dx) < cross_len
            in_cross_v = abs(dx) < cross_w and abs(dy) < cross_len
            if (in_cross_h or in_cross_v) and dist < lens_r - aa:
                cross_fade = smooth_step(lens_r - aa, lens_r - aa * 3, dist)
                r, g, b = blend(WHITE, int(cross_fade * 160), (r, g, b))

            # ── 把手 ──
            # 把手是沿 45° 方向的圆角矩形，用点到线段距离实现
            # 线段：(hx0,hy0) → (hx1,hy1)
            hdx, hdy = hx1 - hx0, hy1 - hy0
            hlen2 = hdx * hdx + hdy * hdy
            if hlen2 > 0:
                t_h = max(0.0, min(1.0, ((px - hx0) * hdx + (py - hy0) * hdy) / hlen2))
                proj_x = hx0 + t_h * hdx
                proj_y = hy0 + t_h * hdy
                dist_h = math.hypot(px - proj_x, py - proj_y)
                handle_a = smooth_step(hwid + aa, hwid - aa, dist_h)
                if handle_a > 0:
                    # 把手颜色渐变：近端酒红，末端暗色
                    hc = tuple(int(lerp(WINE_LIGHT[i], WINE_DARK[i], t_h * 0.7)) for i in range(3))
                    # 把手高光
                    hl_h = max(0.0, 1.0 - (dist_h / hwid)) * 0.35
                    hc = tuple(int(min(255, hc[i] + 40 * hl_h)) for i in range(3))
                    r, g, b = blend(hc, int(handle_a * 255), (r, g, b))

            pixels[idx]   = r
            pixels[idx+1] = g
            pixels[idx+2] = b
            pixels[idx+3] = a

    return encode_png(s, s, pixels)

def encode_png(w, h, rgba_data):
    """Minimal PNG encoder for RGBA data."""
    def chunk(ctype, data):
        c = ctype + data
        crc = struct.pack('>I', zlib.crc32(c) & 0xffffffff)
        return struct.pack('>I', len(data)) + c + crc

    # PNG signature
    sig = b'\x89PNG\r\n\x1a\n'

    # IHDR
    ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)  # 8-bit RGBA

    # IDAT - raw pixel rows with filter byte 0
    raw = bytearray()
    for y in range(h):
        raw.append(0)  # filter: none
        row_start = y * w * 4
        raw.extend(rgba_data[row_start:row_start + w * 4])

    compressed = zlib.compress(bytes(raw), 9)

    # IEND
    return sig + chunk(b'IHDR', ihdr) + chunk(b'IDAT', compressed) + chunk(b'IEND', b'')

def create_ico(sizes):
    """Create ICO file with multiple sizes."""
    images = []
    for s in sizes:
        png_data = create_png(s)
        images.append((s, png_data))

    # ICO header: reserved(2) + type(2) + count(2)
    header = struct.pack('<HHH', 0, 1, len(images))

    # Calculate offsets
    dir_size = 16 * len(images)
    offset = 6 + dir_size

    directory = bytearray()
    image_data = bytearray()

    for size, png in images:
        w = 0 if size == 256 else size
        h = 0 if size == 256 else size
        # ICONDIRENTRY: width, height, colors, reserved, planes, bpp, size, offset
        entry = struct.pack('<BBBBHHII', w, h, 0, 0, 1, 32, len(png), offset)
        directory.extend(entry)
        image_data.extend(png)
        offset += len(png)

    return header + bytes(directory) + bytes(image_data)

if __name__ == '__main__':
    import os
    script_dir = os.path.dirname(os.path.abspath(__file__))
    output_path = os.path.join(script_dir, 'magnifier.ico')
    ico_data = create_ico([16, 32, 48, 64, 128, 256])
    with open(output_path, 'wb') as f:
        f.write(ico_data)
    print(f"ICO created: {len(ico_data)} bytes")
