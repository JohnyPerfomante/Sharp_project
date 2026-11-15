# ПЛАВС — лінгвістичний аналіз відповідей студентів

Вебпідсистема для інформаційної системи «Освітній процес»: банк питань, еталони, погодження альтернативних формулювань, тестування, локальний аналіз українських відповідей, ручна перевірка та звітність. Академічне рішення залишається за викладачем.

Стек: **C# 14 / .NET 10, ASP.NET Core Web API, EF Core 10, PostgreSQL 17, Identity, ML.NET, ONNX Runtime, Raffinert.FuzzySharp; HTML/CSS/ES modules, Bootstrap 5, Chart.js**. Без зовнішніх AI API, Node-сервера та фронтенд-фреймворків. Усі бібліотеки інтерфейсу збережені локально.

## Завантаження на GitHub

Репозиторій містить вихідний код, залежності з фіксованими версіями, міграції, початкові дані, тести, Docker-конфігурацію та [повну DOCX-інструкцію](docs/user-guide/Інструкція_користувача_ПЛАВС.docx). Великі модельні файли та результати збірки зберігаються поза папкою проєкту. Робоча база даних залишається в Docker volume.

Щоб отримати фізично чисту папку для завантаження через браузер:

```bash
python3 scripts/package.py --directory ../PLAVS-GitHub
```

Поряд із проєктом з’являться папка `PLAVS-GitHub` і архів `PLAVS-GitHub.zip`. Завантажуйте вміст чистої папки в корінь репозиторію; ZIP спочатку розпакуйте. Перевірка комплекту працює також без встановленого Git. [Покрокова інструкція публікації та перелік локальних файлів](docs/github.md).

## Швидкий запуск через Docker

Потрібні Docker Desktop із запущеним Docker Engine та Python 3.10+ для допоміжних скриптів. Сервер, алгоритми й інтерфейс реалізовані на погодженому стеку; Python використовується лише для налаштування та пакування.

```bash
python3 scripts/docker-start.py
```

Скрипт автоматично створює локальну `.env`, завантажує й перевіряє модель, збирає контейнери та запускає застосунок. На повторному запуску справна модель використовується з кешу, а наявна `.env` зберігається.

