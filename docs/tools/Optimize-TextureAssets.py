"""Prepare, apply, or verify the approved 555 -> 512 texture conversion.

Run without arguments to back up originals and produce review images in DevData/Backups.
--apply changes only the files recorded in the prepared manifest.
--verify also understands the FA conditional texture directory.
Requires Pillow and numpy. Original backups are never overwritten.
"""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / "DevData" / "Backups" / "PerformanceOptimization-20260907"
MANIFEST = WORK / "textures.json"
SIZE = (512, 512)


def safe(path):
    path = path.resolve()
    if not path.is_relative_to(ROOT):
        raise ValueError(f"Path outside mod workspace: {path}")
    return path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def live_path(relative):
    path = safe(ROOT / relative)
    if not path.exists() and relative.startswith("Textures/FA/"):
        path = safe(ROOT / "1.6/FacialAnimation" / relative)
    return path


def centroid(im):
    alpha = np.asarray(im.getchannel("A"), dtype=np.float64)
    total = alpha.sum()
    if total == 0:
        return None
    x = np.dot(alpha.sum(axis=0), np.arange(im.width) + 0.5) / total / im.width
    y = np.dot(alpha.sum(axis=1), np.arange(im.height) + 0.5) / total / im.height
    return np.array([x, y])


def prepare():
    if MANIFEST.exists():
        raise RuntimeError(f"Backup already exists; use --apply or --verify: {MANIFEST}")
    rows = []
    for directory in (ROOT / "Textures",):
        for path in sorted(directory.rglob("*.png")):
            with Image.open(path) as source:
                if source.size != (555, 555):
                    continue
                relative = path.relative_to(ROOT).as_posix()
                backup = safe(WORK / "OriginalTextures" / relative)
                staged = safe(WORK / "ResizedTextures" / relative)
                if backup.exists():
                    raise RuntimeError(f"Refusing to overwrite original backup: {backup}")
                backup.parent.mkdir(parents=True, exist_ok=True)
                staged.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, backup)
                original = source.convert("RGBA")
                # Premultiplied alpha avoids filtering hidden RGB into transparent edges.
                resized = original.convert("RGBa").resize(SIZE, Image.Resampling.LANCZOS).convert("RGBA")
                resized.save(staged, optimize=True)
                before, after = centroid(original), centroid(resized)
                if (before is None) != (after is None):
                    raise RuntimeError(f"Texture transparency changed: {relative}")
                drift = 0.0 if before is None else float(np.max(np.abs(before - after)))
                if drift > 1 / 512:
                    raise RuntimeError(f"Alpha alignment changed excessively: {relative}, {drift}")
                rows.append({"path": relative, "original_sha256": digest(path),
                             "resized_sha256": digest(staged), "alpha_centroid_drift": drift})
    if not rows:
        raise RuntimeError("No 555x555 source textures found")
    WORK.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps({"from": [555, 555], "to": list(SIZE), "textures": rows},
                                   indent=2, ensure_ascii=False), encoding="utf-8")
    make_review(rows)
    print(json.dumps({"prepared": len(rows), "max_normalized_alpha_drift": max(r["alpha_centroid_drift"] for r in rows),
                      "backup": str(WORK / "OriginalTextures"), "review": str(WORK / "texture-comparison.png")}, ensure_ascii=False))


