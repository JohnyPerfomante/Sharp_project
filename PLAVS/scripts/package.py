#!/usr/bin/env python3
"""Create a small source-only archive suitable for a new GitHub repository."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile

sys.dont_write_bytecode = True
from _common import ROOT as root, source_files

parser = argparse.ArgumentParser(description='Створити чистий комплект GitHub без моделі, збірок і секретів.')
parser.add_argument('--output', type=Path, default=root.parent/'PLAVS-GitHub.zip')
parser.add_argument('--directory', type=Path, help='Також створити чисту папку для завантаження (має ще не існувати).')
args = parser.parse_args()
destination = args.directory.expanduser().resolve() if args.directory else None
if destination and (destination == root or destination.is_relative_to(root) or destination.exists()):
    parser.error('Чиста папка має бути новою й розміщеною поза поточним проєктом.')
subprocess.run([sys.executable, str(root/'scripts/check-repo-size.py')], cwd=root, check=True)
paths = source_files()
output = args.output.expanduser().resolve()
output.parent.mkdir(parents=True, exist_ok=True)
with tempfile.NamedTemporaryFile(dir=output.parent, suffix='.zip', delete=False) as stream:
    temporary = Path(stream.name)
try:
    with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in paths:
            archive.write(root/path, 'PLAVS/'+path.as_posix())
    temporary.replace(output)
finally:
    temporary.unlink(missing_ok=True)
if destination:
    destination.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix='plavs-export-', dir=destination.parent))
    try:
        for path in paths:
            target = staging/path
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(root/path, target)
        staging.rename(destination)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    print(f'Папка для GitHub: {destination}')
print(f'Архів: {output} ({output.stat().st_size/1024/1024:.2f} MiB)')
print('У репозиторій завантажуйте вміст чистої папки, а не сам ZIP.')
