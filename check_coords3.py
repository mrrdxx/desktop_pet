from PIL import Image
img = Image.open('assets/pets/default/idle_0.png').convert('RGBA')
px = img.load()
# Check opaque range at y=30
opaque_y30 = [(x, px[x, 30][3]) for x in range(120) if px[x, 30][3] > 0]
print(f"Opaque at y=30: {len(opaque_y30)} pixels, x range [{opaque_y30[0][0]}, {opaque_y30[-1][0]}]")

# Check opaque range at y=25
opaque_y25 = [(x, px[x, 25][3]) for x in range(120) if px[x, 25][3] > 0]
print(f"Opaque at y=25: {len(opaque_y25)} pixels, x range [{opaque_y25[0][0]}, {opaque_y25[-1][0]}]")

# Check opaque range at y=35
opaque_y35 = [(x, px[x, 35][3]) for x in range(120) if px[x, 35][3] > 0]
print(f"Opaque at y=35: {len(opaque_y35)} pixels, x range [{opaque_y35[0][0]}, {opaque_y35[-1][0]}]")

# For 25 steps of 1 DIP from x=60: 60..85, need opaque up to 85
# y=30 range is [38, 82] — 85 is still outside
# Try y=35: range [38, 82] — same
# Better: use 1 DIP steps, 17 steps from 60 to 77 (within [43,77] at y=20)
# 17 * 50 = 850ms — not enough for 1000ms threshold
# Use 1 DIP steps, 25 steps from 55 to 80 — 80 is transparent at y=20
# Use y=30, 1 DIP steps from 60 to 82 — 82 is the last opaque at y=30
# 22 steps * 50ms = 1100ms > 1000ms — works!
print("\nPlan: y=30, 1 DIP steps, 22 steps from x=60 to x=82")
print(f"  Check (82, 30): alpha={px[82, 30][3]}")
print(f"  Check (81, 30): alpha={px[81, 30][3]}")
