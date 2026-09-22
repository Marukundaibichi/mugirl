"""Normalize fully transparent texture pixels to black without altering alpha.

FA lid cover images are shader masks, so their RGB remains meaningful even where
alpha is zero. Those files are deliberately excluded from normalization.

python docs/tools/Normalize-TransparentPixels.py          # Read-only scan
python docs/tools/Normalize-TransparentPixels.py --apply  # Back up, fix, verify

Requires Pillow and numpy. Backups and reports are stored in DevData/Backups.
"""
import argparse
import hashlib
import io
import json
import os
import shutil
import struct
from collections import Counter
from datetime import datetime
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
DIRECTORIES = (
    "Textures", "1.6/FacialAnimation/Textures",
    "Bio_1.6/Textures", "Odyssey_1.6/Textures",
    "Versions/1.6/Integrations/VCookE/Textures",
    "Versions/1.6/Integrations/SearchAndDestroy/Textures",
)


def preserves_transparent_rgb(relative):
    """Return whether transparent RGB is shader data rather than disposable color."""
    parts = relative.parts
    return (parts[:5] == ("1.6", "FacialAnimation", "Textures", "FA", "Lids")
            and "_cover_" in relative.stem)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def chunks(data):
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("Invalid PNG signature")
    result, offset = [], 8
    while offset + 12 <= len(data):
        length = struct.unpack_from(">I", data, offset)[0]
        end = offset + 12 + length
        if end > len(data):
            raise ValueError("Truncated PNG chunk")
        kind = data[offset + 4:offset + 8]
        result.append((kind, data[offset:end]))
        offset = end
        if kind == b"IEND":
            return result, data[offset:]
    raise ValueError("Missing PNG end marker")


def load(data):
    with Image.open(io.BytesIO(data)) as image:
        image.load()
        if image.mode not in ("RGB", "RGBA") or getattr(image, "n_frames", 1) != 1:
            raise ValueError(f"Unexpected PNG mode or animation: {image.mode}")
        return image.mode, np.array(image.convert("RGBA"))


def replace_pixels(original, pixels):
    output = io.BytesIO()
    Image.fromarray(pixels).save(output, format="PNG", optimize=True)
    old_chunks, trailer = chunks(original)
    new_chunks, _ = chunks(output.getvalue())
    new_header = next(raw for kind, raw in new_chunks if kind == b"IHDR")
    new_image = b"".join(raw for kind, raw in new_chunks if kind == b"IDAT")
    parts, inserted = [original[:8]], False
    for kind, raw in old_chunks:
        if kind == b"IHDR":
            parts.append(new_header)
        elif kind == b"IDAT":
            if not inserted:
                parts.append(new_image)
                inserted = True
        else:
            parts.append(raw)
    result = b"".join(parts) + trailer
    kept_chunks, kept_trailer = chunks(result)
    ancillary = lambda entries: [(kind, raw) for kind, raw in entries
                                  if kind not in (b"IHDR", b"IDAT")]
    if ancillary(old_chunks) != ancillary(kept_chunks) or trailer != kept_trailer:
        raise ValueError("PNG metadata changed")
    return result


