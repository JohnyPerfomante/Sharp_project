import { api, refreshCsrf } from "./api.js";
import {
  esc,
  icon,
  date,
  percent,
  initials,
  roleName,
  statusNames,
  badge,
  heading,
  empty,
  notice,
  btn,
  field,
  area,
  option,
  select,
  toast,
  formDialog,
  confirmDialog,
  infoDialog,
  pager,
} from "./ui.js";

const state = {
  user: null,
  disciplines: [],
  groups: [],
  directory: null,
  charts: [],
  epoch: 0,
  timer: null,
  attempt: null,
  index: 0,
};
const actions = {};
const app = document.getElementById("app");
const teacher = () => state.user?.roles.includes("Teacher");
const admin = () => state.user?.roles.includes("Administrator");
const student = () =>
  state.user?.roles.includes("Student") && !teacher() && !admin();
const routeUrl = () =>
  new URL(location.hash.slice(1) || "/dashboard", "https://plavs.local");
const go = (path) => {
  if (location.hash === `#/${path}`) route();
  else location.hash = `#/${path}`;
};
const main = (html) => {
  document.getElementById("main").innerHTML =
    html +
    `<footer class="footer-note"><span>ПЛАВС · Інформаційна система «Освітній процес»</span><span>Лінгвістичний аналіз відповідей студентів</span></footer>`;
};
const run = async (fn) => {
  try {
    await fn();
  } catch (error) {
    toast(error.message, true);
    if (error.status === 401) {
      state.user = null;
      await loginPage();
    }
  }
};
document.addEventListener("click", (event) => {
  const el = event.target.closest("[data-action]");
  if (!el || el.disabled) return;
  event.preventDefault();
  const action = actions[el.dataset.action];
  if (action) run(() => action(el.dataset.id, el));
});
actions["close-dialog"] = () => document.getElementById("modal").close();
actions["toggle-sidebar"] = () => {
  if (innerWidth <= 640) document.body.classList.toggle("sidebar-open");
  else document.body.classList.toggle("collapsed");
};
actions["profile"] = () =>
  infoDialog(
    "Мій профіль",
    `<div class="d-flex align-items-center gap-3 mb-4"><span class="avatar">${esc(initials(state.user.fullName))}</span><div><h3 class="mb-1">${esc(state.user.fullName)}</h3><span class="muted">${esc(state.user.email)}</span></div></div><p class="muted">Роль: ${state.user.roles.map(roleName).map(esc).join(", ")}</p><p class="form-hint">Зміни облікового запису та скидання пароля виконує адміністратор.</p>${btn("Вийти з облікового запису", "logout", "", "btn-light", "logout")}`,
  );
actions["logout"] = async () => {
  await api("auth/logout", { method: "POST" });
  state.user = null;
  history.replaceState(null, "", location.pathname + "#/dashboard");
  document.getElementById("modal").close();
  await refreshCsrf();
  await loginPage();
};

async function loginPage() {
  clearInterval(state.timer);
  state.charts.forEach((c) => c.destroy());
  state.charts = [];
  document.body.classList.remove("collapsed", "sidebar-open");
  let demo = false;
  try {
    demo = (await api("auth/config")).demo;
  } catch {}
  app.innerHTML = `<div class="login-page"><section class="login-story"><a class="brand" href="#/dashboard"><img src="/logo.svg" alt=""><div class="brand-text"><strong>ПЛАВС</strong><small>ОСВІТНІЙ ПРОЦЕС</small></div></a><div class="login-copy"><div class="eyebrow">ЛІНГВІСТИЧНИЙ АНАЛІЗ ВІДПОВІДЕЙ</div><h1>Різні слова.<br><span>Спільний зміст</span></h1><p>Простір для змістовного оцінювання знань. Від навчального питання до обґрунтованого рішення викладача</p><div class="login-flow"><span class="flow-item">Відповідь студента</span>${icon("arrow")}<span class="flow-item">Аналіз змісту</span></div></div><div class="login-bottom"><span>ІПЗ & КН · Навчальна платформа</span><span>Локальне опрацювання даних</span></div></section><section class="login-form-area"><div class="login-box"><div class="eyebrow">ОСОБИСТИЙ КАБІНЕТ</div><h2>Вітаємо у ПЛАВС</h2><p class="login-intro">Увійдіть, щоб продовжити роботу з навчальними матеріалами.</p><form id="login-form"><div id="login-error" role="alert"></div>${field("Електронна пошта", "email", "", "email", 'required autocomplete="username" placeholder="name@university.edu.ua"')}${field("Пароль", "password", "", "password", 'required autocomplete="current-password" placeholder="Введіть пароль"')}<button class="btn btn-primary" type="submit">Увійти до кабінету ${icon("arrow")}</button></form><div class="login-safe">${icon("shield")} Захищений доступ до освітніх даних</div>${demo ? `<div class="demo-box"><strong>Навчальне середовище</strong><br>Оберіть свою роль<div class="demo-buttons"><button data-action="demo" data-id="teacher">Викладач</button><button data-action="demo" data-id="student">Студент</button><button data-action="demo" data-id="admin">Адміністратор</button></div></div>` : ""}</div></section></div>`;
  document
    .getElementById("login-form")
    .addEventListener("submit", async (e) => {
      e.preventDefault();
      const button = e.submitter;
      button.disabled = true;
      document.getElementById("login-error").innerHTML = "";
      try {
        const form = new FormData(e.target);
        await refreshCsrf();
        state.user = await api("auth/login", {
          method: "POST",
          body: { email: form.get("email"), password: form.get("password") },
        });
        await refreshCsrf();
        await enterWorkspace();
      } catch (error) {
        document.getElementById("login-error").innerHTML =
          `<div class="error-box">${esc(error.message)}</div>`;
      } finally {
        button.disabled = false;
      }
    });
}
actions.demo = (id) => {
  document.getElementById("email").value = `${id}@plavs.local`;
  document.getElementById("password").value = "Plavs!Demo2026";
  document.getElementById("password").focus();
};

const navItems = () =>
  admin()
    ? [
        ["dashboard", "Огляд системи", "grid"],
        ["users", "Користувачі", "users"],
        ["disciplines", "Дисципліни", "book"],
        ["groups", "Навчальні групи", "layers"],
        ["reports", "Статистика та звіти", "chart"],
        ["audit", "Журнал дій", "clock"],
        ["settings", "Налаштування аналізу", "settings"],
        ["system", "Стан системи", "activity"],
      ]
    : student()
      ? [
          ["dashboard", "Мій огляд", "grid"],
          ["disciplines", "Мої дисципліни", "book"],
          ["tests", "Доступні тести", "test"],
          ["results", "Мої результати", "chart"],
        ]
      : [
          ["dashboard", "Огляд", "grid"],
          ["disciplines", "Мої дисципліни", "book"],
          ["questions", "Банк питань", "question"],
          ["references", "Еталонні відповіді", "text"],
          ["variants", "Лінгвістичні варіації", "layers"],
          ["keywords", "Ключові поняття", "key"],
          ["tests", "Конструктор тестів", "test"],
          ["answers", "Відповіді студентів", "answers"],
          ["review", "Ручна перевірка", "review"],
          ["reports", "Статистика та звіти", "chart"],
          ["settings", "Налаштування аналізу", "settings"],
        ];
function shell() {
  app.innerHTML = `<div class="mobile-shade" data-action="toggle-sidebar"></div><aside class="sidebar"><a class="brand" href="#/dashboard"><img src="/logo.svg" alt=""><div class="brand-text"><strong>ПЛАВС</strong><small>ОСВІТНІЙ ПРОЦЕС</small></div></a><nav class="sidebar-body" aria-label="Головна навігація"><div class="nav-label">${student() ? "Навчання" : "Робочий простір"}</div>${navItems()
    .map(
      ([path, label, symbol], i) =>
        `${i === (teacher() ? 9 : 4) ? '<div class="nav-label">Аналітика й керування</div>' : ""}<a class="nav-link" href="#/${path}" title="${label}" data-nav="${path}">${icon(symbol)}<span>${label}</span></a>`,
    )
    .join(
      "",
    )}</nav><div class="sidebar-footer">${icon("shield")}<div><strong>Дані залишаються тут</strong>Локальний лінгвістичний аналіз<br>ПЛАВС · версія 1.0</div></div></aside><div class="workspace"><header class="topbar"><div class="topbar-left"><button class="icon-button" data-action="toggle-sidebar" aria-label="Згорнути або розгорнути меню">${icon("menu")}</button><div class="breadcrumbs"><span>Освітній процес</span>${icon("chevron")}<strong id="breadcrumb">Огляд</strong></div></div><div class="topbar-right"><span class="environment">НАВЧАЛЬНИЙ ПРОСТІР</span><button class="user-button" data-action="profile" aria-label="Відкрити профіль"><span class="avatar">${esc(initials(state.user.fullName))}</span><span class="user-text"><span class="user-name">${esc(state.user.fullName)}</span><span class="user-role">${esc(roleName(state.user.roles[0]))}</span></span></button></div></header><main id="main" class="main" tabindex="-1"></main></div>`;
}
async function enterWorkspace() {
  state.disciplines = await api("disciplines");
  if (!student()) {
    [state.groups, state.directory] = await Promise.all([
      api("groups"),
      api("directory"),
    ]);
  }
  shell();
  if (!location.hash) go("dashboard");
  else await route();
}
async function route() {
  if (!state.user) return;
  const epoch = ++state.epoch;
  clearInterval(state.timer);
  state.timer = null;
  state.charts.forEach((c) => c.destroy());
  state.charts = [];
  document.body.classList.remove("sidebar-open");
  const url = routeUrl(),
    [page, id] = url.pathname.slice(1).split("/");
  const active =
    page === "question"
      ? "questions"
      : page === "answer"
        ? "review"
        : page === "attempt"
          ? "tests"
          : page;
  const item = navItems().find((i) => i[0] === active);
  document
    .querySelectorAll("[data-nav]")
    .forEach((n) => n.classList.toggle("active", n.dataset.nav === active));
  document.getElementById("breadcrumb").textContent =
    item?.[1] || "Робочий простір";
  document.title = `${item?.[1] || "ПЛАВС"} · ПЛАВС`;
  main(
    '<div class="loading-content"><span class="spinner"></span>Завантаження даних…</div>',
  );
  try {
    const render = await (
      {
        dashboard: dashboardPage,
        questions: questionsPage,
        references: questionsPage,
        variants: questionsPage,
        keywords: questionsPage,
        question: () => questionPage(id),
        disciplines: disciplinesPage,
        tests: testsPage,
        attempt: () => attemptPage(id),
        answers: answersPage,
        review: answersPage,
        results: answersPage,
        answer: () => answerPage(id),
        reports: reportsPage,
        settings: settingsPage,
        users: usersPage,
        groups: groupsPage,
        audit: auditPage,
        system: systemPage,
      }[page] || dashboardPage
    )(url.searchParams, page);
    if (epoch !== state.epoch) return;
    if (typeof render === "function") render();
    document.getElementById("main")?.focus({ preventScroll: true });
  } catch (error) {
    if (epoch !== state.epoch) return;
    if (error.status === 401) {
      state.user = null;
      await loginPage();
      return;
    }
    main(
      empty(
        "Не вдалося завантажити розділ",
        error.message,
        btn("Повторити", "refresh", "", "btn-light"),
      ),
    );
  }
}
actions.refresh = () => route();
window.addEventListener("hashchange", () => run(route));

