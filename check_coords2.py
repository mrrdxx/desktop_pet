from PIL import Image
import math
img = Image.open('assets/pets/default/idle_0.png').convert('RGBA')
px = img.load()

# Check (70, 12)
print(f"(70, 12): alpha={px[70, 12][3]}")
print(f"(70, 14): alpha={px[70, 14][3]}")
print(f"(69, 13): alpha={px[69, 13][3]}")
print(f"(68, 13): alpha={px[68, 13][3]}")

# Last position in test is (70, 20) after 5 moves (60+5*2=70)
# Move to (70, 12): dist = sqrt(0 + 64) = 8.0
dist = math.sqrt((70-70)**2 + (12-20)**2)
print(f"\nDistance from (70,20) to (70,12): {dist}")
print(f"PettingMaxMovePerFramePx = 8.0, check is >, so {dist} > 8.0 = {dist > 8.0}")

# Check y=12 row
opaque_y12 = [(x, px[x, 12][3]) for x in range(120) if px[x, 12][3] > 0]
print(f"\nOpaque at y=12: {len(opaque_y12)} pixels")
opaque_y13 = [(x, px[x, 13][3]) for x in range(120) if px[x, 13][3] > 0]
print(f"Opaque at y=13: {len(opaque_y13)} pixels")
if opaque_y13:
    print(f"  x range: [{opaque_y13[0][0]}, {opaque_y13[-1][0]}]")
