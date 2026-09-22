"""验证雪牛娘脸部差分并修复成人身体断颈贴图。

``--apply`` 会先把原图备份到 DevData/Backups，再执行确定性的像素修复；默认只验证。
需要 Pillow 与 numpy。
"""

import argparse
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[2]
BACKUP_ROOT = ROOT / "DevData" / "Backups" / "AppearanceTextureRepair-20260921" / "OriginalTextures"
BODY_LIMITS = {
    "south": (230, 284, 154, 188),
    "north": (230, 284, 154, 188),
    "east": (238, 291, 150, 181),
}
FACE_PATH = Path("Textures/Mugirl/BodyAccessories/Mugirl_FaceAccessory5_south.png")
HEAD_TYPES = "ABCDEFGHIJKLMN"
QA_PATH = ROOT / "DevData" / "Backups" / "AppearanceTextureRepair-20260921" / "appearance-before-after.png"


def safe(path):
    path = path.resolve()
    if not path.is_relative_to(ROOT):
        raise ValueError(f"Path outside mod workspace: {path}")
    return path


def body_path(direction):
    return Path(f"Textures/Mugirl/Bodies/Naked_Female_{direction}.png")


def load_rgba(path):
    with Image.open(path) as image:
        if image.size != (512, 512):
            raise RuntimeError(f"Unexpected texture size: {path} = {image.size}")
        return np.array(image.convert("RGBA"))


def save_rgba(path, pixels):
    temporary = path.with_suffix(".repairing.png")
    Image.fromarray(pixels, "RGBA").save(temporary, optimize=True)
    temporary.replace(path)


def cap_masks(pixels, limits):
    x0, x1, y0, y1 = limits
    alpha = pixels[:, :, 3]
    rgb = pixels[:, :, :3]
    yy, xx = np.indices(alpha.shape)
    roi = (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)
    red_cut = (
        roi
        & (alpha > 0)
        & (rgb[:, :, 0] > 120)
        & (rgb[:, :, 0] > rgb[:, :, 1] * 1.45)
        & (rgb[:, :, 1] < 150)
    )
    white_bone = roi & (alpha > 0) & (rgb.min(axis=2) > 205)
    return roi, red_cut, white_bone


def repair_body(source, limits):
    pixels = source.copy()
    alpha = pixels[:, :, 3]
    rgb = pixels[:, :, :3]
    x0, x1, y0, y1 = limits
    yy, xx = np.indices(alpha.shape)
    roi, red_cut, white_bone = cap_masks(pixels, limits)

    # 仅重绘颈部内部；随后用原图深色像素重建两像素外描边。
    solid = Image.fromarray(np.where(alpha > 0, 255, 0).astype(np.uint8), "L")
    interior = np.array(solid.filter(ImageFilter.MinFilter(5))) == 255
    target = roi & interior
    outline = roi & (alpha > 0) & ~interior
    donor = (
        (xx >= x0 - 8)
        & (xx < x1 + 8)
        & (yy >= y1)
        & (yy < y1 + 45)
        & (alpha >= 192)
        & (rgb.min(axis=2) > 80)
        & (rgb.max(axis=2) < 253)
    )

    target_coords = np.column_stack(np.where(target))
    donor_coords = np.column_stack(np.where(donor))
    donor_colors = rgb[donor].astype(np.float64)
    if not len(target_coords) or not len(donor_coords):
        raise RuntimeError("Neck repair mask or donor area is empty")

    # 从颈部下方邻近皮肤做反距离加权插值，避免生成式工具改变身体其他区域。
    repaired_colors = np.empty((len(target_coords), 3), dtype=np.float64)
    for start in range(0, len(target_coords), 128):
        query = target_coords[start : start + 128]
        distance_squared = ((query[:, None, :] - donor_coords[None, :, :]) ** 2).sum(axis=2)
        nearest_count = min(16, len(donor_coords))
        nearest = np.argpartition(distance_squared, nearest_count - 1, axis=1)[:, :nearest_count]
        nearest_distances = np.take_along_axis(distance_squared, nearest, axis=1)
        weights = 1.0 / (nearest_distances + 16.0)
        repaired_colors[start : start + len(query)] = (
            (donor_colors[nearest] * weights[:, :, None]).sum(axis=1)
            / weights.sum(axis=1)[:, None]
        )

    pixels[:, :, :3][target] = np.clip(np.rint(repaired_colors), 0, 255).astype(np.uint8)
    dark_pixels = roi & (alpha > 0) & (rgb.max(axis=2) < 90)
    if not dark_pixels.any():
        raise RuntimeError("Neck outline donor area is empty")
    outline_color = np.median(rgb[dark_pixels], axis=0).astype(np.uint8)
    pixels[:, :, :3][outline] = outline_color
    pixels[:, :, :3][alpha == 0] = 0
    if not np.array_equal(pixels[:, :, 3], source[:, :, 3]):
        raise RuntimeError("Body repair changed the alpha silhouette")
    return pixels, int(target.sum())


def face_safe_mask(inset=2):
    masks = []
    for head_type in HEAD_TYPES:
        path = ROOT / f"Textures/Mugirl/Heads/Female/Mugirl_Female_{head_type}_south.png"
        masks.append(load_rgba(path)[:, :, 3])
    common = np.minimum.reduce(masks)
    binary = Image.fromarray(np.where(common > 0, 255, 0).astype(np.uint8), "L")
    # 只把该轮廓用于选择和验证缩放比例，不拿它裁切差分图形。
    return np.array(binary.filter(ImageFilter.MinFilter(inset * 2 + 1))) == 255


