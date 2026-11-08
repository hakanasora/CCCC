import csv
import json
import pathlib
import struct
import sys


def count_rim(path, theme):
    data = path.read_bytes()
    if data[:2] != b'BM':
        raise ValueError('Expected a BMP screen capture')
    offset = struct.unpack_from('<I', data, 10)[0]
    width, height = struct.unpack_from('<ii', data, 18)
    bits = struct.unpack_from('<H', data, 28)[0]
    if bits != 32 or width != 424 or abs(height) != 144:
        raise ValueError('Expected a 424 by 144, 32-bit capture at 96 DPI')
    pixels = data[offset:offset + width * abs(height) * 4]
    if len(pixels) != width * abs(height) * 4:
        raise ValueError('Truncated screen capture')
    row = abs(height) - 1 - 100 if height > 0 else 100
    b, g, r, a = struct.unpack_from('BBBB', pixels, (row * width + 290) * 4)
    tinted = (75 <= r <= 110 and 20 <= g <= 50 and 60 <= b <= 100) if theme == 'dark' else (
        240 <= r <= 255 and 185 <= g <= 225 and 215 <= b <= 250)
    if not tinted:
        raise ValueError('INCONCLUSIVE: expected tinted marker is absent from the card interior')
    return sum(1 for b, g, r, a in struct.iter_unpack('BBBB', pixels)
               if r >= 250 and g <= 4 and abs(b - 180) <= 4)


def main():
    directory = pathlib.Path(sys.argv[1])
    metadata = dict(line.split('=', 1) for line in (directory / 'run.txt').read_text().splitlines() if '=' in line)
    if metadata.get('mode') != 'color' or metadata.get('material') != 'True' or metadata.get('dpi') != '96':
        raise ValueError('INCONCLUSIVE: requires an active material marker run at 96 DPI')
    rows = list(csv.DictReader((directory / 'frames.csv').open()))
    valid = [r for r in rows if 400 < float(r['capture_start_ms']) < 2700
             and float(r['opacity']) >= .999 and float(r['scale']) > 1.005]
    if len(valid) < 10 or not any(float(r['scale']) > 1.05 for r in valid):
        raise ValueError('INCONCLUSIVE: insufficient captured pulse activity')
    if any(r.get('native_active') != '1' or r.get('marker_active') != '1' for r in valid):
        raise ValueError('INCONCLUSIVE: material or marker was unavailable during the pulse')
    counts = [(count_rim(directory / r['file'], metadata['theme']), r['file']) for r in valid]
    peak, filename = max(counts)
    result = dict(captured_pulse_frames=len(valid), peak_exposed_magenta_pixels=peak,
                  threshold_pixels=120, decisive_frame=filename, passed=peak <= 120)
    print(json.dumps(result))
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
