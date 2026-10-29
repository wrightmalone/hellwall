"""Pack five generated 6x5 sheets into eight facings and five animation clips.

Python dependencies: Pillow, numpy. No procedural pose warping: source poses
are generated artwork; this script crops, aligns, mirrors and resamples only.
Rows in the runtime atlas are clip * 8 + sim-facing, columns are six frames.
"""
import json
import hashlib
from collections import deque
from pathlib import Path
import numpy as np
from PIL import Image, ImageOps, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / 'game/art/bear'
OUT = ROOT / 'out/bear-animation'
OUT.mkdir(parents=True, exist_ok=True)
CELL, FRAMES = 96, 6
CLIPS = ['idle', 'walk', 'attack', 'hit', 'death']
# Same angles as Art.Facing: +x SE, +x+y S, +y SW, -x+y W, etc.
DIRECTIONS = [('sw', True), ('s', False), ('sw', False), ('w', False),
              ('nw', False), ('n', False), ('nw', True), ('w', True)]

def clean_cell(image):
    """Remove disconnected fragments of neighboring poses at generated grid boundaries."""
    pixels = np.array(image)
    mask = pixels[:, :, 3] > 32
    remaining = mask.copy()
    best = []
    height, width = mask.shape
    for y,x in zip(*np.nonzero(mask)):
        if not remaining[y,x]: continue
        pending = deque([(int(y),int(x))]); remaining[y,x] = False; component = []
        while pending:
            cy,cx = pending.popleft(); component.append((cy,cx))
            for ny in range(max(0,cy-1),min(height,cy+2)):
                for nx in range(max(0,cx-1),min(width,cx+2)):
                    if remaining[ny,nx]: remaining[ny,nx] = False; pending.append((ny,nx))
        if len(component)>len(best): best=component
    kept=np.zeros_like(mask)
    for y,x in best: kept[y,x]=True
    # Preserve antialiasing surrounding the connected character.
    for _ in range(2):
        padded=np.pad(kept,1)
        kept=np.logical_or.reduce([padded[y:y+height,x:x+width] for y in range(3) for x in range(3)])
    pixels[~kept,3]=0
    return Image.fromarray(pixels)

def splits(alpha, n, axis):
    sums = (alpha > 32).sum(axis=axis)
    length = len(sums)
    edges = [0]
    for i in range(1, n):
        nominal = length * i / n
        lo, hi = int(nominal-length/n*.11), int(nominal+length/n*.11)
        segment = sums[lo:hi]
        candidates = np.where(segment == segment.min())[0] + lo
        edges.append(int(min(candidates, key=lambda x: abs(x-nominal))))
    return edges + [length]

frames = {}
report = {}
for direction in ['sw','s','w','nw','n']:
    sheet = Image.open(FOLDER / 'source' / f'{direction}.png').convert('RGBA')
    alpha = np.asarray(sheet)[:, :, 3]
    xs, ys = splits(alpha, 6, 0), splits(alpha, 5, 1)
    cells = [[clean_cell(sheet.crop((xs[c], ys[r], xs[c+1], ys[r+1]))) for c in range(6)] for r in range(5)]
    bounds = [[im.getchannel('A').point(lambda x: 255 if x > 24 else 0).getbbox() for im in row] for row in cells]
    assert all(b is not None for row in bounds for b in row), direction
    # Estimate neutral proportions and use ONE scale for every frame of a direction.
    idle_h = float(np.median([b[3]-b[1] for b in bounds[0]]))
    neutral_x = float(np.median([(b[0]+b[2])/2 for b in bounds[0]]))
    neutral_bottom = float(np.median([b[3] for b in bounds[0]]))
    max_width = max(b[2]-b[0] for row in bounds for b in row)
    max_above = max(neutral_bottom-b[1] for row in bounds for b in row)
    scale = min(69 / idle_h, 88 / max_width, 74 / max_above)
    packed = []
    for r,row in enumerate(cells):
        packed_row = []
        for c,im in enumerate(row):
            b = bounds[r][c]
            crop = im.crop(b)
            w,h = max(1,round(crop.width*scale)), max(1,round(crop.height*scale))
            crop = crop.resize((w,h), Image.Resampling.LANCZOS)
            x = round(48 + (b[0]-neutral_x)*scale)
            y = round(76 + (b[1]-neutral_bottom)*scale)
            cell = Image.new('RGBA',(CELL,CELL))
            assert x >= 0 and y >= 0 and x+w <= CELL and y+h <= CELL, (direction,r,c,(x,y,w,h))
            cell.alpha_composite(crop,(x,y))
            packed_row.append(cell)
        packed.append(packed_row)
    frames[direction] = packed
    report[direction] = {'source_size':sheet.size,'x_splits':xs,'y_splits':ys,'scale':scale,'neutral_height':idle_h}

