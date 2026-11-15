# REST API v1

Базовий префікс `/api/v1`. JSON використовує camelCase, стани — рядки enum. OpenAPI JSON: `/swagger/v1/swagger.json`, інтерактивний Swagger: `/swagger`, обидва лише у Development. Статичний клієнт та API мають спільний origin; CORS не відкрито.

## Сесія та CSRF

1. `GET /auth/csrf` встановлює antiforgery-cookie та повертає `{ "token": "..." }`.
2. `POST /auth/login` з `{ "email": "...", "password": "..." }` та заголовком `X-CSRF-TOKEN`. Клієнт зберігає HttpOnly cookie сесії.
3. Знову `GET /auth/csrf`: після входу токен має відповідати автентифікованій особі.
4. Усі POST/PUT/DELETE надсилають актуальний токен; GET не змінює дані. `credentials: 'same-origin'` у браузері.
5. `GET /auth/me` повертає безпечний UserView; `POST /auth/logout` завершує сесію.

```javascript
let token = (await (await fetch('/api/v1/auth/csrf')).json()).token;
await fetch('/api/v1/auth/login', {
  method: 'POST', credentials: 'same-origin',
  headers: {'Content-Type': 'application/json', 'X-CSRF-TOKEN': token},
  body: JSON.stringify({email, password})
});
token = (await (await fetch('/api/v1/auth/csrf')).json()).token;
```

## Маршрути

| Метод і шлях | Доступ та поведінка |
|---|---|
| GET `/auth/config` | Публічна ознака демонстраційного середовища, без секретів |
| GET/POST `/questions` | Teacher; список із search, disciplineId, status, page, size; створення |
| GET/PUT/DELETE `/questions/{id}` | Teacher призначеної дисципліни; DELETE архівує; PUT потребує актуального revision |
| GET `/questions/{id}/history` | Попередні незмінні версії питання |
| POST `/questions/{id}/reference-answers` | Додатковий активний еталон |
| PUT/DELETE `/questions/{id}/reference-answers/{referenceId}` | Редагування або деактивація; основний еталон деактивувати не можна |
| POST `/questions/{id}/variants` | Створення Pending-формулювання |
| PUT `/questions/{id}/variants/{variantId}` | Редагування з поверненням у Pending |
| POST `/questions/{id}/variants/{variantId}/decision` | `{status: Approved, Rejected або Inactive}` |
| POST/PUT `/questions/{id}/keywords` | Повна заміна масиву `{term,synonyms,weight}` з історією питання |
| GET `/tests` | Ресурсно обмежений список; Student отримує лише доступні тести, без еталонів |
| POST/PUT/DELETE `/tests[/{id}]` | Teacher; створення/редагування/архівування; склад фіксується після першої спроби |
| PUT `/tests/{id}/results-visibility` | Teacher; JSON `true` або `false` |
| POST `/tests/{id}/start` | Student; одна спроба, повторний запит ідемпотентний |
| GET `/tests/attempts/{id}` | Лише власник; питання, дедлайн і збережені чернетки |
| POST `/tests/attempts/{id}/submit` | Лише власник; 202, відповіді надіслані в стійку чергу аналізу |
| POST `/answers` | Student; збереження чернетки `{attemptId,questionId,text}` до дедлайну |
| GET `/answers` | Teacher або Student; scope за дисциплінами/групами або власником; status, testId, page, size |
| GET `/answers/{id}/analysis` | Teacher — повні деталі; власник-Student — окрема безпечна проєкція |
| POST `/answers/{id}/reanalyze` | Teacher; 202, повторна постановка в чергу; після остаточного ручного рішення заборонено |
| GET `/answers/{id}/history` | Teacher; збережені запуски алгоритму |
| POST `/answers/{id}/review` | Teacher; decision, score, comment, createVariant |
| GET/POST/PUT/DELETE `/disciplines[/{id}]` | GET — особистий scope; зміни — Teacher своєї дисципліни або Administrator; DELETE архівує |
| GET `/groups`, `/directory` | Teacher — призначені групи; Administrator — довідники для призначень |
| GET `/dashboard` | Рольова агрегована статистика |
| GET `/reports` | Teacher/Administrator; disciplineId, groupId, testId, from, to |
| GET `/reports/export?format=csv` або `xlsx` | Ті самі агрегати й фільтри; без персональних відповідей |
| GET/PUT `/settings` | Teacher/Administrator; незмінні версії, optimistic version check |
| GET `/settings/history` | До 100 останніх версій |
| GET `/system/health` | Teacher/Administrator; PostgreSQL, ONNX, версія алгоритму |
| GET/POST/PUT `/admin/users[/{id}]` | Administrator; ролі, блокування й скидання пароля |
| POST/PUT/DELETE `/admin/groups[/{id}]` | Administrator; створення/назва/архівування |
| PUT `/admin/groups/{id}/members` | Administrator; масив ID студентів |
| PUT `/admin/disciplines/{id}/assignments` | Administrator; `{teacherIds,groupIds}` |
| GET `/admin/audit` | Administrator; пагінований аудит |
| GET/PUT `/admin/settings` | Аліас адміністративного доступу до версій налаштувань |

Списки питань, тестів, відповідей, користувачів та аудиту повертають `{items,total,pageNumber,pageSize}`. Розмір сторінки 1–100, за замовчуванням 20 (аудит — 30). Архівні дані залишаються доступними уповноваженим користувачам для історії.

## HTTP-результати

- 200 — читання або оновлення з результатом; 201 — створення; 202 — збережено та поставлено в обробку; 204 — виконано без тіла.
- 400 — валідація форми, ваг, балів, станів або CSRF.
- 401 — немає сесії, неправильний пароль, заблокований/змінений обліковий запис.
- 403 — роль не має маршруту. 404 — ресурс відсутній **або не належить дозволеному scope**.
- 409 — застаріла версія, конфлікт конкурентності, повтор унікального запису або заборонений перехід стану.
- 429 — перевищено частоту входу; 503 — база тимчасово недоступна; 500 — безпечне повідомлення з traceId.

Повертається ProblemDetails, у валідації MVC — також словник `errors`. Внутрішні SQL, паролі та студентські тексти не включаються в помилки.

Оцінка в review задається у відсотках: Accept=100, Reject=0, Partial між 0 і 100, Defer без бала. Кандидат варіації створюється лише з непорожньої Accept-відповіді й залишається Pending. Бал за питання дорівнює `score × maxPoints / 100`.
