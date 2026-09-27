"""Downloads CC0 models from Poly Haven (polyhaven.com) into Assets/Art/ThirdParty/PolyHaven/<id>/.

Each model comes as its 1k FBX plus the textures the FBX references that the game uses (diffuse, and alpha where the
model has one); roughness/metal/AO maps are skipped (the game's materials don't read them). Already-present models
are left alone. Usage: python Tools/polyhaven_get.py id [id ...]
Prints what it fetched and the total size, for CREDITS (all Poly Haven assets are CC0, no attribution required).
"""
import json
import os
import sys
import urllib.request

ROOT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Art', 'ThirdParty', 'PolyHaven')
KEEP = ('_diff_', '_alpha_')  # normal maps (EXR, ~1 MB each) add little on small props


def get(url):
    req = urllib.request.Request(url, headers={'User-Agent': 'OpeningBell-assets/1.0'})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def fetch(model_id):
    folder = os.path.join(ROOT, model_id)
    fbx_path = os.path.join(folder, model_id + '.fbx')
    if os.path.exists(fbx_path):
        return 0, 'present'
    files = json.loads(get('https://api.polyhaven.com/files/' + model_id))
    fbx = files['fbx']['1k']['fbx']
    total = 0
    os.makedirs(os.path.join(folder, 'textures'), exist_ok=True)
    for rel, info in fbx.get('include', {}).items():
        if not any(k in rel for k in KEEP):
            continue
        data = get(info['url'])
        with open(os.path.join(folder, rel), 'wb') as f:
            f.write(data)
        total += len(data)
    data = get(fbx['url'])
    with open(fbx_path, 'wb') as f:
        f.write(data)
    total += len(data)
    return total, 'ok'


if __name__ == '__main__':
    grand = 0
    for mid in sys.argv[1:]:
        try:
            size, state = fetch(mid)
        except Exception as e:  # keep going: one missing model shouldn't stop the batch
            size, state = 0, 'FAILED ' + str(e)
        grand += size
        print(f'{mid:40s} {state:10s} {size / 1e6:6.2f} MB')
    print(f'total {grand / 1e6:.1f} MB')
