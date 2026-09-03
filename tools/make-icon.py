"""Draws the VmView icon (tray + window + exe) and packs it into Assets/vmview.ico.

A blue rounded tile with a white monitor on it: the bezel is a thick white frame so it still
reads at 16 px, the screen shows a light-blue "picture", and a stand sits underneath.
Every size is rendered from the same geometry at 8x and downsampled with Lanczos.
"""
from pathlib import Path
from PIL import Image, ImageDraw

BLUE = (37, 99, 235, 255)        # #2563EB, the app accent
SCREEN = (219, 234, 254, 255)    # #DBEAFE, AccentSoft
WHITE = (255, 255, 255, 255)
GREEN = (22, 163, 74, 255)       # #16A34A, "live"

def render(size: int, live_dot: bool) -> Image.Image:
    ss = 8
    s = size * ss
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = s / 16  # geometry is expressed on a 16-unit grid so small sizes stay crisp

    # tile
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=3.2 * u, fill=BLUE)

    # monitor bezel: white frame, slightly wider than tall
    bx0, by0, bx1, by1 = 2 * u, 3 * u, 14 * u, 11.2 * u
    d.rounded_rectangle((bx0, by0, bx1, by1), radius=1.2 * u, fill=WHITE)
    # screen
    inset = 1.1 * u
    d.rounded_rectangle((bx0 + inset, by0 + inset, bx1 - inset, by1 - inset), radius=0.5 * u, fill=SCREEN)
    # stand: neck + foot
    d.rectangle((7.2 * u, 11.2 * u, 8.8 * u, 12.6 * u), fill=WHITE)
    d.rounded_rectangle((4.6 * u, 12.4 * u, 11.4 * u, 13.6 * u), radius=0.5 * u, fill=WHITE)

    if live_dot:
        r = 2.6 * u
        cx, cy = 12.6 * u, 12.2 * u
        d.ellipse((cx - r - 0.7 * u, cy - r - 0.7 * u, cx + r + 0.7 * u, cy + r + 0.7 * u), fill=BLUE)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=GREEN)

    return img.resize((size, size), Image.LANCZOS)

def main() -> None:
    root = Path(__file__).resolve().parent.parent
    assets = root / "Assets"
    preview = root / "tools" / "icon-preview"
    assets.mkdir(exist_ok=True)
    preview.mkdir(exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    for name, live in (("vmview", False), ("vmview-live", True)):
        frames = [render(sz, live) for sz in sizes]
        frames[-1].save(assets / f"{name}.ico", format="ICO", sizes=[(sz, sz) for sz in sizes],
                        append_images=frames[:-1])

    # Contact sheet: every tray-relevant size at 4x nearest-neighbour so the pixel grid can be judged.
    row = (16, 24, 32, 48)
    width = sum(sz * 4 for sz in row) + 12 * (len(row) + 1)
    sheet = Image.new("RGBA", (width, 48 * 4 * 2 + 36), (240, 240, 240, 255))
    for r, live in enumerate((False, True)):
        x = 12
        for sz in row:
            im = render(sz, live).resize((sz * 4, sz * 4), Image.NEAREST)
            sheet.alpha_composite(im, (x, 12 + r * (48 * 4 + 12) + (48 * 4 - sz * 4)))
            x += sz * 4 + 12
    sheet.save(preview / "sheet.png")

    # 16 px at 1:1 on a light and a dark taskbar, scaled 4x.
    strip = Image.new("RGBA", (200, 48), (0, 0, 0, 0))
    d = ImageDraw.Draw(strip)
    d.rectangle((0, 0, 100, 48), fill=(243, 243, 243, 255))
    d.rectangle((100, 0, 200, 48), fill=(32, 32, 32, 255))
    for i, live in enumerate((False, True)):
        strip.alpha_composite(render(16, live), (20 + i * 40, 16))
        strip.alpha_composite(render(16, live), (120 + i * 40, 16))
    strip.resize((800, 192), Image.NEAREST).save(preview / "tray.png")

if __name__ == "__main__":
    main()
