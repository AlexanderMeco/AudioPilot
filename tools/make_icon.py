"""从源 PNG 生成 AudioPilot 的应用 / 托盘图标（纯标准库，零第三方依赖）。

用法：python tools/make_icon.py
输入：Assets/icon-source.png（深色底 + 白色调音台图案，RGBA PNG）
输出：Assets/app.ico（32 / 16 / 48 三帧，传统 BMP 格式 ICO）

采用传统 BMP 而不是 PNG 内嵌格式，兼容性最好：既能作为 csproj 的
ApplicationIcon 嵌入 Win32 资源，也能作为 EmbeddedResource 供托盘加载。
"""

import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "Assets" / "icon-source.png"
OUTPUT = ROOT / "Assets" / "app.ico"
SIZES = (32, 16, 48)  # 32 放首位，.NET 的 new Icon(stream) 会取第一帧


def paeth_predictor(a, b, c):
    """PNG Paeth 滤波器使用的预测值。"""
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    if pb <= pc:
        return b
    return c


def decode_png(data):
    """解码 8bit RGBA、非隔行的 PNG，返回 (宽, 高, 每行 RGBA 数据)。"""
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("不是有效的 PNG 文件")

    pos = 8
    compressed = bytearray()
    width = height = 0
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        kind = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", payload)
            if (depth, color, interlace) != (8, 6, 0):
                raise ValueError("仅支持 8bit RGBA、非隔行 PNG")
        elif kind == b"IDAT":
            compressed += payload
        elif kind == b"IEND":
            break

    raw = zlib.decompress(bytes(compressed))
    stride = width * 4
    rows = []
    previous = bytearray(stride)
    offset = 0
    for _ in range(height):
        filter_type = raw[offset]
        offset += 1
        line = bytearray(raw[offset:offset + stride])
        offset += stride

        # 逐字节还原 PNG 的行滤波器，bpp 固定为 4（RGBA）。
        for i in range(stride):
            left = line[i - 4] if i >= 4 else 0
            above = previous[i]
            upper_left = previous[i - 4] if i >= 4 else 0
            if filter_type == 1:
                line[i] = (line[i] + left) & 0xFF
            elif filter_type == 2:
                line[i] = (line[i] + above) & 0xFF
            elif filter_type == 3:
                line[i] = (line[i] + ((left + above) >> 1)) & 0xFF
            elif filter_type == 4:
                line[i] = (line[i] + paeth_predictor(left, above, upper_left)) & 0xFF
            elif filter_type != 0:
                raise ValueError(f"未知 PNG 滤波器：{filter_type}")

        rows.append(bytes(line))
        previous = line
    return width, height, rows


def crop_to_artwork(width, height, rows, threshold=110, margin_ratio=0.10):
    """裁掉源图四周的大片纯色留白，让图案在小尺寸下仍然清晰。

    源图通常是正方形画布，真正的图标只占中间一块；直接把 600px 缩到 32px
    会把细线条平均成灰色。这里按亮度找出图案外接框，向四周留一点边距后
    裁成正方形，再交给 resize。
    """
    min_x, min_y, max_x, max_y = width, height, -1, -1
    for y, row in enumerate(rows):
        for x in range(width):
            i = x * 4
            brightness = (row[i] + row[i + 1] + row[i + 2]) // 3
            if brightness > threshold:
                min_x = min(min_x, x)
                min_y = min(min_y, y)
                max_x = max(max_x, x)
                max_y = max(max_y, y)

    if max_x < min_x:
        return width, height, rows  # 没识别出图案，保持原图

    content_w = max_x - min_x + 1
    content_h = max_y - min_y + 1
    side = int(max(content_w, content_h) * (1 + 2 * margin_ratio))
    cx, cy = (min_x + max_x) // 2, (min_y + max_y) // 2

    x0 = max(0, cx - side // 2)
    y0 = max(0, cy - side // 2)
    x1 = min(width, x0 + side)
    y1 = min(height, y0 + side)
    x0 = max(0, x1 - side)
    y0 = max(0, y1 - side)

    cropped = [row[x0 * 4:x1 * 4] for row in rows[y0:y1]]
    return x1 - x0, y1 - y0, cropped


def resize(width, height, rows, size):
    """盒式平均缩放：线条图标缩到 16/32 像素时比最近邻更平滑。"""
    # 取左上角像素作为背景色基准（源图四周是纯色底）。
    bg_r, bg_g, bg_b = rows[0][0], rows[0][1], rows[0][2]
    bg_value = (bg_r + bg_g + bg_b) / 3
    scale_x = width / size
    scale_y = height / size
    result = []
    for y in range(size):
        y0 = int(y * scale_y)
        y1 = min(height, max(y0 + 1, int((y + 1) * scale_y)))
        line = bytearray()
        for x in range(size):
            x0 = int(x * scale_x)
            x1 = min(width, max(x0 + 1, int((x + 1) * scale_x)))
            r = g = b = a = count = 0
            for yy in range(y0, y1):
                src = rows[yy]
                for xx in range(x0, x1):
                    index = xx * 4
                    r += src[index]
                    g += src[index + 1]
                    b += src[index + 2]
                    a += src[index + 3]
                    count += 1
            r, g, b = r // count, g // count, b // count
            # 盒式平均会把细线冲淡，按「与背景色的亮度差」做一次线性增强，
            # 让 16 / 32 像素下的白色线条保持锐利。
            v = (r + g + b) / 3
            if v > bg_value:
                t = min(1.0, (v - bg_value) / (255 - bg_value) * 1.5)
                r = int(bg_r + (255 - bg_r) * t)
                g = int(bg_g + (255 - bg_g) * t)
                b = int(bg_b + (255 - bg_b) * t)
            line += bytes((min(255, r), min(255, g), min(255, b), a // count))
        result.append(bytes(line))
    return result


def encode_bmp_frame(size, rows):
    """把 RGBA 像素编码成 ICO 内的一帧 DIB（BITMAPINFOHEADER + XOR + AND）。"""
    xor = bytearray()
    for row in reversed(rows):  # BMP 行序为自下而上
        for i in range(0, len(row), 4):
            r, g, b, a = row[i:i + 4]
            xor += bytes((b, g, r, a))  # 32bpp 像素顺序为 BGRA

    mask_row_bytes = ((size + 31) // 32) * 4
    and_mask = bytes(mask_row_bytes * size)  # 全 0：不透明，透明度交给 alpha
    header = struct.pack(
        "<IiiHHIIiiII",
        40, size, size * 2, 1, 32, 0,
        len(xor) + len(and_mask), 0, 0, 0, 0
    )
    return header + bytes(xor) + and_mask


def main():
    width, height, rows = decode_png(SOURCE.read_bytes())
    source_size = (width, height)
    width, height, rows = crop_to_artwork(width, height, rows)
    print(f"source : {source_size[0]}x{source_size[1]} -> cropped {width}x{height}")
    frames = [
        (size, encode_bmp_frame(size, resize(width, height, rows, size)))
        for size in SIZES
    ]

    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries = bytearray()
    body = bytearray()
    for size, frame in frames:
        entries += struct.pack(
            "<BBBBHHII", size, size, 0, 0, 1, 32, len(frame), offset
        )
        body += frame
        offset += len(frame)

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_bytes(header + entries + body)
    print(f"output : {OUTPUT} ({OUTPUT.stat().st_size} bytes), sizes={SIZES}")


if __name__ == "__main__":
    main()
