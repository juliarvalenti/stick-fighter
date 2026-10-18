#!/usr/bin/env python3
"""Draws the app icon: a stick figure mid high kick, in player 1's red on the stage paper.

    python3 tools/icon/make_icon.py        # needs Pillow; macOS for iconutil

Writes assets/icon/icon.png (the window icon, 256 px) and assets/icon/StickFighter.icns (the Mac
app icon, every size from 16 to 1024 px). Each size is drawn on its own rather than scaled down,
so the limbs stay at least about two pixels thick and the kick still reads at 16 px. Commit the
outputs; the export preset and project.godot point at them.

Colors are the style guide's tokens (game/Style/Palette.cs): Paper, Ink, P1, Accent.
"""

import math
import os
import shutil
import subprocess
import tempfile

from PIL import Image, ImageDraw

PAPER = (0xF2, 0xEE, 0xE3)
INK = (0x17, 0x16, 0x1C)
P1 = (0xC8, 0x10, 0x2E)
ACCENT = (0xFF, 0xC2, 0x1A)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "icon")

# The figure in a 100-unit box, y down: leaning back, the back leg planted, the front leg kicking
# high, the back arm thrown back for balance and the front fist up.
HEAD = ((30, 22), 10)
BONES = [
    ((43, 60), (35, 33)),  # spine
    ((35, 33), (23, 42)), ((23, 42), (13, 35)),  # back arm
    ((35, 33), (46, 38)), ((46, 38), (52, 28)),  # front arm
    ((43, 60), (41, 75)), ((41, 75), (36, 90)),  # standing leg
    ((43, 60), (62, 49)), ((62, 49), (83, 34)),  # kicking leg
]
KICK = (83, 34)

SUPERSAMPLE = 8


def draw(size):
    """The icon at `size` px."""
    s = size * SUPERSAMPLE
    image = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)

    # The tile: Apple's grid insets it on large icons; small ones fill more of the space.
    inset = 0.1 if size >= 128 else 0.04
    lo, hi = s * inset, s * (1 - inset)
    radius = (hi - lo) * 0.225
    d.rounded_rectangle([lo, lo, hi, hi], radius=radius, fill=PAPER, outline=INK, width=max(SUPERSAMPLE, int(s * 0.012)))

    # Map the 100-unit figure into the tile, a little below centre so the kick has room.
    span = hi - lo
    def at(p):
        return (lo + span * (0.06 + 0.88 * p[0] / 100), lo + span * (0.04 + 0.88 * p[1] / 100))

    # Limbs in the style's proportion, but never under ~2.4 px at the final size.
    width = max(span * 0.085, 2.4 * SUPERSAMPLE)

    # A small spark where the kick lands, on icons big enough to show it.
    if size >= 128:
        cx, cy = at(KICK)
        for i in range(8):
            a = i * math.tau / 8 + 0.2
            r0, r1 = span * 0.05, span * (0.11 if i % 2 else 0.08)
            p0 = (cx + math.cos(a) * r0, cy + math.sin(a) * r0)
            p1 = (cx + math.cos(a) * r1, cy + math.sin(a) * r1)
            stroke(d, p0, p1, INK, span * 0.03)
            stroke(d, p0, p1, ACCENT, span * 0.018)

    # At 32 px and under the arms only clutter the head, so the silhouette is body, legs and kick.
    bones = BONES if size > 32 else [BONES[0]] + BONES[5:]
    for a, b in bones:
        stroke(d, at(a), at(b), P1, width)
    (hx, hy), hr = HEAD
    cx, cy = at((hx, hy))
    r = span * 0.88 * hr / 100
    r = max(r, width * 0.9)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=P1)

    return image.resize((size, size), Image.LANCZOS)


def stroke(d, a, b, color, width):
    """A round-capped line, like a limb."""
    d.line([a, b], fill=color, width=int(round(width)))
    r = width / 2
    for x, y in (a, b):
        d.ellipse([x - r, y - r, x + r, y + r], fill=color)


def main():
    os.makedirs(OUT, exist_ok=True)
    draw(256).save(os.path.join(OUT, "icon.png"))

    iconset = os.path.join(tempfile.mkdtemp(), "StickFighter.iconset")
    os.makedirs(iconset)
    for points in (16, 32, 128, 256, 512):
        draw(points).save(os.path.join(iconset, f"icon_{points}x{points}.png"))
        draw(points * 2).save(os.path.join(iconset, f"icon_{points}x{points}@2x.png"))
    subprocess.run(["iconutil", "-c", "icns", iconset, "-o", os.path.join(OUT, "StickFighter.icns")], check=True)
    shutil.rmtree(os.path.dirname(iconset))
    print("wrote assets/icon/icon.png and assets/icon/StickFighter.icns")


if __name__ == "__main__":
    main()
