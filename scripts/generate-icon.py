"""Package the official logo into Windows standard icon sizes."""
from pathlib import Path
from PIL import Image
root = Path(__file__).resolve().parents[1]
source = root / 'assets' / 'deskmux-logo.png'
destination = root / 'src' / 'DeskMux.App' / 'Assets' / 'DeskMux.ico'
with Image.open(source) as image:
    image.convert('RGBA').resize((256,256), Image.Resampling.LANCZOS).save(destination, format='ICO', sizes=[(s,s) for s in (16,20,24,32,40,48,64,128,256)])
print(destination)
