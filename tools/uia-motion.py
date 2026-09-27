"""Motion map for UIA screenshot bursts: which pixels change across N shots of one screen.

Stills cannot show shader animation (the edge-light sweep, halo breath, ...). Capture a burst
with the in-game shot server — the first shot stages the screen, the rest use the tab value
`same` so every frame shares one state — then difference them here:

    world=latest
    waitms=1500
    shot=m-a.png:SmartStow
    shot=m-b.png:same
    shot=m-c.png:same

usage:
    python tools/uia-motion.py OUT.png SHOT_A.png SHOT_B.png [SHOT_C.png ...] [--crop x0,y0,x1,y1]

OUT.png = frame A dimmed to grey with every pixel whose luminance range across the frames
exceeds 3 levels painted magenta (brightness = range x6). Prints the moving-pixel count and a
per-80px-band summary. If frames differ wholesale (the player clicked mid-burst) the map is
meaningless: check the "whole-frame mean diff" line first — a static screen reads well under 1.
Requires: pip install numpy pillow
"""
import sys
import numpy as np
from PIL import Image


def main(argv):
    crop = None
    args = []
    i = 0
    while i < len(argv):
        if argv[i] == "--crop":
            crop = tuple(int(v) for v in argv[i + 1].split(","))
            i += 2
            continue
        args.append(argv[i])
        i += 1
    if len(args) < 3:
        print(__doc__)
        return 2
    out, paths = args[0], args[1:]
    frames = [np.asarray(Image.open(p).convert("RGB")).astype(np.float32) for p in paths]
    lum = np.stack([0.3 * f[..., 0] + 0.59 * f[..., 1] + 0.11 * f[..., 2] for f in frames])
    rng = lum.max(0) - lum.min(0)
    print("whole-frame mean diff: %.2f" % rng.mean())

    x0, y0 = 0, 0
    y1, x1 = rng.shape
    if crop:
        x0, y0, x1, y1 = crop
    base = frames[0][y0:y1, x0:x1]
    r = rng[y0:y1, x0:x1]
    img = np.repeat(base.mean(-1, keepdims=True) * 0.35, 3, -1)
    mask = r > 3.0
    hot = np.clip(r * 6.0, 0, 255)
    img[mask, 0] = np.maximum(img[mask, 0], hot[mask])
    img[mask, 2] = np.maximum(img[mask, 2], hot[mask])
    img[mask, 1] *= 0.3
    Image.fromarray(img.astype(np.uint8)).save(out)

    print("moving px (range>3): %d of %d" % (int(mask.sum()), mask.size))
    for b in range(0, r.shape[0], 80):
        band = mask[b:b + 80]
        cols = np.where(band.any(0))[0]
        span = "%d..%d" % (cols.min() + x0, cols.max() + x0) if cols.size else "-"
        print("y %4d-%4d  moving=%7d  x=%s" % (b + y0, min(b + 80, r.shape[0]) + y0, int(band.sum()), span))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