function stat(label, value, symbol, note, accent = false) {
  return `<div class="stat ${accent ? "accent" : ""}"><div class="stat-top"><span>${label}</span><span class="stat-icon">${icon(symbol)}</span></div><div class="stat-number">${value}</div><div class="stat-note">${note}</div></div>`;
}
async function dashboardPage() {
  const data = await api("dashboard"),
    m = data.metrics;
  return () => {
    const first = state.user.fullName.split(" ")[0];
    const report = m.reports || [];
    main(
      heading(
        student() ? `Вітаємо, ${first}` : "Огляд навчального простору",
        student()
          ? "Ваші дисципліни, активні тести та результати навчання."
          : "Питання, відповіді й результати — усе важливе для щоденної роботи.",
        teacher()
          ? btn("Створити питання", "question-new", "", "btn-primary", "plus")
          : admin()
            ? btn("Додати користувача", "user-new", "", "btn-primary", "plus")
            : btn(
                "Перейти до тестів",
                "goto-tests",
                "",
                "btn-primary",
                "arrow",
              ),
        new Intl.DateTimeFormat("uk-UA", {
          day: "numeric",
          month: "long",
          year: "numeric",
        }).format(new Date()),
      ) +
        `<div class="stats">${admin() ? stat("Користувачів", data.users.total, "users", `${data.users.teachers} викладачів · ${data.users.students} студентів`) : stat("Мої дисципліни", m.disciplines, "book", "Призначені навчальні напрями")}${stat(student() ? "Доступні тести" : "Питань у банку", student() ? m.tests : m.questions, student() ? "test" : "question", student() ? "У вашому навчальному просторі" : "Збережені в базі знань")}${stat(student() ? "Мої відповіді" : "Відповідей студентів", m.answers, "answers", student() ? "З дозволеними результатами" : "Надіслані студентами")}${stat("Очікують перевірки", m.pending, "review", "Потребують уваги викладача", true)}</div>` +
        (!student() && data.semantic && !data.semantic.available
          ? notice(
              "Потрібне налаштування семантичного модуля",
              data.semantic.message,
              true,
            )
          : "") +
        `<div class="grid-2"><section class="card-panel"><div class="panel-header"><div><h2>Активність відповідей</h2><p>Надіслані відповіді за останні 14 днів</p></div><span class="badge-status blue">14 днів</span></div><div class="chart-wrap"><canvas id="activity-chart" aria-label="Кількість відповідей за днями" role="img"></canvas></div></section><section class="card-panel"><div class="panel-header"><div><h2>Розподіл результатів</h2><p>Попередні та перевірені оцінки</p></div>${icon("chart")}</div>${m.distribution.high + m.distribution.partial + m.distribution.low ? `<div class="donut-wrap"><canvas id="distribution-chart" aria-label="Розподіл високих, часткових і низьких результатів" role="img"></canvas></div><div class="chart-legend"><span><i class="legend-dot" style="background:#6d9f90"></i>Високі</span><span><i class="legend-dot" style="background:#d4b675"></i>Часткові</span><span><i class="legend-dot" style="background:#c68890"></i>Низькі</span></div>` : '<div class="chart-empty">Оцінки з’являться після аналізу<br>або рішення викладача.</div>'}</section></div>` +
        `<div class="grid-2"><section class="card-panel"><div class="panel-header"><div><h2>${admin() ? "Останні системні події" : student() ? "Мій навчальний простір" : "База знань у роботі"}</h2><p>${admin() ? "Критичні дії користувачів" : student() ? "Продовжуйте роботу зі своїми дисциплінами" : "Підготуйте матеріали до оцінювання"}</p></div></div>${
          admin()
            ? eventRows(m.events)
            : student()
              ? state.disciplines
                  .slice(0, 4)
                  .map(
                    (d) =>
                      `<a class="feature-row" href="#/tests?disciplineId=${d.id}"><span class="feature-icon">${icon("book")}</span><div><strong>${esc(d.name)}</strong><p>${d.tests} тестів у дисципліні</p></div></a>`,
                  )
                  .join("") ||
                empty(
                  "Дисциплін поки немає",
                  "Адміністратор призначить вашу групу.",
                )
              : `<a class="feature-row" href="#/variants"><span class="feature-icon">${icon("layers")}</span><div><strong>Лінгвістичні варіації</strong><p>${m.pendingVariants} формулювань очікують погодження</p></div><span class="icon-button">${icon("chevron")}</span></a><a class="feature-row" href="#/review"><span class="feature-icon">${icon("review")}</span><div><strong>Ручна перевірка відповідей</strong><p>${m.pending} відповідей потребують вашого рішення</p></div><span class="icon-button">${icon("chevron")}</span></a><a class="feature-row" href="#/tests"><span class="feature-icon">${icon("test")}</span><div><strong>Конструктор тестів</strong><p>${m.tests} тестів у ваших дисциплінах</p></div><span class="icon-button">${icon("chevron")}</span></a>`
        }</section><section class="card-panel"><div class="panel-header"><h2>${student() ? "Як оцінюється відповідь" : "Поняття, що потребують уваги"}</h2></div><div class="panel-body">${student() ? `<p class="muted small">Формулюйте думку своїми словами й розкривайте всі частини питання. Система порівняє зміст із навчальними матеріалами.</p><p class="muted small mb-0">Результат може потребувати ручної перевірки. Остаточне рішення приймає викладач.</p>` : m.frequentReasons.length ? m.frequentReasons.map((r) => `<div class="system-row"><span>${esc(r.term)}</span><strong>${r.count} пропусків</strong></div>`).join("") : `<p class="muted small">Після перших відповідей тут з’являться поняття, які студенти розкривають найрідше.</p><div class="reference-block small muted">Оцінка поєднує лексичну схожість, семантичну близькість і повноту відповіді.</div>`}</div></section></div>` +
        (report.length
          ? `<section class="card-panel"><div class="panel-header"><h2>Результати за дисциплінами</h2>${!student() ? '<a class="link-small" href="#/reports">Усі звіти ' + icon("arrow") + "</a>" : ""}</div>${reportTable(report.slice(0, 5))}</section>`
          : ""),
    );
    if (window.Chart) {
      state.charts.push(
        new Chart(document.getElementById("activity-chart"), {
          type: "bar",
          data: {
            labels: m.activity.map(
              (a) => a.date.slice(8, 10) + "." + a.date.slice(5, 7),
            ),
            datasets: [
              {
                data: m.activity.map((a) => a.count),
                backgroundColor: "#6c9aa8",
                hoverBackgroundColor: "#2d708a",
                borderRadius: 3,
                maxBarThickness: 24,
              },
            ],
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: { legend: { display: false } },
            scales: {
              x: {
                grid: { display: false },
                ticks: { color: "#8b9ca8", font: { size: 9 } },
                border: { display: false },
              },
              y: {
                beginAtZero: true,
                ticks: { precision: 0, color: "#9aabb5", font: { size: 9 } },
                grid: { color: "#eef2f4" },
                border: { display: false },
              },
            },
          },
        }),
      );
      if (document.getElementById("distribution-chart"))
        state.charts.push(
          new Chart(document.getElementById("distribution-chart"), {
            type: "doughnut",
            data: {
              labels: ["Високі", "Часткові", "Низькі"],
              datasets: [
                {
                  data: [
                    m.distribution.high,
                    m.distribution.partial,
                    m.distribution.low,
                  ],
                  backgroundColor: ["#6d9f90", "#d4b675", "#c68890"],
                  borderWidth: 5,
                  borderColor: "#fff",
                },
              ],
            },
            options: {
              responsive: true,
              maintainAspectRatio: false,
              cutout: "77%",
              plugins: { legend: { display: false } },
            },
          }),
        );
    }
  };
}
actions["goto-tests"] = () => go("tests");
function eventRows(rows) {
  return rows.length
    ? rows
        .map(
          (r) =>
            `<div class="activity-row"><span class="activity-dot">${icon("clock")}</span><div>${esc(eventName(r.action))}<small>${esc(r.entityType)} · ${esc(r.entityId.slice(0, 8))}</small></div><time>${esc(date(r.createdAt))}</time></div>`,
        )
        .join("")
    : empty("Подій поки немає", "Дії користувачів з’являтимуться автоматично.");
}
function eventName(action) {
  return (
    {
      "Auth.Login": "Вхід до системи",
      "Question.Create": "Створено питання",
      "Question.Update": "Оновлено питання",
      "Variant.Approved": "Затверджено варіацію",
      "Attempt.Submit": "Надіслано відповіді",
      "Attempt.Start": "Розпочато тест",
      "User.Create": "Створено користувача",
      "User.Update": "Оновлено користувача",
      "Settings.CreateVersion": "Оновлено параметри аналізу",
    }[action] || action
  );
}

