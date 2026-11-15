#!/usr/bin/env python3
"""Build and start Docker with model import via stdin, without host-directory file sharing."""
import hashlib
import json
import subprocess
import sys
import tarfile

sys.dont_write_bytecode = True
from _common import ROOT as root, local_environment, model_directory

# A fresh GitHub clone needs neither a committed .env nor committed model files.
subprocess.run([sys.executable, str(root/'scripts/setup.py')], cwd=root, check=True)
subprocess.run([sys.executable, str(root/'scripts/download-model.py')], cwd=root, check=True)
env = local_environment()
manifest = json.loads((root / 'models/manifest.json').read_text(encoding='utf-8'))
assets = []
for asset in manifest['files']:
    path = model_directory(env) / asset['destination']
    if not path.is_file():
        raise SystemExit('Спочатку виконайте python3 scripts/download-model.py.')
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    if h.hexdigest() != asset['sha256']:
        raise SystemExit(f'Контрольна сума {path.name} не збігається. Повторіть download-model.py.')
    assets.append(path)

def run(*args, **kwargs):
    return subprocess.run(args, cwd=root, env=env, check=True, **kwargs)

run('docker', 'compose', 'build')
# Capture configuration in memory; it contains connection secrets and must not be printed.
config = json.loads(run('docker', 'compose', 'config', '--format', 'json', capture_output=True, text=True).stdout)
volume = config['volumes']['model_data']['name']
run('docker', 'volume', 'create', '--label', 'com.docker.compose.project=' + config['name'],
    '--label', 'com.docker.compose.volume=model_data', volume, stdout=subprocess.DEVNULL)
print('Імпорт перевіреної моделі у Docker volume…', flush=True)
process = subprocess.Popen(['docker', 'run', '--rm', '-i', '--mount', f'type=volume,source={volume},target=/models',
                            'alpine:3.22', 'tar', '-xf', '-', '-C', '/models'], cwd=root, stdin=subprocess.PIPE)
try:
    with tarfile.open(fileobj=process.stdin, mode='w|') as archive:
        for path in assets:
            archive.add(path, arcname=path.name)
finally:
    process.stdin.close()
if process.wait() != 0:
    raise SystemExit('Імпорт моделі не завершено.')
run('docker', 'compose', 'up', '-d', '--wait', 'db')
run('docker', 'compose', 'up', '-d', '--no-deps', '--force-recreate', 'app')
port = config['services']['app']['ports'][0]['published']
print(f'Контейнери запущено: http://localhost:{port}. Перша ініціалізація може тривати кілька секунд.')
