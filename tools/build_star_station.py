"""Rebuild the current v3 room as warmup_station_stars; never modify the baseline.

The pinned in-game export is authoritative for all preserved objects. New decor
is collisionless. The seven SCP models come from their normal owning builders.
Run from any directory: python tools/build_star_station.py
"""
import copy
import hashlib
import importlib
import json
import math
import random
from pathlib import Path

from scp_builder import Builder, save

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'generated/schematics/warmup_station_v3/warmup_station_v3.json'
OUTPUT = ROOT / 'generated/schematics/warmup_station_stars/warmup_station_stars.json'
SOURCE_SHA = 'd823cc990f784f5fccda5f33852815829a571234b4b4e1bcbcb67f5b32db428b'
ROLES = ('049', '079', '096', '106', '173', '939', '3114')
# Bottom-to-top colors sampled visually from the supplied sunset sphere.
GRADIENT = ('405779', '665582', 'A45B80', 'DC626B', 'F78150', 'FFA337')


def gradient(t):
    t = max(0, min(1, t)) * (len(GRADIENT) - 1)
    a = min(int(t), len(GRADIENT) - 2)
    f = t - a
    return '#' + ''.join(f'{round(int(GRADIENT[a][i:i+2],16)*(1-f)+int(GRADIENT[a+1][i:i+2],16)*f):02X}' for i in (0,2,4))


