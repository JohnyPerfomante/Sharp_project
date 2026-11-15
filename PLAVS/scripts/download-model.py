#!/usr/bin/env python3
"""Download pinned local inference assets. Python is used only for setup, never to grade answers."""
import hashlib
import json
import argparse
import sys
import urllib.request

sys.dont_write_bytecode = True
from _common import ROOT, model_directory

parser = argparse.ArgumentParser(description='Завантажити й перевірити модель у локальному кеші поза проєктом.')
parser.add_argument('--print-directory', action='store_true', help='Лише вивести шлях до моделі.')
args = parser.parse_args()
target = model_directory()
if args.print_directory:
    print(target)
    raise SystemExit(0)
manifest = json.loads((ROOT / 'models/manifest.json').read_text(encoding='utf-8'))
target.mkdir(parents=True, exist_ok=True)

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()

for asset in manifest['files']:
    path = target / asset['destination']
    if path.exists() and path.stat().st_size == asset['size'] and digest(path) == asset['sha256']:
        print(f'Перевірено: {path.name}', flush=True)
        continue
    temporary = path.with_suffix(path.suffix + '.part')
    url = f"https://huggingface.co/{manifest['repository']}/resolve/{manifest['revision']}/{asset['source']}"
    print(f"Завантаження {path.name}: {asset['size'] / 1_000_000:.1f} MB", flush=True)
    with urllib.request.urlopen(url, timeout=120) as response, temporary.open('wb') as stream:
        while chunk := response.read(1024 * 1024):
            stream.write(chunk)
    if temporary.stat().st_size != asset['size'] or digest(temporary) != asset['sha256']:
        temporary.unlink(missing_ok=True)
        raise SystemExit(f'Неправильна контрольна сума: {path.name}')
    temporary.replace(path)
    print(f'Збережено й перевірено: {path.name}', flush=True)
print('Модель готова. Перезапустіть API, якщо воно вже працює.')