atlas = Image.new('RGBA',(FRAMES*CELL,len(CLIPS)*8*CELL))
for facing,(source,mirrored) in enumerate(DIRECTIONS):
    for clip in range(len(CLIPS)):
        for frame in range(FRAMES):
            im = frames[source][clip][frame]
            if mirrored: im = ImageOps.mirror(im)
            atlas.alpha_composite(im,(frame*CELL,(clip*8+facing)*CELL))
atlas.save(FOLDER/'bear-animations.png')
# Catch empty, clipped or accidentally repeated frames before importing the atlas.
for clip in range(len(CLIPS)):
    for facing in range(8):
        row=clip*8+facing
        cells=[atlas.crop((f*CELL,row*CELL,(f+1)*CELL,(row+1)*CELL)) for f in range(FRAMES)]
        for cell in cells:
            b=cell.getchannel('A').getbbox()
            assert b and 0<b[0]<b[2]<CELL and 0<b[1]<b[3]<CELL,(clip,facing,b)
        assert len({hashlib.sha256(im.tobytes()).digest() for im in cells})==FRAMES,(clip,facing,'duplicate frames')
# Easy-to-review sheet, all directions at idle frame zero.
font_path = '/System/Library/Fonts/Supplemental/Arial.ttf'
font = ImageFont.truetype(font_path,18)
board = Image.new('RGBA',(960,310),(31,38,33,255)); draw=ImageDraw.Draw(board)
draw.text((20,15),'EIGHT DIRECTIONS / five drawn views, three mirrored',font=font,fill='#e5dcc6')
for d,label in enumerate(['SE','S','SW','W','NW','N','NE','E']):
    im=atlas.crop((0,d*CELL,CELL,(d+1)*CELL)).resize((192,192),Image.Resampling.NEAREST)
    # 4x2 layout generated separately below, preview strip stays true to 96 px.
    im=atlas.crop((0,d*CELL,CELL,(d+1)*CELL)).resize((112,112),Image.Resampling.NEAREST)
    board.alpha_composite(im,(d*118+7,90));draw.text((d*118+50,220),label,font=font,fill='#ded5bc')
board.convert('RGB').save(OUT/'eight-directions.png')
# GIF uses actual runtime atlas cells, including the mirrored facings.
for clip_idx,clip in enumerate(CLIPS):
    gif=[]
    count=12 if clip=='death' else 6
    for f in range(count):
        cf=min(f,5)
        canvas=Image.new('RGBA',(960,310),(31,38,33,255));dr=ImageDraw.Draw(canvas)
        dr.text((20,15),f'{clip.upper()} / eight game directions / actual 96 px source cells enlarged',font=font,fill='#e5dcc6')
        for d,label in enumerate(['SE','S','SW','W','NW','N','NE','E']):
            row=clip_idx*8+d
            im=atlas.crop((cf*CELL,row*CELL,(cf+1)*CELL,(row+1)*CELL)).resize((112,112),Image.Resampling.NEAREST)
            canvas.alpha_composite(im,(d*118+7,90));dr.text((d*118+50,220),label,font=font,fill='#ded5bc')
        gif.append(canvas.convert('RGB'))
    duration={'idle':200,'walk':140,'attack':150,'hit':65,'death':180}[clip]
    gif[0].save(OUT/f'{clip}.gif',save_all=True,append_images=gif[1:],duration=duration,loop=0,disposal=2)
metadata={'cell':CELL,'frames':FRAMES,'directions':8,'clips':CLIPS,'feet':[48,76],
          'direction_sources':[{'source':s,'mirrored':m} for s,m in DIRECTIONS], 'alignment':report}
(FOLDER/'animations.json').write_text(json.dumps(metadata,indent=2)+'\n')
print(f'Packed {len(CLIPS)*8*FRAMES} cells: {atlas.size}; 150 drawn source poses, 90 mirrored cells')