async function questionsPage(params, pageName) {
  if (!teacher()) throw new Error("Розділ доступний викладачу.");
  const query = new URLSearchParams(params);
  query.set("size", "15");
  const data = await api(`questions?${query}`);
  return () => {
    const title = {
      questions: "Банк питань",
      references: "Еталонні відповіді",
      variants: "Лінгвістичні варіації",
      keywords: "Ключові поняття",
    }[pageName];
    const sub = {
      questions:
        "Створюйте навчальні питання та формуйте основу змістовного оцінювання.",
      references:
        "Основні формулювання, з якими порівнюються відповіді студентів.",
      variants:
        "Перевіряйте альтернативні формулювання та допускайте їх до аналізу.",
      keywords:
        "Важливі поняття, допустимі синоніми та ваги для перевірки повноти.",
    }[pageName];
    const filter = `<form class="filters" id="question-filter"><div class="search-field">${icon("search")}<input class="form-control" name="search" aria-label="Пошук питання" placeholder="Знайти питання або тему…" value="${esc(params.get("search") || "")}"></div><select class="form-select" name="disciplineId" aria-label="Дисципліна">${option("", "Усі дисципліни")}${state.disciplines.map((d) => option(d.id, d.name, params.get("disciplineId") === d.id)).join("")}</select><select class="form-select" name="status" aria-label="Статус">${option("", "Усі статуси")}${["Active", "Draft", "Archived"].map((s) => option(s, statusNames[s], params.get("status") === s)).join("")}</select><button class="btn btn-light" type="submit">Знайти</button></form>`;
    let content = "";
    if (pageName === "questions")
      content = `<div class="table-wrap"><table class="table"><thead><tr><th>Питання</th><th>Дисципліна</th><th>Варіації</th><th>Поняття</th><th>Статус</th><th></th></tr></thead><tbody>${data.items.map((q) => `<tr><td class="primary-cell"><a href="#/question/${q.id}">${esc(q.text)}</a><span class="sub-cell">Q-${q.id.slice(0, 6).toUpperCase()} · версія ${q.revision}</span></td><td>${esc(q.discipline)}</td><td>${q.variants.filter((v) => v.status === "Approved").length}<span class="sub-cell">із ${q.variants.length} затверджено</span></td><td>${q.keywords.length}</td><td>${badge(q.status)}</td><td><div class="row-actions"><button class="icon-button" data-action="question-edit" data-id="${q.id}" aria-label="Редагувати питання">${icon("edit")}</button><a class="icon-button" href="#/question/${q.id}" aria-label="Відкрити питання">${icon("chevron")}</a></div></td></tr>`).join("")}</tbody></table></div>`;
    else if (pageName === "references")
      content = data.items
        .map(
          (q) =>
            `<div class="panel-body border-bottom"><a class="action-link" href="#/question/${q.id}">${esc(q.text)}</a><p class="form-hint">${esc(q.discipline)}</p>${q.references
              .filter((r) => r.active)
              .map(
                (r) =>
                  `<div class="reference-block question-body mt-3">${esc(r.text)}</div>`,
              )
              .join("")}</div>`,
        )
        .join("");
    else if (pageName === "variants")
      content = data.items
        .map(
          (q) =>
            `<div class="panel-body border-bottom"><a class="action-link" href="#/question/${q.id}">${esc(q.text)}</a><p class="form-hint">${esc(q.discipline)}</p>${q.variants.map((v) => variantCard(q.id, v)).join("") || '<p class="muted small">Варіацій поки немає.</p>'}</div>`,
        )
        .join("");
    else
      content = data.items
        .map(
          (q) =>
            `<div class="panel-body border-bottom"><a class="action-link" href="#/question/${q.id}">${esc(q.text)}</a><div class="concept-chips">${q.keywords.map((k) => `<span class="concept-chip">${esc(k.term)} · вага ${k.weight}</span>`).join("")}</div><p class="form-hint mb-0">${q.keywords.map((k) => `${esc(k.term)}: ${k.synonyms.map(esc).join(", ") || "без синонімів"}`).join(" · ")}</p></div>`,
        )
        .join("");
    main(
      heading(
        title,
        sub,
        btn("Створити питання", "question-new", "", "btn-primary", "plus"),
      ) +
        (pageName === "variants"
          ? notice(
              "Лише погоджені формулювання беруть участь в аналізі",
              "Перегляньте зміст перед затвердженням. Зміна затвердженої варіації поверне її на погодження.",
            )
          : "") +
        `<section class="card-panel">${filter}${data.total ? content : empty("Питань не знайдено", "Змініть фільтри або додайте перше навчальне питання.")}${pager(data, pageName + (params.toString() ? `?${new URLSearchParams([...params].filter(([k]) => k !== "page"))}` : ""))}</section>`,
    );
    document.getElementById("question-filter").onsubmit = (e) => {
      e.preventDefault();
      const p = new URLSearchParams(
        [...new FormData(e.target)].filter(([, v]) => v),
      );
      go(`${pageName}?${p}`);
    };
  };
}
const keywordRow = (k) =>
  `<div class="keyword-row"><input class="form-control" name="term" aria-label="Поняття" placeholder="Поняття" value="${esc(k?.term || "")}" required minlength="2" maxlength="160"><input class="form-control" name="synonyms" aria-label="Синоніми через крапку з комою" placeholder="Синоніми через ;" value="${esc(k?.synonyms?.join("; ") || "")}" maxlength="3000"><input class="form-control" name="weight" type="number" step="0.1" min="0.1" max="100" aria-label="Вага" value="${k?.weight || 1}" required><button class="icon-button" type="button" data-action="keyword-remove" aria-label="Прибрати поняття">${icon("close")}</button></div>`;
actions["keyword-remove"] = (_, el) => el.closest(".keyword-row").remove();
actions["keyword-add"] = () =>
  document
    .getElementById("keyword-rows")
    .insertAdjacentHTML("beforeend", keywordRow());
async function editQuestion(id) {
  const q = id ? await api(`questions/${id}`) : null;
  formDialog(
    id ? "Редагування питання" : "Нове навчальне питання",
    `${select(
      "Дисципліна",
      "disciplineId",
      state.disciplines
        .filter((d) => !d.archived)
        .map((d) => option(d.id, d.name, q?.disciplineId === d.id))
        .join(""),
      q ? "disabled" : "required",
    )}${field("Тематичний напрям", "topic", q?.topic || "", "text", 'required maxlength="160"')}${area("Текст питання", "text", q?.text || "", 'required minlength="5" maxlength="2000"')}${area("Основна еталонна відповідь", "reference", q?.references.find((r) => r.isPrimary)?.text || "", 'required minlength="5" maxlength="5000"')}<div class="section-heading"><h3>Ключові поняття</h3>${btn("Додати", "keyword-add", "", "btn-light btn-sm", "plus")}</div><p class="form-hint">Синоніми розділяйте крапкою з комою. Кожне поняття має додатну вагу.</p><div id="keyword-rows">${(q?.keywords || [null, null, null]).map(keywordRow).join("")}</div>${!q ? `<div class="section-heading"><h3>Варіації на погодження</h3></div>${[1, 2, 3].map((i) => area(`Формулювання ${i}`, `variant${i}`, "", 'maxlength="5000"')).join("")}` : ""}${select("Статус питання", "status", ["Active", "Draft", "Archived"].map((s) => option(s, statusNames[s], (q?.status || "Active") === s)).join(""))}`,
    async (form, node) => {
      const keywords = [...node.querySelectorAll(".keyword-row")].map(
        (row) => ({
          term: row.querySelector("[name=term]").value,
          synonyms: row
            .querySelector("[name=synonyms]")
            .value.split(";")
            .map((s) => s.trim())
            .filter(Boolean),
          weight: Number(row.querySelector("[name=weight]").value),
        }),
      );
      if (!keywords.length) throw new Error("Додайте хоча б одне поняття.");
      const input = {
        disciplineId: q?.disciplineId || form.get("disciplineId"),
        topic: form.get("topic"),
        text: form.get("text"),
        reference: form.get("reference"),
        keywords,
        variants: q
          ? []
          : [1, 2, 3].map((i) => form.get(`variant${i}`)).filter(Boolean),
        status: form.get("status"),
        revision: q?.revision,
      };
      const result = await api(id ? `questions/${id}` : "questions", {
        method: id ? "PUT" : "POST",
        body: input,
      });
      toast("Питання збережено");
      go(`question/${result.id}`);
    },
  );
}
actions["question-new"] = () => editQuestion();
actions["question-edit"] = (id) => editQuestion(id);
actions["question-archive"] = async (id) => {
  if (
    await confirmDialog(
      "Архівувати питання?",
      "Питання залишиться в історії оцінювання. Почати новий тест із ним буде неможливо.",
      "Архівувати",
    )
  ) {
    await api(`questions/${id}`, { method: "DELETE" });
    toast("Питання архівовано");
    route();
  }
};
function variantCard(qid, v) {
  return `<article class="variant-item"><div class="variant-meta">${badge(v.status)}<span>${v.source === "SeedDraft" ? "Початкове наповнення" : v.source === "StudentAnswer" ? "З відповіді студента" : "Викладач"}</span></div><p>${esc(v.text)}</p><div class="variant-actions">${v.status !== "Approved" ? `<button class="btn btn-light btn-sm" data-action="variant-decision" data-id="${qid}/${v.id}/Approved">${icon("check")}Затвердити</button>` : `<button class="btn btn-light btn-sm" data-action="variant-decision" data-id="${qid}/${v.id}/Inactive">Деактивувати</button>`}${v.status !== "Rejected" ? `<button class="btn btn-light btn-sm" data-action="variant-decision" data-id="${qid}/${v.id}/Rejected">Відхилити</button>` : ""}<button class="btn btn-light btn-sm" data-action="variant-edit" data-id="${qid}/${v.id}">${icon("edit")}Редагувати</button></div></article>`;
}
actions["variant-decision"] = async (key) => {
  const [q, v, status] = key.split("/");
  if (
    status === "Approved" &&
    !(await confirmDialog(
      "Затвердити формулювання?",
      "Підтвердьте, що це правильна й повна відповідь. Вона використовуватиметься в нових спробах тестування.",
      "Затвердити",
    ))
  )
    return;
  await api(`questions/${q}/variants/${v}/decision`, {
    method: "POST",
    body: { status },
  });
  toast("Статус варіації оновлено");
  route();
};
actions["variant-add"] = (id) =>
  formDialog(
    "Додати альтернативне формулювання",
    area(
      "Текст варіації",
      "text",
      "",
      'required minlength="3" maxlength="5000"',
    ),
    async (form) => {
      await api(`questions/${id}/variants`, {
        method: "POST",
        body: { text: form.get("text") },
      });
      toast("Варіацію надіслано на погодження");
      route();
    },
  );
