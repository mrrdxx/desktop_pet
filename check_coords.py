from PIL import Image
img = Image.open('assets/pets/default/idle_0.png').convert('RGBA')
px = img.load()
coords = [(0,20),(120,20),(60,20),(61,20),(62,20),(64,20),(66,20),(70,20),(80,20),(130,20),(200,20),(-5,20),(60,-10),(-1,0),(10000,20),(70,80),(60,5),(60,10)]
for x,y in coords:
    if 0<=x<120 and 0<=y<120:
        a = px[x,y][3]
        label = "OPAQUE" if a>0 else "TRANSPARENT"
        print(f"({x},{y}): alpha={a} {label}")
    else:
        print(f"({x},{y}): OUT OF BOUNDS")
