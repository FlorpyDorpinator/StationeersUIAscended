"""Turn a shot-server burst (uia-shots/<name>/NNNN.raw + manifest.txt) into a GIF and an MP4.

The in-game shot server's `burst=` step writes raw RGB24 frames (bottom-up rows, the
ReadPixels layout) plus a manifest: line 1 = "<w> <h>", then "<frame> <seconds>" per frame.
Frames arrive at an uneven rate, so this resamples them onto a steady clock by timestamp.

usage:
    python tools/uia-gif.py BURST_DIR OUT_BASE [--fps 20] [--width 640] [--mp4-width 1280]
                            [--start S] [--end S]

Writes OUT_BASE.gif (looping, adaptive palette) and OUT_BASE.mp4 (H.264, needs the
imageio-ffmpeg package: pip install imageio-ffmpeg). Requires: pip install numpy pillow
"""
import argparse
import os
import subprocess
import sys

import numpy as np
from PIL import Image


def load(burst_dir):
    with open(os.path.join(burst_dir, "manifest.txt")) as f:
        lines = [l.split() for l in f.read().splitlines() if l.strip()]
    w, h = int(lines[0][0]), int(lines[0][1])
    frames = [(int(a), float(b)) for a, b in lines[1:]]
    return w, h, frames


def frame_image(burst_dir, idx, w, h):
    raw = np.fromfile(os.path.join(burst_dir, "%04d.raw" % idx), dtype=np.uint8)
    return Image.fromarray(raw.reshape(h, w, 3)[::-1])   # ReadPixels rows are bottom-up


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("burst_dir")
    ap.add_argument("out_base")
    ap.add_argument("--fps", type=float, default=20.0)
    ap.add_argument("--width", type=int, default=640)
    ap.add_argument("--mp4-width", type=int, default=1280)
    ap.add_argument("--start", type=float, default=0.0)
    ap.add_argument("--end", type=float, default=-1.0)
    ap.add_argument("--crop", default="", help="x,y,w,h inside the burst frame (top-left origin)")
    ap.add_argument("--colors", type=int, default=192)
    ap.add_argument("--bayer", type=int, default=4, help="1 (coarse) .. 5 (fine) ordered dither")
    ap.add_argument("--dither", default="bayer",
                    help="bayer (small files, stable backgrounds) or sierra2_4a (smooth gradients, bigger)")
    a = ap.parse_args()

    fw, fh, frames = load(a.burst_dir)
    if not frames:
        sys.exit("no frames")
    box = tuple(int(v) for v in a.crop.split(",")) if a.crop else (0, 0, fw, fh)
    w, h = box[2], box[3]

    def grab(idx):
        im = frame_image(a.burst_dir, idx, fw, fh)
        return im if not a.crop else im.crop((box[0], box[1], box[0] + w, box[1] + h))
    end = frames[-1][1] if a.end < 0 else a.end
    times = np.arange(a.start, end, 1.0 / a.fps)
    stamps = np.array([t for _, t in frames])
    picks = [frames[int(np.argmin(np.abs(stamps - t)))][0] for t in times]

    gif_frames = []
    mp4_path = a.out_base + ".mp4"
    ffmpeg = None
    try:
        import imageio_ffmpeg
        ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    except ImportError:
        print("imageio-ffmpeg not installed: skipping MP4")
    proc = None
    mw = min(a.mp4_width, w) // 2 * 2
    mh = int(round(h * mw / w)) // 2 * 2
    if ffmpeg:
        proc = subprocess.Popen(
            [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24",
             "-s", "%dx%d" % (mw, mh), "-r", str(a.fps), "-i", "-", "-c:v", "libx264",
             "-pix_fmt", "yuv420p", "-crf", "18", "-preset", "slow", "-movflags", "+faststart", mp4_path],
            stdin=subprocess.PIPE)

    gw = a.width // 2 * 2
    gh = int(round(h * gw / w)) // 2 * 2
    gif_path = a.out_base + ".gif"
    gproc = None
    if ffmpeg:
        # ffmpeg's palette pipeline: ORDERED (bayer) dithering is stable frame to frame and
        # diff_mode=rectangle re-encodes only what changed, so a still background compresses
        # to almost nothing (error-diffusion dithering re-noised it every frame: 22 MB GIFs).
        gproc = subprocess.Popen(
            [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24",
             "-s", "%dx%d" % (gw, gh), "-r", str(a.fps), "-i", "-",
             "-vf", "split[a][b];[a]palettegen=max_colors=%d:stats_mode=diff[p];"
                    "[b][p]paletteuse=dither=%s:diff_mode=rectangle" % (a.colors,
                        "bayer:bayer_scale=%d" % a.bayer if a.dither == "bayer" else a.dither),
             "-loop", "0", gif_path],
            stdin=subprocess.PIPE)
    for idx in picks:
        im = grab(idx)
        if proc:
            proc.stdin.write(im.resize((mw, mh), Image.LANCZOS).tobytes())
        g = im.resize((gw, gh), Image.LANCZOS)
        if gproc:
            gproc.stdin.write(g.tobytes())
        else:
            gif_frames.append(g)
    if proc:
        proc.stdin.close()
        proc.wait()
        print("wrote", mp4_path)
    if gproc:
        gproc.stdin.close()
        gproc.wait()
        print("wrote", gif_path, "(%d frames, %.1f MB)" % (len(picks), os.path.getsize(gif_path) / 1e6))
        return

    # One shared adaptive palette from a sample of frames keeps the glow ramps stable
    # between frames (per-frame palettes shimmer on gradients).
    sample = Image.new("RGB", (gw, gh * 4))
    for i, k in enumerate(np.linspace(0, len(gif_frames) - 1, 4).astype(int)):
        sample.paste(gif_frames[k], (0, gh * i))
    pal = sample.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    q = [f.quantize(palette=pal, dither=Image.Dither.FLOYDSTEINBERG) for f in gif_frames]
    q[0].save(gif_path, save_all=True, append_images=q[1:], loop=0,
              duration=int(round(1000.0 / a.fps)), optimize=False, disposal=1)
    print("wrote", gif_path, "(%d frames)" % len(q))


if __name__ == "__main__":
    main()
