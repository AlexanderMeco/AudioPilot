"""生成 AudioGroup 的应用 / 托盘图标（纯标准库，输出传统 BMP 格式 ICO）。

用法：python tools/make_icon.py
输出：Assets/app.ico，包含 32 / 16 / 48 三种尺寸。

采用传统 BMP（而非 PNG 内嵌）格式，兼容性最好，
既能作为 csproj 的 ApplicationIcon 嵌入 Win32 资源，也能作为 EmbeddedResource 供托盘加载。
"""

import struct
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "Assets" / "app.ico"
SIZES = (32, 16, 48)


def rounded_rect(u, v, radius=0.20):
    """判断归一化坐标 (0..1) 是否落在圆角矩形内。"""
    dx = max(radius - u, 0.0, u - (1.0 - radius))
    dy = max(radius - v, 0.0, v - (1.0 - radius))
    return dx * dx + dy * dy <= radius * radius


def pixel(u, v):
    """返回该点的颜色，顺序为 BGRA（32bpp BMP 的像素格式）。"""
    if not rounded_rect(u, v):
        return (0, 0, 0, 0)

    # 蓝色渐变背景：B 最大、R 最小，左上角略亮
    t = (u + v) / 2.0
    base = (
        int(205 + 30 * (1 - t)),  # B
        int(120 + 40 * (1 - t)),  # G
        int(45 + 18 * (1 - t)),   # R
        255,                       # A
    )
    white = (255, 255, 255, 255)

    # 扬声器：左侧机身矩形 + 向右张开的喇叭
    if 0.20 <= u <= 0.34 and 0.43 <= v <= 0.57:
        return white
    if 0.34 < u <= 0.50:
        half = 0.07 + (u - 0.34) / 0.16 * 0.13
        if 0.50 - half <= v <= 0.50 + half:
            return white

    # 右侧两道声波（同心圆弧）
    d = ((u - 0.46) ** 2 + (v - 0.50) ** 2) ** 0.5
    if u > 0.55 and (abs(d - 0.24) < 0.030 or abs(d - 0.34) < 0.030):
        return white

    return base


def bmp_image(size):
    """把一帧图像编码成 ICO 内的 DIB（BITMAPINFOHEADER + XOR + AND）。"""
    rows = []
    for y in range(size):
        row = bytearray()
        for x in range(size):
            u = (x + 0.5) / size
            v = (y + 0.5) / size
            b, g, r, a = pixel(u, v)
            row += bytes((b, g, r, a))
        rows.append(bytes(row))

    xor = b"".join(reversed(rows))  # BMP 行序为自下而上
    mask_row_bytes = ((size + 31) // 32) * 4
    and_mask = bytes(mask_row_bytes * size)  # 全 0 表示不透明，透明度交给 alpha 通道

    header = struct.pack(
        "<IiiHHIIiiII",
        40,                          # biSize
        size,                        # biWidth
        size * 2,                    # biHeight = XOR 高度 + AND 高度
        1,                           # biPlanes
        32,                          # biBitCount
        0,                           # biCompression = BI_RGB
        len(xor) + len(and_mask),    # biSizeImage
        0, 0, 0, 0
    )
    return header + xor + and_mask


def main():
    images = [(size, bmp_image(size)) for size in SIZES]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = bytearray()
    body = bytearray()

    for size, data in images:
        entries += struct.pack(
            "<BBBBHHII",
            size, size,       # 宽、高（<256 时直接写像素数）
            0, 0,             # 调色板数、保留
            1, 32,            # planes、位深
            len(data), offset
        )
        body += data
        offset += len(data)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(header + entries + body)
    print(f"written: {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