actions["variant-edit"] = async (key) => {
  const [q, v] = key.split("/");
  const question = await api(`questions/${q}`);
  formDialog(
    "Редагування варіації",
    notice(
      "Потрібне повторне погодження",
      "Після збереження формулювання отримає статус «На погодженні».",
    ) +
      area(
        "Текст варіації",
        "text",
        question.variants.find((x) => x.id === v).text,
        'required minlength="3" maxlength="5000"',
      ),
    async (form) => {
      await api(`questions/${q}/variants/${v}`, {
        method: "PUT",
        body: { text: form.get("text") },
      });
      toast("Варіацію збережено");
      route();
    },
  );
};
async function questionPage(id) {
  const q = await api(`questions/${id}`);
  return () =>
    main(
      heading(
        q.text,
        `${q.discipline} · версія ${q.revision}`,
        btn("Редагувати", "question-edit", q.id, "btn-light", "edit") +
          btn("Архівувати", "question-archive", q.id, "btn-light", "archive"),
        `БАНК ПИТАНЬ / Q-${q.id.slice(0, 6).toUpperCase()}`,
      ) +
        `<div class="split-main"><div class="detail-stack"><section class="card-panel"><div class="panel-header"><h2>Еталонні відповіді</h2>${btn("Додати еталон", "reference-add", id, "btn-light btn-sm", "plus")}</div><div class="panel-body">${q.references.map((r) => `<div class="mb-3"><div class="variant-meta mb-2"><span class="small-label">${r.isPrimary ? "Основна відповідь" : "Додатковий еталон"}${!r.active ? " · неактивний" : ""}</span><span><button class="icon-button" data-action="reference-edit" data-id="${id}/${r.id}" aria-label="Редагувати еталон">${icon("edit")}</button>${!r.isPrimary && r.active ? `<button class="icon-button" data-action="reference-disable" data-id="${id}/${r.id}" aria-label="Деактивувати еталон">${icon("archive")}</button>` : ""}</span></div><div class="reference-block question-body">${esc(r.text)}</div></div>`).join("")}</div></section><section class="card-panel"><div class="panel-header"><div><h2>Лінгвістичні варіації</h2><p>У нових спробах використовуються лише затверджені</p></div>${btn("Додати", "variant-add", id, "btn-light btn-sm", "plus")}</div><div class="panel-body">${q.variants.map((v) => variantCard(id, v)).join("") || empty("Формулювань поки немає", "Додайте альтернативну правильну відповідь.")}</div></section></div><aside class="detail-stack"><section class="card-panel"><div class="panel-header"><h2>Ключові поняття</h2>${icon("key")}</div><div class="panel-body">${q.keywords.map((k) => `<div class="keyword-item"><span class="weight">вага ${k.weight}</span><strong>${esc(k.term)}</strong><small>${k.synonyms.map(esc).join(" · ") || "Синоніми не задані"}</small></div>`).join("")}</div></section><section class="card-panel"><div class="panel-body"><div class="system-row"><span>Статус</span>${badge(q.status)}</div><div class="system-row"><span>Версія</span><strong>${q.revision}</strong></div><div class="system-row"><span>Оновлено</span><span>${esc(date(q.updatedAt))}</span></div><button class="btn btn-light w-100 mt-3" data-action="question-history" data-id="${id}">${icon("clock")}Історія змін</button></div></section></aside></div>`,
    );
}
actions["reference-add"] = (id) =>
  formDialog(
    "Додаткова еталонна відповідь",
    area(
      "Правильна відповідь",
      "text",
      "",
      'required minlength="3" maxlength="5000"',
    ),
    async (form) => {
      await api(`questions/${id}/reference-answers`, {
        method: "POST",
        body: { text: form.get("text") },
      });
      toast("Еталон додано");
      route();
    },
  );
actions["reference-edit"] = async (key) => {
  const [qid, rid] = key.split("/");
  const q = await api(`questions/${qid}`);
  formDialog(
    "Редагування еталона",
    area(
      "Текст еталона",
      "text",
      q.references.find((r) => r.id === rid).text,
      'required minlength="3" maxlength="5000"',
    ),
    async (form) => {
      await api(`questions/${qid}/reference-answers/${rid}`, {
        method: "PUT",
        body: { text: form.get("text") },
      });
      toast("Еталон збережено");
      route();
    },
  );
};
actions["reference-disable"] = async (key) => {
  const [q, r] = key.split("/");
  if (
    await confirmDialog(
      "Деактивувати еталон?",
      "Він залишиться в історії, але не використовуватиметься в нових спробах.",
    )
  ) {
    await api(`questions/${q}/reference-answers/${r}`, { method: "DELETE" });
    route();
  }
};
actions["question-history"] = async (id) => {
  const history = await api(`questions/${id}/history`);
  infoDialog(
    "Історія питання",
    history.length
      ? history
          .map(
            (h) =>
              `<details class="variant-item"><summary>Версія ${h.revision} · ${esc(date(h.createdAt))}</summary><h3 class="mt-3">${esc(h.snapshot.text)}</h3><p>${esc(h.snapshot.references.find((r) => r.isPrimary)?.text || "")}</p><small class="muted">Поняття: ${h.snapshot.keywords.map((k) => esc(k.term)).join(", ")}</small></details>`,
          )
          .join("")
      : empty(
          "Історія порожня",
          "Попередні версії з’являться після редагування.",
        ),
  );
};
async function disciplinesPage() {
  state.disciplines = await api("disciplines");
  return () =>
    main(
      heading(
        student() ? "Мої дисципліни" : "Навчальні дисципліни",
        "Навчальні матеріали та тестування за тематичними напрямами.",
        !student()
          ? btn(
              "Додати дисципліну",
              "discipline-new",
              "",
              "btn-primary",
              "plus",
            )
          : "",
      ) +
        `<div class="discipline-grid">${state.disciplines.map((d) => `<article class="discipline-card"><span class="feature-icon">${icon("book")}</span><h2>${esc(d.name)}</h2><p>${esc(d.description)}</p><div class="card-meta">${!student() ? `<span>${d.questions} питань</span>` : ""}<span>${d.tests} тестів</span></div><div class="card-bottom">${badge(d.archived ? "Archived" : "Active")}<div class="d-flex gap-1">${admin() ? btn("Призначення", "discipline-assign", d.id, "btn-light btn-sm") : ""}${!student() ? `<button class="icon-button" data-action="discipline-edit" data-id="${d.id}" aria-label="Редагувати дисципліну">${icon("edit")}</button><button class="icon-button" data-action="discipline-archive" data-id="${d.id}" aria-label="Архівувати дисципліну">${icon("archive")}</button>` : ""}${!admin() ? `<a class="btn btn-light btn-sm" href="#/${student() ? "tests" : "questions"}?disciplineId=${d.id}">Відкрити ${icon("arrow")}</a>` : ""}</div></div></article>`).join("")}</div>` +
        (state.disciplines.length
          ? ""
          : empty(
              "Дисциплін поки немає",
              "Створіть дисципліну або зверніться до адміністратора щодо призначення.",
            )),
    );
}
function disciplineDialog(id) {
  const d = state.disciplines.find((d) => d.id === id);
  formDialog(
    d ? "Редагування дисципліни" : "Нова дисципліна",
    field(
      "Назва",
      "name",
      d?.name || "",
      "text",
      'required minlength="3" maxlength="160"',
    ) + area("Опис", "description", d?.description || "", 'maxlength="2000"'),
    async (form) => {
      await api(d ? `disciplines/${id}` : "disciplines", {
        method: d ? "PUT" : "POST",
        body: { name: form.get("name"), description: form.get("description") },
      });
      state.disciplines = await api("disciplines");
      toast("Дисципліну збережено");
      route();
    },
    { small: true },
  );
}
actions["discipline-new"] = () => disciplineDialog();
actions["discipline-edit"] = (id) => disciplineDialog(id);
actions["discipline-archive"] = async (id) => {
  if (
    await confirmDialog(
      "Архівувати дисципліну?",
      "Її тести також перейдуть до архіву. Дані зберігатимуться для історії.",
      "Архівувати",
    )
  ) {
    await api(`disciplines/${id}`, { method: "DELETE" });
    toast("Дисципліну архівовано");
    route();
  }
};
actions["discipline-assign"] = async (id) => {
  state.directory = await api("directory");
  const dir = state.directory;
  formDialog(
    "Призначення викладачів і груп",
    `<h3>Викладачі</h3><div class="check-list">${dir.teachers.map((t) => `<label><input type="checkbox" name="teachers" value="${t.id}" ${dir.assignments.some((a) => a.disciplineId === id && a.userId === t.id) ? "checked" : ""}>${esc(t.fullName)}</label>`).join("")}</div><h3 class="mt-4">Навчальні групи</h3><div class="check-list">${state.groups
      .filter((g) => !g.archived)
      .map(
        (g) =>
          `<label><input type="checkbox" name="groups" value="${g.id}" ${dir.disciplineGroups.some((d) => d.disciplineId === id && d.groupId === g.id) ? "checked" : ""}>${esc(g.name)}</label>`,
      )
      .join("")}</div>`,
    async (form) => {
      await api(`admin/disciplines/${id}/assignments`, {
        method: "PUT",
        body: {
          teacherIds: form.getAll("teachers"),
          groupIds: form.getAll("groups"),
        },
      });
      toast("Призначення оновлено");
      route();
    },
  );
};

