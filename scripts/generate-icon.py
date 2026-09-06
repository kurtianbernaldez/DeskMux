from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
destination = root / "src" / "DeskMux.App" / "Assets" / "DeskMux.ico"
destination.parent.mkdir(parents=True, exist_ok=True)
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = []
for size in sizes:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    pad = max(1, round(size * 0.06))
    radius = max(2, round(size * 0.20))
    draw.rounded_rectangle((pad, pad, size - pad - 1, size - pad - 1), radius=radius, fill="#18d1bc")
    stroke = max(1, round(size * 0.065))
    inset = round(size * 0.22)
    split_x = round(size * 0.58)
    split_y = round(size * 0.54)
    ink = "#07171d"
    draw.rounded_rectangle((inset, inset, size - inset - 1, size - inset - 1), radius=max(1, round(size * 0.035)), outline=ink, width=stroke)
    draw.line((split_x, inset, split_x, size - inset), fill=ink, width=stroke)
    draw.line((split_x, split_y, size - inset, split_y), fill=ink, width=stroke)
    images.append(image)

images[-1].save(destination, format="ICO", append_images=images[:-1], sizes=[(size, size) for size in sizes])
print(destination)
