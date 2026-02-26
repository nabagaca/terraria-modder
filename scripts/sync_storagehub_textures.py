#!/usr/bin/env python3
"""Sync StorageHub textures from an external source folder.

This script applies the naming map used by StorageHub and pads 2x2 tile textures
from 32x32 to Terraria's expected 36x36 sheet layout (16px frames + 2px padding).
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from PIL import Image


@dataclass(frozen=True)
class TextureMapping:
    source_name: str
    item_target_name: Optional[str] = None
    tile_target_name: Optional[str] = None
    pad_tile_2x2: bool = False


MAPPINGS = [
    TextureMapping(
        source_name="storage-core.png",
        item_target_name="storage-heart.png",
        tile_target_name="storage-heart.png",
        pad_tile_2x2=True,
    ),
    TextureMapping(
        source_name="storage-drive.png",
        item_target_name="storage-unit.png",
        tile_target_name="storage-unit.png",
        pad_tile_2x2=True,
    ),
    TextureMapping(
        source_name="storage-disk-tier-1.png",
        item_target_name="storage-disk.png",
    ),
    TextureMapping(
        source_name="disk-upgrader.png",
        item_target_name="disk-upgrader.png",
        tile_target_name="disk-upgrader.png",
        pad_tile_2x2=True,
    ),
]


def pad_2x2_tile_sheet(image: Image.Image) -> Image.Image:
    """Convert 32x32 (2x2 frames) into 36x36 with 2px frame padding."""
    if image.size == (36, 36):
        return image
    if image.size != (32, 32):
        raise ValueError(f"Expected tile source size 32x32 or 36x36, got {image.size}")

    src = image.convert("RGBA")
    out = Image.new("RGBA", (36, 36), (0, 0, 0, 0))

    # Top-left, top-right, bottom-left, bottom-right 16x16 frames.
    out.paste(src.crop((0, 0, 16, 16)), (0, 0))
    out.paste(src.crop((16, 0, 32, 16)), (18, 0))
    out.paste(src.crop((0, 16, 16, 32)), (0, 18))
    out.paste(src.crop((16, 16, 32, 32)), (18, 18))
    return out


def main() -> int:
    parser = argparse.ArgumentParser(description="Sync StorageHub textures and pad tiles.")
    parser.add_argument(
        "--source-dir",
        default=r"C:\Users\Aiden\OneDrive\Documents\Terraia Mod Work\Storage",
        help="Folder containing source PNG textures.",
    )
    parser.add_argument(
        "--assets-root",
        default="src/StorageHub/assets",
        help="StorageHub assets root containing items/ and tiles/.",
    )
    args = parser.parse_args()

    source_dir = Path(args.source_dir)
    assets_root = Path(args.assets_root)
    items_dir = assets_root / "items"
    tiles_dir = assets_root / "tiles"

    if not source_dir.exists():
        raise FileNotFoundError(f"Source dir does not exist: {source_dir}")

    items_dir.mkdir(parents=True, exist_ok=True)
    tiles_dir.mkdir(parents=True, exist_ok=True)

    copied = 0
    skipped = 0

    for mapping in MAPPINGS:
        src_path = source_dir / mapping.source_name
        if not src_path.exists():
            print(f"SKIP missing source: {src_path}")
            skipped += 1
            continue

        src_image = Image.open(src_path).convert("RGBA")

        if mapping.item_target_name:
            dst_item = items_dir / mapping.item_target_name
            src_image.save(dst_item)
            print(f"ITEM  {mapping.source_name} -> {dst_item}")
            copied += 1

        if mapping.tile_target_name:
            dst_tile = tiles_dir / mapping.tile_target_name
            tile_image = pad_2x2_tile_sheet(src_image) if mapping.pad_tile_2x2 else src_image
            tile_image.save(dst_tile)
            print(f"TILE  {mapping.source_name} -> {dst_tile} size={tile_image.size}")
            copied += 1

    print(f"Done. Copied {copied} outputs, skipped {skipped} missing sources.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