async function testsPage(params) {
  const query = new URLSearchParams(params);
  query.set("size", "15");
  const data = await api(`tests?${query}`);
  return () => {
    state.tests = data.items;
    main(
      heading(
        student() ? "Мої тести" : "Конструктор тестів",
        student()
          ? "Оберіть доступний тест. Кожна відповідь зберігається на сервері."
          : "Об’єднуйте питання, визначайте час і відкривайте тестування для груп.",
        teacher()
          ? btn("Створити тест", "test-new", "", "btn-primary", "plus")
          : "",
      ) +
        `<div class="test-grid">${data.items.map((t) => `<article class="discipline-card"><div class="d-flex justify-content-between align-items-center mb-3"><span class="feature-icon mb-0">${icon("test")}</span>${badge(t.archived ? "Archived" : t.attemptStatus || (t.published ? "Active" : "Draft"))}</div><div class="eyebrow">${esc(t.discipline)}</div><h2>${esc(t.title)}</h2><p>${esc(t.instructions)}</p><div class="card-meta"><span>${t.questions.length} питань</span><span>${t.durationMinutes} хвилин</span><span>${t.questions.reduce((s, q) => s + q.maxPoints, 0)} балів</span></div><p class="form-hint">${t.opensAt ? "З " + esc(date(t.opensAt)) : ""}${t.closesAt ? " · До " + esc(date(t.closesAt)) : ""}</p><div class="card-bottom">${student() ? `<span class="form-hint">${t.resultsVisible ? "Результати відкриті" : "Результати відкриє викладач"}</span>${t.attemptStatus === "Submitted" ? '<a href="#/results" class="btn btn-light btn-sm">Результати</a>' : btn(t.attemptStatus === "InProgress" ? "Продовжити" : "Почати тест", "test-start", t.id, "btn-primary btn-sm", "arrow")}` : `<div class="d-flex gap-1 flex-wrap">${teacher() ? btn("Редагувати", "test-edit", t.id, "btn-light btn-sm", "edit") + btn(t.resultsVisible ? "Приховати результати" : "Відкрити результати", "test-visibility", t.id, "btn-light btn-sm") : ""}</div>${teacher() ? `<button class="icon-button" data-action="test-archive" data-id="${t.id}" aria-label="Архівувати тест">${icon("archive")}</button>` : ""}`}</div></article>`).join("")}</div>` +
        (data.items.length
          ? ""
          : empty(
              "Тестів поки немає",
              student()
                ? "Викладач відкриє доступ для вашої групи."
                : "Створіть тест на основі банку питань.",
            )) +
        pager(
          data,
          "tests" +
            (params.get("disciplineId")
              ? "?disciplineId=" + params.get("disciplineId")
              : ""),
        ),
    );
  };
}
function localTime(value) {
  if (!value) return "";
  const d = new Date(value);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000)
    .toISOString()
    .slice(0, 16);
}
async function testDialog(id) {
  const t = state.tests?.find((t) => t.id === id);
  state.directory = await api("directory");
  const modal = formDialog(
    t ? "Редагування тесту" : "Новий тест",
    `${select(
      "Дисципліна",
      "disciplineId",
      state.disciplines
        .filter((d) => !d.archived)
        .map((d) => option(d.id, d.name, t?.disciplineId === d.id))
        .join(""),
      t ? "disabled" : "required",
    )}${field("Назва тесту", "title", t?.title || "", "text", 'required minlength="3" maxlength="200"')}${area("Інструкція студенту", "instructions", t?.instructions || "", 'maxlength="4000"')}<div class="row"><div class="col-sm-6">${field("Початок доступу", "opensAt", localTime(t?.opensAt), "datetime-local")}</div><div class="col-sm-6">${field("Завершення доступу", "closesAt", localTime(t?.closesAt), "datetime-local")}</div></div>${field("Тривалість, хвилин", "durationMinutes", t?.durationMinutes || 60, "number", 'required min="1" max="480"')}<h3 class="mt-3">Навчальні групи</h3><div id="test-groups" class="check-list"></div><div class="section-heading"><h3>Питання та бали</h3></div><p class="form-hint">Порядок у списку визначає порядок у тесті. Після першої спроби склад тесту фіксується.</p><div id="test-questions" class="test-question-list"></div><div class="check-list mt-3"><label><input name="published" type="checkbox" ${t?.published ? "checked" : ""}> Опублікувати тест</label><label><input name="resultsVisible" type="checkbox" ${t?.resultsVisible ? "checked" : ""}> Показувати результати студентам</label></div>`,
    async (form, node) => {
      const questions = [
        ...node.querySelectorAll("[name=question]:checked"),
      ].map((el) => ({
        questionId: el.value,
        maxPoints: Number(
          el.closest("label").querySelector("[name=points]").value,
        ),
      }));
      const input = {
        disciplineId: t?.disciplineId || form.get("disciplineId"),
        title: form.get("title"),
        instructions: form.get("instructions"),
        durationMinutes: Number(form.get("durationMinutes")),
        opensAt: form.get("opensAt")
          ? new Date(form.get("opensAt")).toISOString()
          : null,
        closesAt: form.get("closesAt")
          ? new Date(form.get("closesAt")).toISOString()
          : null,
        published: form.has("published"),
        resultsVisible: form.has("resultsVisible"),
        questions,
        groupIds: form.getAll("group"),
      };
      await api(t ? `tests/${id}` : "tests", {
        method: t ? "PUT" : "POST",
        body: input,
      });
      toast("Тест збережено");
      route();
    },
  );
  const load = async () => {
    const disciplineId =
      t?.disciplineId || modal.querySelector("[name=disciplineId]").value;
    const data = await api(
      `questions?disciplineId=${disciplineId}&status=Active&size=100`,
    );
    const order = t?.questions.map((q) => q.questionId) || [];
    data.items.sort((a, b) => {
      const x = order.indexOf(a.id),
        y = order.indexOf(b.id);
      return (x < 0 ? 1000 : x) - (y < 0 ? 1000 : y);
    });
    modal.querySelector("#test-questions").innerHTML =
      data.items
        .map(
          (q) =>
            `<label class="test-question-option"><input name="question" type="checkbox" value="${q.id}" ${t?.questions.some((x) => x.questionId === q.id) ? "checked" : ""}><span>${esc(q.text)}</span><input name="points" class="form-control" type="number" min="0.1" max="1000" step="0.1" aria-label="Максимальний бал" value="${t?.questions.find((x) => x.questionId === q.id)?.maxPoints || 10}"></label>`,
        )
        .join("") ||
      '<div class="panel-body muted small">У дисципліні немає активних питань.</div>';
    const groups = state.groups.filter(
      (g) =>
        !g.archived &&
        state.directory.disciplineGroups.some(
          (d) => d.disciplineId === disciplineId && d.groupId === g.id,
        ),
    );
    modal.querySelector("#test-groups").innerHTML =
      groups
        .map(
          (g) =>
            `<label><input name="group" type="checkbox" value="${g.id}" ${t ? (t.groupIds.includes(g.id) ? "checked" : "") : "checked"}>${esc(g.name)}</label>`,
        )
        .join("") ||
      '<span class="form-hint">Адміністратор має призначити групи дисципліні.</span>';
  };
  modal
    .querySelector("[name=disciplineId]")
    .addEventListener("change", () => run(load));
  await load();
}
actions["test-new"] = () => testDialog();
actions["test-edit"] = (id) => testDialog(id);
actions["test-visibility"] = async (id) => {
  const t = state.tests.find((t) => t.id === id);
  await api(`tests/${id}/results-visibility`, {
    method: "PUT",
    body: !t.resultsVisible,
  });
  toast(
    t.resultsVisible ? "Результати приховано" : "Результати доступні студентам",
  );
  route();
};
actions["test-archive"] = async (id) => {
  if (
    await confirmDialog(
      "Архівувати тест?",
      "Нові спроби стануть недоступні. Історія відповідей збережеться.",
      "Архівувати",
    )
  ) {
    await api(`tests/${id}`, { method: "DELETE" });
    route();
  }
};
actions["test-start"] = async (id) => {
  const result = await api(`tests/${id}/start`, { method: "POST" });
  state.index = 0;
  go(`attempt/${result.id}`);
};

