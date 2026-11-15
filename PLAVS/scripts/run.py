#!/usr/bin/env python3
"""Cross-platform development launcher; does not change system SDK installations."""
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
from _common import ROOT as root, build_directory, local_environment, model_directory

env = local_environment()
env.setdefault('Model__Directory', str(model_directory(env)))
env.setdefault('ASPNETCORE_ENVIRONMENT', 'Development')
if 'ConnectionStrings__DefaultConnection' not in env:
    password = env.get('POSTGRES_PASSWORD')
    if not password or password.startswith('REPLACE_'):
        raise SystemExit('Спочатку виконайте python3 scripts/setup.py або задайте ConnectionStrings__DefaultConnection.')
    env['ConnectionStrings__DefaultConnection'] = 'Host=127.0.0.1;Port={};Database={};Username={};Password={}'.format(
        env.get('POSTGRES_PORT', '5432'), env.get('POSTGRES_DB', 'plavs'), env.get('POSTGRES_USER', 'plavs'), password)
env.setdefault('ASPNETCORE_URLS', 'http://localhost:' + env.get('PLAVS_PORT', '5080'))
env.setdefault('Seed__DemoUsers', env.get('PLAVS_DEMO_USERS', 'true'))
env.setdefault('Seed__DemoPassword', env.get('PLAVS_DEMO_PASSWORD', 'Plavs!Demo2026'))
candidates = [shutil.which('dotnet'), str(Path.home()/'.dotnet/dotnet'), str(Path.home()/'.cache/plavs-tools/dotnet/dotnet')]
sdk = None
for candidate in candidates:
    if not candidate or not Path(candidate).is_file():
        continue
    result = subprocess.run([candidate, '--list-sdks'], capture_output=True, text=True)
    if any(line.startswith('10.') for line in result.stdout.splitlines()):
        sdk = candidate
        break
if not sdk:
    raise SystemExit('Потрібен .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0')
env['DOTNET_ROOT'] = str(Path(sdk).resolve().parent)
command = [sdk, 'run', '--project', str(root/'src/PLAVS.Api'),
           '--artifacts-path', str(build_directory(env)), '--no-launch-profile', '--', *sys.argv[1:]]
raise SystemExit(subprocess.call(command, cwd=root, env=env))
