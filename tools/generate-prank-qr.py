"""Development only: pip install qrcode==8.2 zxing-cpp==2.3.0 Pillow.

Run from any directory. Build/runtime only consume the committed resources.
"""
from pathlib import Path
from importlib.metadata import version
import qrcode
import zxingcpp
from PIL import Image

target = "https://github.com/hakanasora/CCCC"
output = Path(__file__).resolve().parents[1] / "assets" / "prank"
output.mkdir(parents=True, exist_ok=True)
assert version("qrcode") == "8.2"
qr = qrcode.QRCode(error_correction=qrcode.constants.ERROR_CORRECT_M, box_size=10, border=4)
qr.add_data(target, optimize=0)
qr.make(fit=True)
image = qr.make_image(fill_color="black", back_color="white").convert("RGB")
matrix = qr.get_matrix()
image.save(output / "repository-qr.png")
(output / "repository-qr.txt").write_text("\n".join("".join("1" if cell else "0" for cell in row) for row in matrix) + "\n", encoding="ascii")
decoded = zxingcpp.read_barcode(Image.open(output / "repository-qr.png"))
assert decoded is not None and decoded.text == target
assert all(not value for row in matrix[:4] + matrix[-4:] for value in row)
assert all(not any(row[:4] + row[-4:]) for row in matrix)
print(f"PASS: qrcode {version('qrcode')}, zxing-cpp {version('zxing-cpp')}; {len(matrix)} modules including four-module quiet zone; {decoded.text}")
