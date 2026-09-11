"""Remove only the exterior near-white matte from the two Tripo plaque images."""
from collections import deque
from pathlib import Path
from PIL import Image

source = Path(__file__).resolve().parent
destination = source.parents[1] / "Assets/Resources/GuandanUI"
for kind in ("Victory", "Defeat"):
    image = Image.open(source / kind / "01_generated_image_url.png").convert("RGBA")
    width, height = image.size
    pixels = image.load()
    visited = set()
    queue = deque([(x, y) for x in range(width) for y in (0, height-1)] +
                  [(x, y) for y in range(height) for x in (0, width-1)])
    while queue:
        x, y = queue.popleft()
        if (x, y) in visited:
            continue
        visited.add((x, y))
        r, g, b, a = pixels[x, y]
        if min(r, g, b) < 222 or max(r, g, b)-min(r, g, b) > 24:
            continue
        pixels[x, y] = (r, g, b, 0)
        for nx, ny in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
            if 0 <= nx < width and 0 <= ny < height and (nx,ny) not in visited:
                queue.append((nx, ny))
    image.save(destination / f"Result{kind}.png")
    print(kind, image.size, "exterior matte removed; center and ornament preserved")