Відкрити **[http://localhost:5080](http://localhost:5080)**. Перша збірка завантажує NuGet-пакети; у Development застосовуються міграції й початкове наповнення. `docker compose logs -f app` показує стан запуску. `docker compose down` зупиняє застосунок і зберігає дані у Docker volume. Не використовуйте `down -v`, якщо потрібна база.

Docker-скрипт переносить модель через stdin у named volume: доступ Docker до папок Mac/Windows не потрібен. Звичайний `docker compose up --build -d` також працює, але без попереднього імпорту моделі семантичний компонент буде недоступний. Для оновлення моделі повторіть `docker-start.py`.

Модель займає приблизно 479 MB разом із токенізатором і завантажується **окремо від Git** з перевіркою SHA-256. За замовчуванням файли зберігаються в `~/.cache/plavs/models/<ревізія>`; точний шлях покаже `python3 scripts/download-model.py --print-directory`. Папка `models` у репозиторії містить маніфест, а не ваги моделі. Для іншого розміщення задайте `PLAVS_MODEL_DIRECTORY` у локальній `.env` або середовищі.

Якщо пропустити встановлення моделі, система працює з ручною перевіркою: M та S залишаються `null`. Після встановлення моделі перезапустіть API й оберіть «Повторити аналіз» для відповідей, які очікують перевірки.

## Демонстраційні облікові записи

Створюються **лише в Development**, якщо `Seed__DemoUsers=true`:

| Роль          | Email                 | Пароль за замовчуванням |
| ------------- | --------------------- | ----------------------- |
| Адміністратор | `admin@plavs.local`   | `Plavs!Demo2026`        |
| Викладач      | `teacher@plavs.local` | `Plavs!Demo2026`        |
| Студент       | `student@plavs.local` | `Plavs!Demo2026`        |

`PLAVS_DEMO_PASSWORD` у локальному `.env` дозволяє задати інший пароль **до першого створення** записів. Повторне початкове наповнення не скидає паролі й не перезаписує відредаговані питання. Кнопки вибору ролі на сторінці входу підставляють лише стандартні навчальні облікові дані; зі зміненим паролем введіть його вручну.

Початкова база містить **70 питань / 7 дисциплін / 210 варіацій на погодження / 283 поняття**, одну групу та 7 тестів по 3 питання. Навчальні варіації не активуються автоматично. Результати тестів спочатку приховані від студентів.

## Запуск без Docker для API

1. Встановити [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Python 3 і PostgreSQL 17. Перевірте `dotnet --list-sdks`: потрібна версія `10.x`. `global.json` допускає новіші feature bands .NET 10.
2. Створити локальну конфігурацію й запустити PostgreSQL:

```bash
python3 scripts/setup.py
docker compose up -d db
python3 scripts/download-model.py
python3 scripts/run.py
```

Для встановленого окремо PostgreSQL задати `POSTGRES_*` у `.env` або `ConnectionStrings__DefaultConnection` у змінних середовища. База й користувач мають уже існувати. `scripts/run.py` читає `.env` без виконання його вмісту як shell-коду та шукає встановлений .NET 10, не змінюючи системні SDK. Цей скрипт зберігає результати збірки в `~/.cache/plavs/build`, тому вони не накопичуються у проєкті. Завантажена модель автоматично доступна API та утиліті перевірки; у Docker вона копіюється в окремий volume.

Альтернативний запуск напряму, Bash/zsh:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=plavs;Username=plavs;Password=YOUR_LOCAL_PASSWORD'
dotnet run --project src/PLAVS.Api --no-launch-profile --urls http://localhost:5080
```

PowerShell використовує `$env:ASPNETCORE_ENVIRONMENT = 'Development'` та `$env:ConnectionStrings__DefaultConnection = '...'`. Для Python на Windows можна використовувати `py -3` замість `python3`.

## Структура

```text
PLAVS.sln
src/
  PLAVS.Domain/          сутності та стани
  PLAVS.Application/     сценарії, DTO, права доступу, контракти
  PLAVS.Infrastructure/  PostgreSQL, Identity, міграції, seed
  PLAVS.Analysis/        нормалізація, FuzzySharp, ML.NET, локальний ONNX
  PLAVS.Api/             REST API, фоновий обробник, wwwroot
tests/
  PLAVS.UnitTests/
  PLAVS.IntegrationTests/
tools/PLAVS.Evaluate/     перевірка моделі, метрики, матриця помилок
database/seed/           навчальні та контрольні дані
models/manifest.json     точна ревізія моделі та контрольні суми
scripts/                запуск, завантаження моделі, перевірка Git, архів
docs/                   інструкції та технічні описи
```

## Перевірки

```bash
dotnet restore PLAVS.sln --locked-mode
dotnet build PLAVS.sln -c Release --no-restore
dotnet test tests/PLAVS.UnitTests -c Release
```

Інтеграційні тести використовують **справжній PostgreSQL** і самі створюють окрему тимчасову БД `plavs_test_<GUID>`, яку прибирають після завершення. Потрібне право `CREATE DATABASE`; робочі дані не очищуються.

```bash
export PLAVS_TEST_CONNECTION='Host=localhost;Port=5432;Database=postgres;Username=plavs;Password=YOUR_LOCAL_PASSWORD'
dotnet test tests/PLAVS.IntegrationTests -c Release
```

Детерміновані тести відмови моделі використовують явно позначений test double. Реальна модель перевіряється окремою утилітою:

```bash
dotnet run --project tools/PLAVS.Evaluate -- --output artifacts/evaluation.json
```

Вона виконує 60 прикладів, перевіряє українські embeddings, обчислює Accuracy, Precision, Recall, F1, матрицю помилок і фактичний p95. Вибірка має **попередні авторські категорії**, `expertScore: null`; її потрібно незалежно погодити з викладачем. Точність не є доведеною якістю автоматичного виставлення академічних оцінок. Вимірювання та обмеження наведені у [звіті перевірок](docs/verification.md).
