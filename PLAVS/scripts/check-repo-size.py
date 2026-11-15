#!/usr/bin/env python3
"""Check the source upload, including folders extracted without Git metadata."""
import argparse
import os
from pathlib import Path
import sys

sys.dont_write_bytecode = True
from _common import ROOT as root, is_local_file, source_files

parser = argparse.ArgumentParser(description='Перевірити комплект для GitHub: до 100 файлів, кожен до 20 MiB.')
parser.add_argument('--physical', action='store_true', help='Перевірити всі фізичні файли, а не лише комплект вихідного коду.')
args = parser.parse_args()
paths = source_files()
if args.physical:
    paths = []
    for directory, children, names in os.walk(root):
        children[:] = [name for name in children if name != '.git']
        paths.extend((Path(directory)/name).relative_to(root) for name in names)
files = [(p, (root/p).stat().st_size) for p in paths if (root/p).is_file()]
bad = []
for path, size in files:
    if size > 20*1024*1024:
        bad.append(f'Завеликий файл: {path} ({size/1024/1024:.2f} MiB)')
    if is_local_file(path) or (root/path).is_symlink():
        bad.append(f'Локальний або непереносний файл у комплекті: {path}')
if len(files) > 100:
    bad.append(f'Файлів {len(files)}: браузер GitHub приймає до 100 за одне завантаження.')
if not files:
    bad.append('У комплекті немає файлів проєкту.')
print(f'Файлів: {len(files)}; разом: {sum(s for _,s in files)/1024/1024:.2f} MiB')
if files:
    largest = max(files, key=lambda f: f[1])
    print(f'Найбільший: {largest[0]} ({largest[1]/1024:.1f} KiB)')
for message in bad:
    print(message)
sys.exit(bool(bad))