def build():
    raw = SOURCE.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == SOURCE_SHA, 'v3 changed; re-audit preserved IDs before rebuilding'
    source = json.loads(raw.decode('utf-8-sig'))
    blocks = source['Blocks']
    root = source['RootObjectId']
    by_id = {b['ObjectId']: b for b in blocks}

    def root_block(b):
        while b['ParentId'] in by_id:
            b = by_id[b['ParentId']]
        return b

    def protected(b):
        top = root_block(b)
        p = top['Position']
        # Whole creature trees, complete parkour shaft including entry signage,
        # and code-owned aim volume including its boundary and attachments.
        return (top['BlockType'] == 0 or p['z'] >= 17.0 or
                (10 <= p['x'] <= 37 and -10.5 <= p['z'] <= 10.5) or
                b['Name'].startswith('marker_'))

    # Export order is pinned above: frame, backing, full sheared logo and wordmark.
    remove = {b['ObjectId'] for b in blocks[104:321]}
    # The old server/author name is separate from the creature geometry.
    remove.add(-12170)
    new_models = []
    budgets = {}
    for role in ROLES:
        label_index = next(i for i,b in enumerate(blocks) if f'<b>SCP-{role}</b>' in b['Properties'].get('Text',''))
        pedestal_index = max(i for i in range(label_index) if blocks[i]['Scale'] == {'x':1.9,'y':.28,'z':1.5})
        pedestal = blocks[pedestal_index]
        model = importlib.import_module(f'build_scp_{role}_asset').build()
        save(model, f'scp-{role}')
        visible = [b for b in model.blocks if b.get('Properties',{}).get('PrimitiveFlags') == 2]
        old = blocks[pedestal_index+2:label_index]
        assert len(visible) <= len(old) + 5, (role,len(old),len(visible))
        budgets[role] = {'before':len(old),'after':len(visible)}
        remove.update(b['ObjectId'] for b in old)
        # Every current v3 display faces +Z. Preserve each original stand/label.
        assert abs(pedestal['Rotation']['y']) < .001
        for b in visible:
            b = copy.deepcopy(b)
            for axis in 'xyz':
                b['Position'][axis] += pedestal['Position'][axis] if axis != 'y' else .28
            new_models.append(b)

    palette = {'D6DBE0':'202A46', 'E8ECEF':'303B5C', 'F3F6F8':'11192E',
               'EDF1F4':'354263', 'A9B4BF':'766391', '8D9AA7':'596183',
               '12161F':'121A32', '00A896':'EF866C', '1B87C9':'F7AA73',
               'E09612':'FFAC62'}
    kept = []
    for original in blocks:
        if original['ObjectId'] in remove:
            assert not protected(original)
            continue
        b = copy.deepcopy(original)
        if not protected(b):
            props = b['Properties']
            if b['BlockType'] == 2:
                props['Intensity'] = min(props.get('Intensity', 2.5), 2.5)
            color = props.get('Color','')
            if color[:6] in palette:
                props['Color'] = palette[color[:6]] + color[6:]
            if b['BlockType'] == 8:
                props['Text'] = props['Text'].replace('#1B87C9','#F7AA73')
        kept.append(b)

    decor = Builder('stars_decor')
    # A broad seamless sunset disc above the displays, sliced into 32 chords.
    radius, cy = 1.65, 5.30
    for i in range(32):
        y = -radius + (i+.5)*2*radius/32
        width = 2*math.sqrt(max(0,radius*radius-y*y))
        decor.box('sunset_disc', (0,cy+y,-45.68), (width,2*radius/32+.002,.055), gradient(i/31))
    # Thin elliptical orbit, wide enough to read as a planet instead of branding.
    for i in range(36):
        a,b = i*math.tau/36,(i+1)*math.tau/36
        def point(t):
            x,y=3.15*math.cos(t),.68*math.sin(t)
            return (x,cy+y+.20*x,-45.58)
        decor.seg('orbit',point(a),point(b),.038,'#F4BA91')
    # Vertical sunset ribbons make the gradient part of the architecture.
    for side in (-1,1):
        for i in range(12):
            decor.box('gradient_pier',(side*10.72,.45+i*.55,-35.8),(.07,.55,18.4),gradient(i/11))
        # Low aisle rails: purely visual, leave all walking/collision space intact.
        decor.box('aisle_light',(side*2.5,.068,-34.2),(.045,.025,16.5),'#F19D83')
    rng = random.Random(914)
    for i in range(50):
        x,z = rng.uniform(-10.4,10.4),rng.uniform(-45.2,-26.1)
        size = rng.choice((.045,.065,.10,.14))
        decor.box('ceiling_star',(x,7.475,z),(size,.025,size), '#FFE2BF' if i%3 else '#B8C7FF',rot=(0,45,0))
    for i in range(32):
        x,y = rng.uniform(-10.4,10.4),rng.uniform(3.95,7.2)
        if abs(x)<3.4 and abs(y-cy)<1.85:
            continue
        size=rng.choice((.055,.085,.13))
        decor.box('wall_star',(x,y,-45.71),(size,size,.025),'#E5D6FC',rot=(0,0,45))
    # Repeated overhead ribs frame the connector; all above head height.
    for z in (-23.8,-20.9,-18.1):
        decor.box('connector_rib',(0,4.87,z),(8.5,.12,.16),'#A57498')
    # Observation ceiling constellation; the creature underneath is untouched.
    points=[(-20.8,4.93,-5),(-18.2,4.93,-2.2),(-19.4,4.93,1),(-15.9,4.93,3.6),(-13,4.93,1.9)]
    for a,b in zip(points,points[1:]):
        decor.seg('constellation_link',a,b,.025,'#BFA0CD')
    for p in points:
        decor.sphere('constellation_star',p,.14,'#FFD0A0')

    next_id = 200000
    for b in new_models + decor.blocks[1:]:
        b['ObjectId'],b['ParentId']=next_id,root
        next_id+=1
        kept.append(b)
    result = {**source,'Blocks':kept}
    final = {b['ObjectId']:b for b in kept}
    preserved = [b for b in blocks if protected(b)]
    assert all(final[b['ObjectId']] == b for b in preserved)
    assert len(final) == len(kept)
    assert all(b['ParentId']==root or b['ParentId'] in final for b in kept)
    assert not any('860705092' in b['Properties'].get('Text','') or 'DE.XIZHI' in b['Properties'].get('Text','') for b in kept)
    OUTPUT.parent.mkdir(parents=True,exist_ok=True)
    OUTPUT.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    # The C# renderer rejects negative IDs. Remap ONLY this preview copy; the
    # deployable schematic keeps all original opaque IDs and parent relationships.
    preview = copy.deepcopy(result)
    id_map = {root:0, **{b['ObjectId']:i+1 for i,b in enumerate(kept)}}
    preview['RootObjectId'] = 0
    for b in preview['Blocks']:
        b['ObjectId'] = id_map[b['ObjectId']]
        b['ParentId'] = id_map[b['ParentId']]
    preview_path = ROOT / 'generated/previews/star-room.mer.json'
    preview_path.write_text(json.dumps(preview,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    # Front-row review scene; no game transforms are changed by this framing.
    lineup = Builder('collectible_lineup')
    for i, role in enumerate(ROLES):
        model = importlib.import_module(f'build_scp_{role}_asset').build()
        lineup.box('stand',(-i*2.8,-.10,0),(2.35,.20,2.2),'#313A52')
        for original in model.blocks[1:]:
            if original['Properties'].get('PrimitiveFlags') != 2:
                continue
            b = copy.deepcopy(original)
            b['ObjectId'], b['ParentId'] = lineup.next_id, 0
            lineup.next_id += 1
            b['Position']['x'] -= i*2.8
            lineup.blocks.append(b)
    (ROOT / 'generated/previews/collectible-lineup.mer.json').write_text(
        json.dumps(lineup.to_json(),indent=2)+'\n',encoding='utf-8')
    report={'source_sha256':SOURCE_SHA,'before':len(blocks),'after':len(kept),
            'unchanged_protected_blocks':len(preserved),'scp_primitives':budgets,
            'removed_brand_blocks':218,'added_decor':len(decor.blocks)-1}
    OUTPUT.with_name('preservation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__ == '__main__':
    build()
