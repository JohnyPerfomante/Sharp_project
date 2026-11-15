#!/usr/bin/env python3
"""Create local configuration without committing credentials."""
from pathlib import Path
import secrets

root = Path(__file__).resolve().parents[1]
path = root / '.env'
if path.exists():
    print('.env уже існує; його збережено без змін.')
else:
    text = (root / '.env.example').read_text()
    text = text.replace('REPLACE_WITH_A_RANDOM_DATABASE_PASSWORD', secrets.token_urlsafe(30))
    path.write_text(text)
    path.chmod(0o600)
    print('Створено .env із випадковим паролем PostgreSQL. Файл виключений із Git.')