def main(apply=False):
    work = ROOT / "DevData" / "Backups" / ("TransparentBlack-" + datetime.now().strftime("%Y%m%d-%H%M%S-%f"))
    if apply:
        work.mkdir(parents=True, exist_ok=False)
    files = sorted(path for directory in DIRECTORIES
                   for path in (ROOT / directory).rglob("*")
                   if path.is_file() and path.suffix.lower() == ".png"
                   and "备份" not in path.parts)
    rows, groups = [], Counter()
    total_pixels, changed_files = 0, 0
    protected_files, protected_pixels = 0, 0
    for index, path in enumerate(files, 1):
        if not path.resolve().is_relative_to(ROOT):
            raise ValueError(f"Texture outside mod workspace: {path}")
        relative = path.relative_to(ROOT)
        original = path.read_bytes()
        original_mode, pixels = load(original)
        transparent = pixels[:, :, 3] == 0
        nonblack_transparent = int(np.count_nonzero(
            transparent & np.any(pixels[:, :, :3] != 0, axis=2)))
        protected = preserves_transparent_rgb(relative)
        count = 0 if protected else nonblack_transparent
        if protected:
            protected_files += 1
            protected_pixels += nonblack_transparent
        row = {"path": relative.as_posix(), "before_sha256": digest(original),
               "after_sha256": digest(original), "changed_pixels": count,
               "preserves_transparent_rgb": protected}
        if count and apply:
            expected = pixels.copy()
            expected[transparent, :3] = 0
            result = replace_pixels(original, expected)
            result_mode, decoded = load(result)
            if result_mode != original_mode or not np.array_equal(decoded, expected):
                raise ValueError(f"Pixel round-trip mismatch: {relative}")
            if not np.array_equal(decoded[:, :, 3], pixels[:, :, 3]):
                raise ValueError(f"Alpha changed: {relative}")
            if not np.array_equal(decoded[~transparent], pixels[~transparent]):
                raise ValueError(f"Visible or partially transparent pixel changed: {relative}")
            backup = work / "OriginalTextures" / relative
            staged = work / "StagedTextures" / relative
            backup.parent.mkdir(parents=True, exist_ok=True)
            staged.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, backup)
            if digest(backup.read_bytes()) != row["before_sha256"]:
                raise ValueError(f"Backup mismatch: {relative}")
            staged.write_bytes(result)
            row["after_sha256"] = digest(result)
        if count:
            changed_files += 1
            total_pixels += count
            group = next(directory for directory in DIRECTORIES
                         if relative.as_posix().startswith(directory + "/"))
            groups[group] += 1
        rows.append(row)
        if index % 200 == 0:
            print(json.dumps({"scanned": index, "total": len(files),
                              "changed_files": changed_files}), flush=True)
    manifest = {"scanned_files": len(files), "changed_files": changed_files,
                "changed_pixels": total_pixels, "groups": dict(groups), "files": rows}
    if not apply:
        print(json.dumps({"files": len(files), "files_needing_change": changed_files,
                          "pixels_needing_change": total_pixels,
                          "protected_shader_mask_files": protected_files,
                          "protected_nonblack_transparent_pixels": protected_pixels,
                          "groups": dict(groups)},
                         ensure_ascii=False, indent=2))
        return
    (work / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False),
                                        encoding="utf-8")
    for row in rows:
        path = ROOT / row["path"]
        if digest(path.read_bytes()) != row["before_sha256"]:
            raise ValueError(f"Source changed during preparation: {path}")
        if row["changed_pixels"]:
            staged = work / "StagedTextures" / row["path"]
            if digest(staged.read_bytes()) != row["after_sha256"]:
                raise ValueError(f"Staged texture changed: {staged}")
    print(json.dumps({"applying_files": changed_files, "pixels": total_pixels}), flush=True)
    for row in rows:
        if row["changed_pixels"]:
            os.replace(work / "StagedTextures" / row["path"], ROOT / row["path"])
    for row in rows:
        data = (ROOT / row["path"]).read_bytes()
        if digest(data) != row["after_sha256"]:
            raise ValueError(f"Final file hash mismatch: {row['path']}")
        _, pixels = load(data)
        if (not preserves_transparent_rgb(Path(row["path"]))
                and np.any(pixels[pixels[:, :, 3] == 0, :3] != 0)):
            raise ValueError(f"Nonblack transparent pixels remain: {row['path']}")
    report = {key: value for key, value in manifest.items() if key != "files"}
    report.update({"verified_files": len(rows), "remaining_nonblack_transparent_pixels": 0,
                   "protected_shader_mask_files": protected_files,
                   "protected_nonblack_transparent_pixels": protected_pixels,
                   "alpha_and_visible_pixels_preserved": True,
                   "backup_directory": str(work / "OriginalTextures")})
    (work / "verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Back up, modify, and verify textures")
    main(parser.parse_args().apply)