def checker(size):
    im = Image.new("RGBA", size)
    draw = ImageDraw.Draw(im)
    for y in range(0, size[1], 16):
        for x in range(0, size[0], 16):
            draw.rectangle((x, y, x + 15, y + 15), fill=(190, 190, 190) if (x // 16 + y // 16) % 2 else (230, 230, 230))
    return im


def make_review(rows):
    # Show samples from distinct layers; every sample uses the same full-image UV transform.
    selected = []
    for category in ("Heads_Blank", "Eyes", "Lids", "Brows", "Mouth", "Emotions", "Hair", "Apparel"):
        matches = [r for r in rows if category.lower() in r["path"].lower()]
        if matches:
            selected.append(next((r for r in matches if "/Normal/" in r["path"] and "normal_" in r["path"]), matches[0]))
    canvas = Image.new("RGB", (768, 282 * len(selected)), "white")
    draw = ImageDraw.Draw(canvas)
    for index, row in enumerate(selected):
        for col, folder in enumerate(("OriginalTextures", "ResizedTextures")):
            with Image.open(WORK / folder / row["path"]) as im:
                preview = im.convert("RGBA").resize((256, 256), Image.Resampling.LANCZOS)
                tile = checker((256, 256))
                tile.alpha_composite(preview)
                canvas.paste(tile.convert("RGB"), (col * 384, index * 282 + 24))
        draw.text((8, index * 282 + 5), "BEFORE 555: " + Path(row["path"]).name, fill="black")
        draw.text((392, index * 282 + 5), "AFTER 512: " + Path(row["path"]).name, fill="black")
    canvas.save(WORK / "texture-comparison.png")
    make_face_review()


def make_face_review():
    # Composite the same full-canvas layers on both sides; crop only the QA preview.
    canvas = Image.new("RGB", (768, 282 * 7), "white")
    draw = ImageDraw.Draw(canvas)
    layers = (("Heads_Blank", "normal"), ("Lids", "normal_bottom"), ("Eyes", "normal"),
              ("Eyes", "normal_highlight"), ("Mouth", "normal"), ("Brows", "normal"), ("Lids", "normal_cover"))
    for index in range(7):
        variant = "Normal" + (str(index + 1) if index else "")
        for col, folder in enumerate(("OriginalTextures", "ResizedTextures")):
            combined = Image.new("RGBA", SIZE)
            for category, shape in layers:
                relative = f"Textures/FA/{category}/{variant}/Female/{shape}_south.png"
                file = WORK / folder / relative
                if not file.exists():
                    file = live_path(relative)
                if file.exists():
                    with Image.open(file) as im:
                        layer = im.convert("RGBA").convert("RGBa").resize(SIZE, Image.Resampling.LANCZOS).convert("RGBA")
                        combined.alpha_composite(layer)
            tile = checker((256, 256))
            tile.alpha_composite(combined.crop((128, 128, 384, 384)))
            canvas.paste(tile.convert("RGB"), (col * 384, index * 282 + 24))
        draw.text((8, index * 282 + 5), "BEFORE " + variant, fill="black")
        draw.text((392, index * 282 + 5), "AFTER " + variant, fill="black")
    canvas.save(WORK / "face-layer-comparison.png")


def apply():
    rows = json.loads(MANIFEST.read_text(encoding="utf-8"))["textures"]
    # Check the entire batch before changing any source files.
    for row in rows:
        for file, expected in ((WORK / "OriginalTextures" / row["path"], row["original_sha256"]),
                               (WORK / "ResizedTextures" / row["path"], row["resized_sha256"]),
                               (live_path(row["path"]), row["original_sha256"])):
            if digest(file) != expected:
                raise RuntimeError(f"Source or backup changed: {file}")
    for row in rows:
        shutil.copy2(WORK / "ResizedTextures" / row["path"], live_path(row["path"]))
    verify()


def verify():
    rows = json.loads(MANIFEST.read_text(encoding="utf-8"))["textures"]
    for row in rows:
        original = safe(WORK / "OriginalTextures" / row["path"])
        current = live_path(row["path"])
        if digest(original) != row["original_sha256"] or digest(current) != row["resized_sha256"]:
            raise RuntimeError(f"Texture/backup hash mismatch: {row['path']}")
        with Image.open(current) as im:
            if im.size != SIZE or im.mode != "RGBA":
                raise RuntimeError(f"Unexpected texture format: {current}")
    print(json.dumps({"verified": len(rows), "backup": str(WORK / "OriginalTextures")}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--apply", action="store_true")
    group.add_argument("--verify", action="store_true")
    group.add_argument("--review", action="store_true", help="Regenerate before/after review images from backups")
    args = parser.parse_args()
    if args.apply:
        apply()
    elif args.verify:
        verify()
    elif args.review:
        make_review(json.loads(MANIFEST.read_text(encoding="utf-8"))["textures"])
    else:
        prepare()
