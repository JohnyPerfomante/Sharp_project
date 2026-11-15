"""Shared paths and source selection for the standard-library setup tools."""
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
LOCAL_DIRECTORIES = {
    '.git', '.vs', '.vscode', '.idea', '.local', '__pycache__',
    'bin', 'obj', 'TestResults', 'node_modules', 'artifacts',
    'coverage', 'playwright-report', 'data',
}
LOCAL_SUFFIXES = {
    '.onnx', '.onnx_data', '.safetensors', '.pt', '.pth',
    '.dump', '.backup', '.zip', '.log', '.pfx', '.pem', '.key',
    '.pyc', '.pyo', '.user', '.suo', '.part',
}


def local_environment():
    """Read dotenv as data; never evaluate it or print its secret values."""
    env = dict(os.environ)
    path = ROOT / '.env'
    if path.is_file():
        for raw in path.read_text(encoding='utf-8').splitlines():
            line = raw.strip()
            if line and not line.startswith('#') and '=' in line:
                key, value = line.split('=', 1)
                env.setdefault(key.strip(), value.strip().strip('"').strip("'"))
    return env


def cache_directory(env=None):
    env = local_environment() if env is None else env
    value = env.get('PLAVS_CACHE_DIRECTORY') or str(Path.home() / '.cache' / 'plavs')
    return Path(value).expanduser().resolve()


def model_directory(env=None):
    env = local_environment() if env is None else env
    if env.get('PLAVS_MODEL_DIRECTORY'):
        path = Path(env['PLAVS_MODEL_DIRECTORY']).expanduser()
        return (path if path.is_absolute() else ROOT / path).resolve()
    revision = json.loads((ROOT / 'models/manifest.json').read_text(encoding='utf-8'))['revision']
    return cache_directory(env) / 'models' / revision


def build_directory(env=None):
    workspace = hashlib.sha256(str(ROOT).encode('utf-8')).hexdigest()[:12]
    return cache_directory(env) / 'build' / workspace


def is_local_file(relative):
    path = Path(relative)
    name = path.name.lower()
    return (
        any(part in LOCAL_DIRECTORIES for part in path.parts)
        or name == '.ds_store'
        or (name.startswith('.env') and name != '.env.example')
        or name == 'appsettings.local.json'
        or (name.startswith('appsettings.') and name.endswith('.local.json'))
        or path.suffix.lower() in LOCAL_SUFFIXES
        or name.endswith('.bpe.model')
        or (path.parts[:2] == ('models', 'onnx') and name != '.gitkeep')
    )


def source_files():
    """Work both in Git checkouts and in downloaded/extracted source folders."""
    if (ROOT / '.git').exists():
        result = subprocess.run(
            ['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'],
            cwd=ROOT, capture_output=True, check=True,
        )
        paths = {Path(os.fsdecode(p)) for p in result.stdout.split(b'\0') if p}
    else:
        paths = set()
        for directory, children, names in os.walk(ROOT):
            children[:] = [name for name in children if name not in LOCAL_DIRECTORIES]
            for name in names:
                relative = (Path(directory) / name).relative_to(ROOT)
                if not is_local_file(relative):
                    paths.add(relative)
    return sorted((p for p in paths if (ROOT / p).is_file()), key=lambda p: p.as_posix())