def visible_bbox(pixels):
    yy, xx = np.where(pixels[:, :, 3] > 0)
    if not len(xx):
        return None
    return [int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1]


def backup(relative):
    source = safe(ROOT / relative)
    destination = safe(BACKUP_ROOT / relative)
    if not destination.exists():
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
    return destination


def verify():
    report = {"body": {}, "face": {}}
    for direction, limits in BODY_LIMITS.items():
        path = ROOT / body_path(direction)
        pixels = load_rgba(path)
        _, red_cut, white_bone = cap_masks(pixels, limits)
        x0, x1, y0, y1 = limits
        alpha = pixels[:, :, 3]
        yy, xx = np.indices(alpha.shape)
        roi = (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)
        solid = Image.fromarray(np.where(alpha > 0, 255, 0).astype(np.uint8), "L")
        interior = np.array(solid.filter(ImageFilter.MinFilter(5))) == 255
        outline = roi & (alpha > 0) & ~interior
        bright_outline = outline & (pixels[:, :, :3].max(axis=2) >= 90)
        report["body"][direction] = {
            "red_cut_pixels": int(red_cut.sum()),
            "white_bone_pixels": int(white_bone.sum()),
            "bright_outline_pixels": int(bright_outline.sum()),
        }
        if red_cut.any() or white_bone.any() or bright_outline.any():
            raise RuntimeError(f"Neck repair extends over the dark outline: {path}")

    face = load_rgba(ROOT / FACE_PATH)
    alpha = face[:, :, 3]
    outside_silhouette = (alpha > 0) & ~face_safe_mask(0)
    outside_inner_outline = (alpha > 0) & ~face_safe_mask(2)
    strong_outside = (alpha >= 64) & ~face_safe_mask(0)
    face_report = {
        "outside_common_silhouette_pixels": int(outside_silhouette.sum()),
        "outside_common_silhouette_alpha_ge_64": int(strong_outside.sum()),
        "outside_2px_inner_margin_pixels": int(outside_inner_outline.sum()),
        "visible_pixels": int((alpha > 0).sum()),
        "bbox": visible_bbox(face),
    }
    if strong_outside.any():
        raise RuntimeError(f"Opaque face accessory pixels extend beyond the common head silhouette: {FACE_PATH}")
    report["face"] = face_report
    return report


def apply():
    # 已修复的检出版本再次运行时保持字节不变。
    try:
        report = verify()
        print(json.dumps({"already_repaired": True, **report}, ensure_ascii=False))
        return
    except RuntimeError:
        pass

    changed = {}
    for direction, limits in BODY_LIMITS.items():
        relative = body_path(direction)
        source = load_rgba(backup(relative))
        repaired, count = repair_body(source, limits)
        save_rgba(ROOT / relative, repaired)
        changed[relative.as_posix()] = count

    report = verify()
    print(
        json.dumps(
            {"repaired": changed, "backup": str(BACKUP_ROOT), "verification": report},
            ensure_ascii=False,
        )
    )


def checkerboard():
    image = Image.new("RGBA", (512, 512), (48, 48, 48, 255))
    draw = ImageDraw.Draw(image)
    tile = 32
    for y in range(0, 512, tile):
        for x in range(0, 512, tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle((x, y, x + tile - 1, y + tile - 1), fill=(75, 75, 75, 255))
    return image


def qa_panel(layers, crop):
    image = checkerboard()
    for layer in layers:
        image.alpha_composite(Image.fromarray(layer, "RGBA"))
    return image.crop(crop).resize((396, 300), Image.Resampling.NEAREST)


def write_qa():
    original_face = load_rgba(BACKUP_ROOT / FACE_PATH)
    current_face = load_rgba(ROOT / FACE_PATH)
    head = load_rgba(ROOT / "Textures/Mugirl/Heads/Female/Mugirl_Female_N_south.png")
    original_body = load_rgba(BACKUP_ROOT / body_path("east"))
    current_body = load_rgba(ROOT / body_path("east"))

    sheet = Image.new("RGB", (828, 760), (24, 24, 24))
    panels = [
        ("FACE BEFORE", qa_panel([head, original_face], (137, 150, 375, 330))),
        ("FACE AFTER - USER-SUPPLIED REPLACEMENT", qa_panel([head, current_face], (137, 150, 375, 330))),
        ("BODY BEFORE", qa_panel([original_body], (150, 135, 348, 285))),
        ("BODY AFTER - OUTLINE RESTORED", qa_panel([current_body], (150, 135, 348, 285))),
    ]
    draw = ImageDraw.Draw(sheet)
    for index, (label, panel) in enumerate(panels):
        column = index % 2
        row = index // 2
        x = 12 + column * 408
        y = 28 + row * 336
        draw.text((x, y - 20), label, fill=(230, 230, 230))
        sheet.paste(panel.convert("RGB"), (x, y))
    QA_PATH.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(QA_PATH, optimize=True)
    print(QA_PATH)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="备份并修复三张身体运行时贴图")
    parser.add_argument("--qa", action="store_true", help="生成修复前后对照图")
    args = parser.parse_args()
    if args.apply:
        apply()
    elif args.qa:
        write_qa()
    else:
        print(json.dumps(verify(), ensure_ascii=False))
