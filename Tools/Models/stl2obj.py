# Converts the moving-cart STL assembly (extract STLFiles.rar into ./cart first) to Assets/Resources/Models/RollCart.obj.
# Usage: python stl2obj.py  (writes out/RollCart.obj; copy it into Assets/Resources/Models/).
import struct,glob,os
S=0.8/1000.0  # along the cart and up; across it stays full size (see the header)
groups={'Frame':['Carts Structure'],'Rollers':['Standard Free Roller'],'Casters':['C-CTH'],'Fittings':['Angle Plate','ST Plate','Joint Support','Pulling']}
def group(f):
    for g,keys in groups.items():
        if any(k in f for k in keys): return g
out=['# Roll cart: user-supplied "Roller Transfer Carts Trolley" STL assembly, merged, in metres: 0.8 along and up, full width across (so a moving box fits between the rails and it still fits in a lift car with you).',
     '# Local frame: +y up (casters on y=0), +z forward (the tow bar), push end at z=0, centred on x.']
vbase=1
tris={g:[] for g in groups}
for f in sorted(glob.glob('cart/STL Files/*.STL')):
    d=open(f,'rb').read(); n=struct.unpack('<I',d[80:84])[0]
    g=group(f)
    for i in range(n):
        o=84+50*i+12
        t=[]
        for k in range(3):
            X,Y,Z=struct.unpack('<3f',d[o+12*k:o+12*k+12])
            # STL mm: x along the cart (tow bar at low x), y up, z across. Local: x across, y up, z forward.
            lx=(Z-439.0)/1000.0; ly=(Y-2.0)*S; lz=(2087.0-X)*S
            # Unity negates OBJ x on import (right- to left-handed): write -x so it lands back on +x.
            t.append((round(-lx,5),round(ly,5),round(lz,5)))
        tris[g].append(t)
for g,ts in tris.items():
    idx={}; verts=[]; faces=[]
    for t in ts:
        f=[]
        for v in t:
            if v not in idx: idx[v]=len(verts); verts.append(v)
            f.append(idx[v])
        if len(set(f))==3: faces.append(f)
    out.append('o '+g)
    out+=['v %g %g %g'%v for v in verts]
    out+=['f %d %d %d'%(a+vbase,c+vbase,b+vbase) for a,b,c in faces]  # reversed: see the note on x
    vbase+=len(verts)
    print(g,len(verts),'verts',len(faces),'tris')
os.makedirs('out',exist_ok=True)
open('out/RollCart.obj','w').write('\n'.join(out)+'\n')
print(os.path.getsize('out/RollCart.obj'))