async function attemptPage(id) {
  const attempt = await api(`tests/attempts/${id}`);
  state.attempt = attempt;
  state.index = Math.min(state.index, attempt.questions.length - 1);
  return () => renderAttempt();
}
function renderAttempt() {
  const a = state.attempt;
  if (a.status === "Submitted") {
    main(
      heading("Відповіді надіслано", a.title) +
        notice(
          "Спробу завершено",
          "Збережені відповіді передано на аналіз. Результат стане доступним після дозволу викладача.",
        ) +
        btn("Мої результати", "goto-results", "", "btn-primary", "arrow"),
    );
    return;
  }
  const q = a.questions[state.index],
    saved = a.questions.filter((q) => q.savedText.trim()).length;
  main(
    heading(
      a.title,
      "Відповідайте своїми словами. Збережіть кожну відповідь перед переходом до іншого питання.",
      "",
      "ПРОХОДЖЕННЯ ТЕСТУ",
    ) +
      `<div class="split-main"><section class="card-panel"><div class="panel-body"><div class="attempt-top"><div><span class="small-label">Питання ${state.index + 1} з ${a.questions.length}</span><h2 class="mt-3 lh-base">${esc(q.text)}</h2><span class="form-hint">Максимум ${q.maxPoints} балів</span></div><span class="time-remaining" id="attempt-clock" aria-label="Час до завершення"></span></div><div class="progress-line"><div style="width:${(100 * saved) / a.questions.length}%"></div></div><form id="attempt-answer-form" class="mt-4"><label class="form-label" for="answer-text">Ваша відповідь</label><textarea id="answer-text" class="form-control attempt-answer" name="answer" maxlength="5000" placeholder="Розкрийте зміст питання…">${esc(q.savedText)}</textarea><div class="d-flex justify-content-between mt-2"><span id="save-state" class="save-state">${q.savedText ? "Збережено на сервері" : "Ще не збережено"}</span><span class="form-hint" id="character-count">${q.savedText.length} / 5000</span></div><div class="attempt-footer"><div class="d-flex gap-2">${btn("Назад", "attempt-prev", "", "btn-light")}<button type="submit" class="btn btn-primary">${icon("check")}Зберегти відповідь</button>${btn("Далі", "attempt-next", "", "btn-light")}</div></div></form></div></section><aside class="detail-stack"><section class="card-panel"><div class="panel-header"><h2>Питання тесту</h2><span class="muted small">${saved}/${a.questions.length}</span></div>${a.questions.map((x, i) => `<button class="question-step ${i === state.index ? "current" : ""} ${x.savedText ? "saved" : ""}" data-action="attempt-step" data-id="${i}"><span class="step-number">${x.savedText ? icon("check") : i + 1}</span><span>${esc(x.text)}</span></button>`).join("")}<div class="panel-body">${btn("Надіслати всі відповіді", "attempt-submit", a.id, "btn-primary w-100", "arrow")}<p class="form-hint mt-3 mb-0">Після надсилання редагування буде недоступне. Після завершення часу сервер надішле збережені відповіді автоматично.</p></div></section></aside></div>`,
  );
  const textarea = document.getElementById("answer-text");
  textarea.addEventListener("input", () => {
    document.getElementById("save-state").textContent = "Є незбережені зміни";
    document.getElementById("character-count").textContent =
      `${textarea.value.length} / 5000`;
  });
  document.getElementById("attempt-answer-form").onsubmit = (e) => {
    e.preventDefault();
    run(async () => {
      e.submitter.disabled = true;
      try {
        await saveAttemptAnswer();
        toast("Відповідь збережено");
        renderAttempt();
      } finally {
        e.submitter.disabled = false;
      }
    });
  };
  clearInterval(state.timer);
  const tick = () => {
    const seconds = Math.max(
      0,
      Math.floor((new Date(a.deadline) - Date.now()) / 1000),
    );
    const clock = document.getElementById("attempt-clock");
    if (clock)
      clock.textContent = `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
    if (seconds === 0) {
      clearInterval(state.timer);
      document.querySelector("#answer-text")?.setAttribute("disabled", "");
      if (clock) clock.textContent = "Час завершено";
    }
  };
  tick();
  state.timer = setInterval(tick, 1000);
}
async function saveAttemptAnswer() {
  const q = state.attempt.questions[state.index],
    text = document.getElementById("answer-text")?.value || "";
  if (text === q.savedText) return;
  if (!text.trim()) throw new Error("Введіть відповідь перед збереженням.");
  const result = await api("answers", {
    method: "POST",
    body: { attemptId: state.attempt.id, questionId: q.id, text },
  });
  q.savedText = text;
  q.answerId = result.id;
}
actions["attempt-step"] = async (id) => {
  await saveAttemptAnswer();
  state.index = Number(id);
  renderAttempt();
};
actions["attempt-prev"] = async () => {
  await saveAttemptAnswer();
  state.index = Math.max(0, state.index - 1);
  renderAttempt();
};
actions["attempt-next"] = async () => {
  await saveAttemptAnswer();
  state.index = Math.min(state.attempt.questions.length - 1, state.index + 1);
  renderAttempt();
};
actions["attempt-submit"] = async (id) => {
  if (new Date(state.attempt.deadline) > new Date()) await saveAttemptAnswer();
  const missing = state.attempt.questions.filter(
    (q) => !q.savedText.trim(),
  ).length;
  if (
    await confirmDialog(
      "Завершити тест?",
      missing
        ? `${missing} питань залишилися без відповіді. Надіслати наявні відповіді?`
        : "Усі відповіді збережено. Після надсилання зміни будуть недоступні.",
      "Надіслати",
    )
  ) {
    await api(`tests/attempts/${id}/submit`, { method: "POST" });
    state.attempt.status = "Submitted";
    renderAttempt();
  }
};
actions["goto-results"] = () => go("results");
window.addEventListener("beforeunload", (event) => {
  const textarea = document.getElementById("answer-text");
  if (
    routeUrl().pathname.startsWith("/attempt/") &&
    textarea &&
    textarea.value !== state.attempt?.questions[state.index]?.savedText
  ) {
    event.preventDefault();
    event.returnValue = "";
  }
});
// Save the current draft before leaving an attempt through the navigation menu.
document.addEventListener(
  "click",
  (event) => {
    const anchor = event.target.closest('a[href^="#/"]');
    const textarea = document.getElementById("answer-text");
    if (
      anchor &&
      textarea &&
      textarea.value !== state.attempt?.questions[state.index]?.savedText
    ) {
      event.preventDefault();
      run(async () => {
        await saveAttemptAnswer();
        go(anchor.getAttribute("href").slice(2));
      });
    }
  },
  true,
);

async function answersPage(params, pageName) {
  if (admin())
    throw new Error(
      "Академічні відповіді доступні лише уповноваженому викладачу.",
    );
  const query = new URLSearchParams(params);
  query.set("size", "20");
  if (pageName === "review" && !query.has("status"))
    query.set("status", "NeedsReview");
  const data = await api(`answers?${query}`);
  return () => {
    main(
      heading(
        student()
          ? "Мої результати"
          : pageName === "review"
            ? "Ручна перевірка"
            : "Відповіді студентів",
        student()
          ? "Результати з’являються після дозволу викладача."
          : "Перегляньте автоматичні висновки та збережіть обґрунтоване рішення.",
        btn("Оновити", "refresh", "", "btn-light", "activity"),
      ) +
        `<section class="card-panel">${!student() ? `<form class="filters" id="answer-filter"><span class="small-label">Статус відповіді</span><select name="status" class="form-select" aria-label="Статус відповіді">${option("", "Усі статуси", !query.get("status"))}${["NeedsReview", "PendingAnalysis", "Analyzed", "Reviewed", "AnalysisError"].map((s) => option(s, statusNames[s], query.get("status") === s)).join("")}</select><button type="submit" class="btn btn-light">Застосувати</button></form>` : ""}${data.total ? `<div class="table-wrap"><table class="table"><thead><tr>${!student() ? "<th>Студент</th>" : ""}<th>Питання / тест</th><th>Результат</th><th>Статус</th><th>Надіслано</th><th></th></tr></thead><tbody>${data.items.map((a) => `<tr>${!student() ? `<td>${esc(a.studentName)}</td>` : ""}<td class="primary-cell"><a href="#/answer/${a.id}">${esc(a.question)}</a><span class="sub-cell">${esc(a.test)}</span></td><td><strong>${percent(a.score)}</strong>${a.category ? `<span class="sub-cell">${statusNames[a.category]}</span>` : ""}</td><td>${badge(a.status)}</td><td class="nowrap">${esc(date(a.createdAt))}</td><td><a href="#/answer/${a.id}" class="btn btn-light btn-sm">${student() ? "Переглянути" : "Відкрити"} ${icon("chevron")}</a></td></tr>`).join("")}</tbody></table></div>` : empty(pageName === "review" ? "Черга перевірки порожня" : "Відповідей поки немає", pageName === "review" ? "Спробуйте інший фільтр, щоб побачити всі відповіді." : "Надіслані відповіді відображатимуться тут.")}${pager(data, pageName + (params.toString() ? `?${new URLSearchParams([...params].filter(([k]) => k !== "page"))}` : ""))}</section>`,
    );
    const form = document.getElementById("answer-filter");
    if (form)
      form.onsubmit = (e) => {
        e.preventDefault();
        go(`${pageName}?status=${new FormData(form).get("status")}`);
      };
    if (data.items.some((a) => a.status === "PendingAnalysis"))
      state.timer = setInterval(() => run(route), 4000);
  };
}
async function answerPage(id) {
  const a = await api(`answers/${id}/analysis`);
  return () => {
    if (student()) {
      main(
        heading("Результат відповіді", a.test, "", "МОЄ НАВЧАННЯ") +
          (!a.resultsVisible
            ? notice(
                "Викладач ще не відкрив результати",
                "Вашу відповідь збережено. Поверніться до цього розділу пізніше.",
              )
            : notice(
                "Результат оцінювання",
                a.comment ||
                  "Автоматичний результат є попереднім. Викладач може уточнити його.",
              )) +
          `<section class="card-panel"><div class="panel-header"><h2>${esc(a.question)}</h2>${badge(a.status)}</div><div class="panel-body"><div class="question-body">${esc(a.answer) || "Відповіді не надано"}</div>${a.resultsVisible ? `<div class="score-panel px-0"><div><div class="score-big">${a.score == null ? "—" : a.score.toFixed(1)}<small>${a.score == null ? "" : "%"}</small></div><div class="score-label">${a.points == null ? "Оцінка ще не визначена" : `${a.points.toFixed(1)} із ${a.maxPoints} балів`}</div></div><div>${a.decision ? badge(a.decision) : a.category ? badge(a.category) : ""}<p class="form-hint mt-3">${esc(a.comment || "")}</p></div></div>` : ""}</div></section>`,
      );
      return;
    }
    const analysis = a.analysis,
      d = analysis?.details;
    main(
      heading(
        "Перевірка відповіді",
        a.test,
        btn("Оновити", "refresh", "", "btn-light", "activity") +
          btn("Повторити аналіз", "answer-retry", id, "btn-light", "activity"),
        "ВІДПОВІДІ СТУДЕНТІВ",
      ) +
        `<div class="split-main"><div class="detail-stack"><section class="card-panel"><div class="panel-header"><h2>Питання</h2>${badge(a.status)}</div><div class="panel-body"><h3 class="lh-base mb-3">${esc(a.question)}</h3><div class="small-label mb-2">Відповідь студента</div><div class="question-body">${esc(a.answer) || "Відповіді не надано"}</div><div class="form-hint mt-3">${esc(date(a.createdAt))} · студент ${esc(a.studentId.slice(0, 8))}</div></div></section><section class="card-panel"><div class="panel-header"><div><h2>Лінгвістичний аналіз</h2><p>Попередня оцінка відповідності</p></div>${analysis ? badge(analysis.category) : badge("PendingAnalysis")}</div>${
          analysis
            ? `<div class="score-panel"><div><div class="score-big">${analysis.score == null ? "—" : analysis.score.toFixed(1)}<small>${analysis.score == null ? "" : "%"}</small></div><div class="score-label">Комплексний показник S</div></div><div class="score-bars">${[
                ["L · Лексична", analysis.lexical],
                ["M · Семантична", analysis.semantic],
                ["K · Повнота", analysis.keywords],
              ]
                .map(
                  ([l, v]) =>
                    `<div class="score-bar"><span>${l}</span><div class="bar-track"><div class="bar-fill" style="width:${v == null ? 0 : v * 100}%"></div></div><strong>${v == null ? "—" : Math.round(v * 100) + "%"}</strong></div>`,
                )
                .join(
                  "",
                )}</div></div><div class="panel-body pt-0">${d.semanticError ? notice("Потрібна ручна перевірка", d.semanticError, true) : ""}<h3>Пояснення результату</h3><ul class="reason-list">${d.reasons.map((r) => `<li>${esc(r)}</li>`).join("")}</ul>${d.warnings.map((w) => `<div class="notice warning mt-3 mb-0">${icon("alert")}<span>${esc(w)}</span></div>`).join("")}<div class="section-heading"><h3>Покриття понять</h3></div><div class="concept-chips">${d.concepts.map((k) => `<span class="concept-chip ${!k.found || k.negated ? "missing" : ""}">${k.found && !k.negated ? "✓" : "−"} ${esc(k.term)}${k.negated ? " · заперечення" : ""}</span>`).join("")}</div><p class="form-hint mt-3">Знайдені формулювання: ${
                d.concepts
                  .filter((k) => k.found)
                  .map((k) => `${esc(k.term)} → ${esc(k.matchedText)}`)
                  .join("; ") || "немає"
              }</p></div>`
            : empty(
                "Аналіз ще не завершено",
                "Оновіть сторінку за кілька секунд. У разі помилки запустіть аналіз повторно.",
              )
        }</section>${d?.matchedText ? `<section class="card-panel"><div class="panel-header"><h2>Вибраний еталон зі знімка спроби</h2></div><div class="panel-body"><div class="reference-block question-body">${esc(d.matchedText)}</div><p class="form-hint mt-3 mb-0">Версія питання ${d.questionRevision} · Налаштування v${d.settings.version} · Алгоритм ${esc(analysis.algorithmVersion)}</p><details class="mt-2"><summary class="form-hint">Версія семантичної моделі</summary><p class="form-hint text-break">${esc(analysis.modelVersion)}</p></details>${btn("Історія аналізу", "analysis-history", id, "btn-light btn-sm mt-3", "clock")}</div></section>` : ""}${
          a.reviews.length
            ? `<section class="card-panel"><div class="panel-header"><h2>Історія рішень викладача</h2></div><div class="panel-body">${a.reviews
                .slice()
                .reverse()
                .map(
                  (r) =>
                    `<div class="review-history">${badge(r.decision)} <strong>${percent(r.score)}</strong><p>${esc(r.comment)}</p><small>${esc(date(r.createdAt))} · ${esc(r.reviewerId.slice(0, 8))}</small></div>`,
                )
                .join("")}</div></section>`
            : ""
        }</div><aside><section class="card-panel"><div class="panel-header"><h2>Рішення викладача</h2>${icon("review")}</div><form id="review-form" class="panel-body review-form">${select(
          "Результат перевірки",
          "decision",
          [
            ["Accept", "Зарахувати · 100%"],
            ["Partial", "Частково зарахувати"],
            ["Reject", "Відхилити · 0%"],
            ["Defer", "Додаткова перевірка"],
          ]
            .map(([v, l]) => option(v, l))
            .join(""),
        )}${field("Підсумкова оцінка, %", "score", 100, "number", 'min="0" max="100" step="0.1" required readonly')}${area("Коментар до рішення", "comment", "", 'required minlength="3" maxlength="2000" placeholder="Обґрунтуйте оцінку…"')}<label class="d-flex gap-2 small muted mb-3"><input type="checkbox" name="createVariant" class="mt-1"> Створити з відповіді кандидата варіації</label><button class="btn btn-primary w-100" type="submit">${icon("check")}Зберегти рішення</button><p class="review-note">Кандидат потребуватиме окремого погодження. Попередні рішення зберігаються в історії.</p></form></section></aside></div>`,
    );
    const form = document.getElementById("review-form");
    form.querySelector("[name=decision]").onchange = (e) => {
      const decision = e.target.value,
        score = form.querySelector("[name=score]");
      score.readOnly = decision !== "Partial";
      score.required = decision !== "Defer";
      score.disabled = decision === "Defer";
      score.value =
        decision === "Accept"
          ? 100
          : decision === "Reject"
            ? 0
            : decision === "Partial"
              ? 60
              : "";
      const candidate = form.querySelector("[name=createVariant]");
      candidate.disabled = decision !== "Accept";
      if (candidate.disabled) candidate.checked = false;
    };
    form.onsubmit = (e) => {
      e.preventDefault();
      run(async () => {
        e.submitter.disabled = true;
        try {
          const data = new FormData(form);
          await api(`answers/${id}/review`, {
            method: "POST",
            body: {
              decision: data.get("decision"),
              score:
                data.get("decision") === "Defer"
                  ? null
                  : Number(data.get("score")),
              comment: data.get("comment"),
              createVariant: data.has("createVariant"),
            },
          });
          toast("Рішення збережено");
          route();
        } finally {
          e.submitter.disabled = false;
        }
      });
    };
  };
}
actions["answer-retry"] = async (id) => {
  await api(`answers/${id}/reanalyze`, { method: "POST" });
  toast("Відповідь поставлено в чергу аналізу");
  route();
};
actions["analysis-history"] = async (id) => {
  const runs = await api(`answers/${id}/history`);
  infoDialog(
    "Історія аналізу",
    runs
      .map(
        (r) =>
          `<div class="variant-item"><strong>${esc(date(r.createdAt))}</strong><p class="mb-1">S = ${percent(r.analysis.score)} · ${statusNames[r.analysis.category]}</p><small class="muted">Алгоритм ${esc(r.analysis.algorithmVersion)} · налаштування v${r.analysis.details.settings.version}</small></div>`,
      )
      .join("") ||
      empty("Запусків поки немає", "Історія з’явиться після аналізу."),
  );
};
function reportTable(rows) {
  return `<div class="table-wrap"><table class="table"><thead><tr><th>Дисципліна / тест</th><th>Група</th><th>Відповіді</th><th>На перевірці</th><th>Середній %</th><th>Високі</th><th>Часткові</th><th>Низькі</th></tr></thead><tbody>${rows.map((r) => `<tr><td class="primary-cell">${esc(r.discipline)}<span class="sub-cell">${esc(r.test)}</span></td><td>${esc(r.group)}</td><td>${r.answers}</td><td>${r.pending}</td><td><strong>${percent(r.average)}</strong></td><td>${r.high}</td><td>${r.partial}</td><td>${r.low}</td></tr>`).join("")}</tbody></table></div>`;
}
async function reportsPage(params) {
  const data = await api(`reports?${params}`);
  const tests = await api("tests?size=100");
  return () => {
    main(
      heading(
        "Статистика та звіти",
        "Узагальнені результати без персональних відповідей студентів.",
        `<a class="btn btn-light" href="/api/v1/reports/export?${params}&format=csv">${icon("download")}CSV</a><a class="btn btn-primary" href="/api/v1/reports/export?${params}&format=xlsx">${icon("download")}XLSX</a>`,
        "АНАЛІТИКА",
      ) +
        notice(
          "Оцінки є попередніми до перевірки викладачем",
          "Середній показник враховує відповіді з визначеним балом. Відповіді без семантичного результату очікують ручної оцінки. Студент із кількома групами може входити до звіту кожної групи.",
        ) +
        `<section class="card-panel"><form class="filters" id="report-filter"><select class="form-select" name="disciplineId" aria-label="Дисципліна">${option("", "Усі дисципліни")}${state.disciplines.map((d) => option(d.id, d.name, params.get("disciplineId") === d.id)).join("")}</select><select class="form-select" name="groupId" aria-label="Група">${option("", "Усі групи")}${state.groups.map((g) => option(g.id, g.name, params.get("groupId") === g.id)).join("")}</select><select class="form-select" name="testId" aria-label="Тест">${option("", "Усі тести")}${tests.items.map((t) => option(t.id, t.title, params.get("testId") === t.id)).join("")}</select><input class="form-control" style="width:150px" type="date" name="from" aria-label="Дата початку" value="${esc(params.get("from")?.slice(0, 10) || "")}"><input class="form-control" style="width:150px" type="date" name="to" aria-label="Дата завершення" value="${esc(params.get("to")?.slice(0, 10) || "")}"><button class="btn btn-light" type="submit">Застосувати</button></form>${data.length ? reportTable(data) : empty("Даних для звіту поки немає", "Змініть фільтри або дочекайтеся перших надісланих відповідей.")}</section>`,
    );
    document.getElementById("report-filter").onsubmit = (e) => {
      e.preventDefault();
      const form = new FormData(e.target),
        query = new URLSearchParams();
      for (const [key, value] of form) {
        if (value)
          query.set(
            key,
            key === "from"
              ? new Date(value + "T00:00:00").toISOString()
              : key === "to"
                ? new Date(value + "T23:59:59.999").toISOString()
                : value,
          );
      }
      go(`reports?${query}`);
    };
  };
}
async function settingsPage() {
  const [settings, history] = await Promise.all([
    api("settings"),
    api("settings/history"),
  ]);
  return () => {
    main(
      heading(
        "Налаштування аналізу",
        "Кожне збереження створює нову версію. Розпочаті спроби зберігають попередні параметри.",
        "",
        "КЕРУВАННЯ",
      ) +
        `<div class="split-main"><section class="card-panel"><div class="panel-header"><h2>Ваги та пороги оцінювання</h2><span class="badge-status blue">Версія ${settings.version}</span></div><form id="settings-form" class="panel-body"><div class="formula">S = 100 × (αL + βM + γK)</div><div class="settings-grid">${field("α · Лексична схожість", "alpha", settings.alpha, "number", 'step="0.01" min="0" max="1" required')}${field("β · Семантична близькість", "beta", settings.beta, "number", 'step="0.01" min="0.01" max="1" required')}${field("γ · Ключові поняття", "gamma", settings.gamma, "number", 'step="0.01" min="0" max="1" required')}</div><p id="weight-sum" class="form-hint mb-4">Сума коефіцієнтів: 1.00</p><div class="row"><div class="col-sm-6">${field("Поріг високої відповідності, %", "highThreshold", settings.highThreshold, "number", 'min="1" max="100" step="0.1" required')}</div><div class="col-sm-6">${field("Поріг часткової відповідності, %", "partialThreshold", settings.partialThreshold, "number", 'min="0.1" max="99.9" step="0.1" required')}</div></div>${notice("Початкова модель оцінювання", "Ваги й пороги потребують калібрування на перевірених викладачем відповідях. Висока подібність сама по собі не доводить правильність змісту.")}<button class="btn btn-primary" type="submit">${icon("check")}Зберегти нову версію</button></form></section><section class="card-panel"><div class="panel-header"><h2>Історія параметрів</h2>${icon("clock")}</div><div class="panel-body">${history.map((s) => `<div class="keyword-item"><strong>Версія ${s.version}</strong><small>α ${s.alpha} · β ${s.beta} · γ ${s.gamma}</small><span class="form-hint">Висока ≥ ${s.highThreshold}% · часткова ≥ ${s.partialThreshold}%</span></div>`).join("")}</div></section></div>`,
    );
    const form = document.getElementById("settings-form");
    form.addEventListener("input", () => {
      const sum = ["alpha", "beta", "gamma"].reduce(
        (s, k) => s + Number(form.elements[k].value),
        0,
      );
      const el = document.getElementById("weight-sum");
      el.textContent = `Сума коефіцієнтів: ${sum.toFixed(2)}${Math.abs(sum - 1) > 0.000001 ? " · має дорівнювати 1" : ""}`;
      el.style.color = Math.abs(sum - 1) > 0.000001 ? "#b05b65" : "#648f80";
    });
    form.onsubmit = (e) => {
      e.preventDefault();
      run(async () => {
        e.submitter.disabled = true;
        try {
          const body = { ...settings };
          for (const key of [
            "alpha",
            "beta",
            "gamma",
            "highThreshold",
            "partialThreshold",
          ])
            body[key] = Number(form.elements[key].value);
          await api("settings", { method: "PUT", body });
          toast("Нову версію налаштувань збережено");
          route();
        } finally {
          e.submitter.disabled = false;
        }
      });
    };
  };
}
async function usersPage(params) {
  const data = await api(`admin/users?${params}&size=20`);
  state.users = data.items;
  return () => {
    main(
      heading(
        "Користувачі системи",
        "Облікові записи, ролі та доступ до навчального простору.",
        btn("Додати користувача", "user-new", "", "btn-primary", "plus"),
        "АДМІНІСТРУВАННЯ",
      ) +
        `<section class="card-panel"><form class="filters" id="user-filter"><div class="search-field">${icon("search")}<input class="form-control" name="search" aria-label="Пошук користувача" placeholder="Ім’я або електронна пошта…" value="${esc(params.get("search") || "")}"></div><button class="btn btn-light" type="submit">Знайти</button></form><div class="table-wrap"><table class="table"><thead><tr><th>Користувач</th><th>Електронна пошта</th><th>Роль</th><th>Стан</th><th></th></tr></thead><tbody>${data.items.map((u) => `<tr><td class="primary-cell"><div class="d-flex align-items-center gap-3"><span class="avatar">${esc(initials(u.fullName))}</span>${esc(u.fullName)}</div></td><td>${esc(u.email)}</td><td>${u.roles.map(roleName).map(esc).join(", ")}</td><td>${badge(u.blocked ? "Rejected" : "Active")}</td><td>${btn("Редагувати", "user-edit", u.id, "btn-light btn-sm", "edit")}</td></tr>`).join("")}</tbody></table></div>${pager(data, "users" + (params.get("search") ? "?search=" + encodeURIComponent(params.get("search")) : ""))}</section>`,
    );
    document.getElementById("user-filter").onsubmit = (e) => {
      e.preventDefault();
      go(
        `users?search=${encodeURIComponent(new FormData(e.target).get("search"))}`,
      );
    };
  };
}
function userDialog(id) {
  const u = state.users?.find((u) => u.id === id);
  formDialog(
    u ? "Редагування облікового запису" : "Новий користувач",
    field(
      "Ім’я та прізвище",
      "fullName",
      u?.fullName || "",
      "text",
      'required minlength="2" maxlength="120"',
    ) +
      field("Електронна пошта", "email", u?.email || "", "email", "required") +
      select(
        "Роль",
        "role",
        ["Student", "Teacher", "Administrator"]
          .map((r) => option(r, roleName(r), u?.roles.includes(r)))
          .join(""),
      ) +
      field(
        u ? "Новий пароль (залиште порожнім, щоб зберегти)" : "Пароль",
        "password",
        "",
        "password",
        `${u ? "" : "required"} minlength="12" autocomplete="new-password"`,
      ) +
      `<p class="form-hint">Щонайменше 12 символів, великі й малі латинські літери, цифра та спеціальний символ.</p><label class="d-flex gap-2 small muted mt-3"><input type="checkbox" name="blocked" ${u?.blocked ? "checked" : ""}> Заблокувати доступ</label>`,
    async (form) => {
      await api(u ? `admin/users/${id}` : "admin/users", {
        method: u ? "PUT" : "POST",
        body: {
          fullName: form.get("fullName"),
          email: form.get("email"),
          role: form.get("role"),
          password: form.get("password") || null,
          blocked: form.has("blocked"),
        },
      });
      toast("Обліковий запис збережено");
      route();
    },
    { small: true },
  );
}
actions["user-new"] = () => userDialog();
actions["user-edit"] = (id) => userDialog(id);
async function groupsPage() {
  [state.groups, state.directory] = await Promise.all([
    api("groups"),
    api("directory"),
  ]);
  return () =>
    main(
      heading(
        "Навчальні групи",
        "Об’єднуйте студентів у групи та призначайте їх дисциплінам.",
        btn("Створити групу", "group-new", "", "btn-primary", "plus"),
        "АДМІНІСТРУВАННЯ",
      ) +
        `<div class="discipline-grid">${state.groups.map((g) => `<article class="discipline-card"><span class="feature-icon">${icon("users")}</span><h2>${esc(g.name)}</h2><p>${g.memberIds.length} студентів</p><div class="card-bottom">${badge(g.archived ? "Archived" : "Active")}<div class="d-flex gap-1">${btn("Склад групи", "group-members", g.id, "btn-light btn-sm")}<button class="icon-button" data-action="group-edit" data-id="${g.id}" aria-label="Редагувати групу">${icon("edit")}</button><button class="icon-button" data-action="group-archive" data-id="${g.id}" aria-label="Архівувати групу">${icon("archive")}</button></div></div></article>`).join("")}</div>` +
        (state.groups.length
          ? ""
          : empty(
              "Груп поки немає",
              "Створіть першу групу та додайте до неї студентів.",
            )),
    );
}
function groupDialog(id) {
  const g = state.groups.find((g) => g.id === id);
  formDialog(
    g ? "Назва навчальної групи" : "Нова навчальна група",
    field(
      "Назва групи",
      "name",
      g?.name || "",
      "text",
      'required minlength="2" maxlength="80"',
    ),
    async (form) => {
      await api(g ? `admin/groups/${id}` : "admin/groups", {
        method: g ? "PUT" : "POST",
        body: { name: form.get("name") },
      });
      toast("Групу збережено");
      route();
    },
    { small: true },
  );
}
actions["group-new"] = () => groupDialog();
actions["group-edit"] = (id) => groupDialog(id);
actions["group-members"] = (id) => {
  const g = state.groups.find((g) => g.id === id);
  formDialog(
    `Склад групи ${g.name}`,
    `<div class="test-question-list">${state.directory.students.map((s) => `<label class="test-question-option"><input type="checkbox" name="student" value="${s.id}" ${g.memberIds.includes(s.id) ? "checked" : ""}><span>${esc(s.fullName)}<small class="d-block muted">${esc(s.email)}</small></span></label>`).join("") || '<p class="panel-body muted">Спочатку створіть облікові записи студентів.</p>'}</div>`,
    async (form) => {
      await api(`admin/groups/${id}/members`, {
        method: "PUT",
        body: form.getAll("student"),
      });
      toast("Склад групи оновлено");
      route();
    },
  );
};
actions["group-archive"] = async (id) => {
  if (
    await confirmDialog(
      "Архівувати групу?",
      "Історичні дані збережуться. Групу не можна буде додати до нового тесту.",
      "Архівувати",
    )
  ) {
    await api(`admin/groups/${id}`, { method: "DELETE" });
    route();
  }
};
async function auditPage(params) {
  const data = await api(`admin/audit?page=${params.get("page") || 1}`);
  return () =>
    main(
      heading(
        "Журнал дій",
        "Історія критичних операцій без вмісту відповідей і паролів.",
        btn("Оновити", "refresh", "", "btn-light", "activity"),
        "АДМІНІСТРУВАННЯ",
      ) +
        `<section class="card-panel"><div class="table-wrap"><table class="table"><thead><tr><th>Подія</th><th>Об’єкт</th><th>Користувач</th><th>Дата</th></tr></thead><tbody>${data.items.map((a) => `<tr><td class="primary-cell">${esc(eventName(a.action))}<span class="sub-cell">${esc(a.action)}</span></td><td>${esc(a.entityType)}<span class="sub-cell">${esc(a.entityId)}</span></td><td>${esc(a.userId.slice(0, 8))}</td><td class="nowrap">${esc(date(a.createdAt))}</td></tr>`).join("")}</tbody></table></div>${pager(data, "audit")}</section>`,
    );
}
async function systemPage() {
  const h = await api("system/health");
  return () =>
    main(
      heading(
        "Технічний стан системи",
        "Стан бази даних і локального компонента семантичного аналізу.",
        btn("Перевірити", "refresh", "", "btn-light", "activity"),
        "АДМІНІСТРУВАННЯ",
      ) +
        `<div class="grid-equal"><section class="card-panel"><div class="panel-header"><h2>Компоненти</h2>${icon("activity")}</div><div class="panel-body"><div class="system-row"><span>PostgreSQL</span>${badge(h.database ? "Active" : "AnalysisError")}</div><div class="system-row"><span>ONNX Runtime · CPU</span>${badge(h.semantic.available ? "Active" : "Unavailable")}</div><div class="system-row"><span>Алгоритм</span><strong>${esc(h.algorithm)}</strong></div><div class="system-row"><span>Перевірено</span><span>${esc(date(h.utcNow))}</span></div></div></section><section class="card-panel"><div class="panel-header"><h2>Семантична модель</h2>${icon("shield")}</div><div class="panel-body"><p class="small muted">${esc(h.semantic.message)}</p><p class="form-hint text-break">${esc(h.semantic.modelVersion)}</p>${!h.semantic.available ? '<p class="form-hint">Виконайте <code>python3 scripts/download-model.py</code> у корені проєкту й перезапустіть API. Великі файли моделі виключені з Git.</p>' : ""}<a class="btn btn-light btn-sm" href="/swagger" target="_blank" rel="noopener">OpenAPI / Swagger ${icon("arrow")}</a><p class="form-hint mt-2">Swagger доступний у середовищі Development.</p></div></section></div>`,
    );
}

async function boot() {
  try {
    await refreshCsrf();
    state.user = await api("auth/me");
    await enterWorkspace();
  } catch (error) {
    if (error.status === 401) await loginPage();
    else {
      app.innerHTML = `<div class="initial-loading"><div>${empty("Не вдалося підключитися", error.message, btn("Повторити", "boot", "", "btn-primary"))}</div></div>`;
    }
  }
}
actions.boot = boot;
await boot();
