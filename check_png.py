from PIL import Image
import os

path = 'assets/pets/default/idle_0.png'
if not os.path.exists(path):
    print('File not found:', path)
    exit()

img = Image.open(path).convert('RGBA')
px = img.load()
w, h = img.size
print(f'PNG size: {w}x{h}')

# Check test coordinate (60, 20)
print(f'Pixel (60,20): {px[60, 20]}')

# Find opaque bounds
min_x, min_y, max_x, max_y = w, h, -1, -1
for y in range(h):
    for x in range(w):
        if px[x, y][3] > 0:
            min_x = min(min_x, x)
            min_y = min(min_y, y)
            max_x = max(max_x, x)
            max_y = max(max_y, y)
print(f'Opaque bounds: ({min_x},{min_y}) to ({max_x},{max_y})')

# Check what's in the upper 1/3 (y < 40)
print('\nOpaque pixels in upper 1/3 (y < 40):')
for y in range(0, 40, 2):
    row = [(x, px[x, y]) for x in range(w) if px[x, y][3] > 0]
    if row:
        print(f'  y={y}: {len(row)} pixels, x range [{row[0][0]}, {row[-1][0]}]')

# Specifically check y=20
opaque_at_y20 = [(x, px[x, 20]) for x in range(w) if px[x, 20][3] > 0]
print(f'\nOpaque pixels at y=20: {len(opaque_at_y20)}')
if opaque_at_y20:
    print(f'  x range: [{opaque_at_y20[0][0]}, {opaque_at_y20[-1][0]}]')

# Check a transparent point in the head zone
print(f'\nPixel (60, 5): {px[60, 5]}')
print(f'Pixel (60, 10): {px[60, 10]}')
print(f'Pixel (60, 15): {px[60, 15]}')
print(f'Pixel (60, 20): {px[60, 20]}')
print(f'Pixel (60, 25): {px[60, 25]}')
print(f'Pixel (60, 30): {px[60, 30]}')
print(f'Pixel (60, 35): {px[60, 35]}')
