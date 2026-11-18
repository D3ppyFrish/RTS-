#!/usr/bin/env python3
"""Turn a textured GLB (Meshy AI exports) into a coloured voxel model for game/models/*.vox.gz.

Usage: python3 scripts/import-model.py MODEL.glb HEIGHT OUT.vox.gz [COLOURS]
HEIGHT is in sculpt units (a thirty-second of a block; an orc is about 62). COLOURS (default 96, at most 250)
is how many colours the texture is cut down to. Needs numpy and Pillow. The format is read by
game/scripts/Detail/Sculpted.Imported.cs. The knight: scripts/import-model.py knight.glb 64 game/models/knight.vox.gz
"""
import numpy as np, sys, json, struct, io, gzip
from collections import deque
from PIL import Image

BRIGHT_GAMMA, BRIGHT_GAIN = 0.7, 1.08
glb, height, out = sys.argv[1], int(sys.argv[2]), sys.argv[3]
K = int(sys.argv[4]) if len(sys.argv) > 4 else 96
d = open(glb, 'rb').read()
jl = struct.unpack('<I', d[12:16])[0]; j = json.loads(d[20:20+jl])
bo = 20 + jl; bl = struct.unpack('<I', d[bo:bo+4])[0]; buf = d[bo+8:bo+8+bl]
def acc(i):
    a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]
    n = {'SCALAR':1,'VEC2':2,'VEC3':3}[a['type']]; dt = {5125:np.uint32,5126:np.float32,5123:np.uint16}[a['componentType']]
    off = bv.get('byteOffset',0) + a.get('byteOffset',0)
    return np.frombuffer(buf, dt, a['count']*n, off).reshape(-1, n) if n > 1 else np.frombuffer(buf, dt, a['count'], off)
p = j['meshes'][0]['primitives'][0]
t = acc(p['indices']).reshape(-1, 3); v = acc(p['attributes']['POSITION']).astype(np.float64); uv = acc(p['attributes']['TEXCOORD_0']).astype(np.float64)
bv = j['bufferViews'][j['images'][0]['bufferView']]
img = np.asarray(Image.open(io.BytesIO(buf[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']])).convert('RGB'))
H, W = img.shape[:2]; print('tex', W, H, 'tris', len(t))
v = v - v.min(0); s = (height - 1) / v[:,1].max(); v = v * s
dims = np.ceil(v.max(0)).astype(int) + 3
g = np.zeros(dims, bool); col = np.zeros(tuple(dims) + (3,), np.uint8); best = np.full(dims, 9.0)
for i in range(len(t)):
    a, b, c = v[t[i]]; ua, ub, uc = uv[t[i]]
    L = max(np.linalg.norm(b-a), np.linalg.norm(c-a), np.linalg.norm(c-b))
    n = int(L * 4) + 3
    u, w = np.meshgrid(np.linspace(0,1,n), np.linspace(0,1,n)); m = u + w <= 1; u = u[m]; w = w[m]
    pts = a + (b-a)*u[:,None] + (c-a)*w[:,None]; tc = ua + (ub-ua)*u[:,None] + (uc-ua)*w[:,None]
    q = np.floor(pts).astype(int) + 1
    dist = np.linalg.norm(pts + 1 - (q + 0.5), axis=1)
    px = np.clip((tc[:,0] * W).astype(int), 0, W-1); py = np.clip((tc[:,1] * H).astype(int), 0, H-1)
    cs = img[py, px]
    for k in range(len(q)):
        x, y, z = q[k]
        g[x,y,z] = True
        if dist[k] < best[x,y,z]: best[x,y,z] = dist[k]; col[x,y,z] = cs[k]
X, Y, Z = g.shape
outside = np.zeros_like(g); dq = deque([(0,0,0)]); outside[0,0,0] = True
N6 = ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1))
while dq:
    x, y, z = dq.popleft()
    for dx, dy, dz in N6:
        a_, b_, c_ = x+dx, y+dy, z+dz
        if 0 <= a_ < X and 0 <= b_ < Y and 0 <= c_ < Z and not outside[a_,b_,c_] and not g[a_,b_,c_]:
            outside[a_,b_,c_] = True; dq.append((a_,b_,c_))
solid = ~outside
# Interior takes the colour of the nearest surface voxel (BFS).
done = g.copy(); dq = deque(zip(*np.nonzero(g)))
while dq:
    x, y, z = dq.popleft()
    for dx, dy, dz in N6:
        a_, b_, c_ = x+dx, y+dy, z+dz
        if 0 <= a_ < X and 0 <= b_ < Y and 0 <= c_ < Z and solid[a_,b_,c_] and not done[a_,b_,c_]:
            done[a_,b_,c_] = True; col[a_,b_,c_] = col[x,y,z]; dq.append((a_,b_,c_))
# Quantize to at most K colours (k-means on the solid voxels' colours).
cs = col[solid].astype(np.float64)
uniq = np.unique(cs, axis=0)
if len(uniq) <= K: cent = uniq
else:
    rng = np.random.default_rng(1); cent = cs[rng.choice(len(cs), K, replace=False)]
    for it in range(25):
        lab = np.argmin(((cs[:,None,:] - cent[None]) ** 2).sum(2), 1)
        for k in range(K):
            m = lab == k
            if m.any(): cent[k] = cs[m].mean(0)
lab = np.argmin(((cs[:,None,:] - cent[None]) ** 2).sum(2), 1)
cells = np.zeros(g.shape, np.uint8); cells[solid] = lab + 1
pal = np.clip(np.round(cent), 0, 255).astype(np.uint8)
# Brighten: the texture has its shadows painted in and the game lights it again, so as made it looks too dark.
pal = np.clip(255 * (pal / 255.0) ** BRIGHT_GAMMA * BRIGHT_GAIN, 0, 255).astype(np.uint8)
print('dims', g.shape, 'solid', solid.sum(), 'colours', len(pal))
with gzip.open(out, 'wb') as f:
    f.write(b'VOXC' + struct.pack('<4i', X, Y, Z, len(pal)) + pal.tobytes() + cells.tobytes(order='F'))
