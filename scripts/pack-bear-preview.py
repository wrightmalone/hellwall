"""Pack the static bear art test into Hellwall's existing 6 x 8 enemy atlas.

Run with Python + Pillow from the repo root. This intentionally repeats one
pose in every cell. It does not claim to supply eight facings or a walk cycle.
"""
from pathlib import Path
from PIL import Image

folder = Path('game/art/preview/bear')
source = Image.open(folder / 'bear-source.png').convert('RGBA')
# Ignore near-transparent fringe when computing bounds, preserve original alpha.
bounds = source.getchannel('A').point(lambda a: 255 if a > 16 else 0).getbbox()
source = source.crop(bounds)
source.thumbnail((66, 53), Image.Resampling.LANCZOS)
cell = Image.new('RGBA', (72, 72))
# Same ground anchor as Art.Feet: feet at approximately y=57.
cell.alpha_composite(source, ((72-source.width)//2, 57-source.height))
cell.save(folder / 'bear-cell.png')
sheet = Image.new('RGBA', (432, 576))
for row in range(8):
    for frame in range(6):
        sheet.alpha_composite(cell, (frame*72, row*72))
sheet.save(folder / 'bear-static-sheet.png')
print(f'Bear content {source.width} x {source.height}; atlas {sheet.size}; single static facing repeated 48 times')
