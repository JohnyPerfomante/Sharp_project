export const esc = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
export const icon = (name) =>
  `<svg class="icon" aria-hidden="true"><use href="/icons.svg#${esc(name)}"></use></svg>`;
export const date = (value) =>
  value
    ? new Intl.DateTimeFormat("uk-UA", {
        day: "2-digit",
        month: "short",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      }).format(new Date(value))
    : "Не обмежено";
export const percent = (value) =>
  value == null ? "—" : `${Number(value).toFixed(1)}%`;
export const initials = (name) =>
  name
    .split(/\s+/)
    .slice(0, 2)
    .map((s) => s[0])
    .join("")
    .toUpperCase();
export const roleName = (role) =>
  ({ Administrator: "Адміністратор", Teacher: "Викладач", Student: "Студент" })[
    role
  ] || role;
export const statusNames = {
  Active: "Активне",
  Draft: "Чернетка",
  Archived: "Архівне",
  Pending: "На погодженні",
  Approved: "Затверджено",
  Rejected: "Відхилено",
  Inactive: "Неактивна",
  PendingAnalysis: "Очікує аналізу",
  NeedsReview: "Потрібна перевірка",
  Analyzed: "Проаналізовано",
  Reviewed: "Перевірено",
  AnalysisError: "Помилка аналізу",
  High: "Висока",
  Partial: "Часткова",
  Low: "Низька",
  Unavailable: "Без оцінки",
  InProgress: "У процесі",
  Submitted: "Надіслано",
  Accept: "Зараховано",
  Reject: "Відхилено",
  Defer: "Додаткова перевірка",
};
export const badge = (status) =>
  `<span class="badge-status ${{ Active: "green", Approved: "green", Reviewed: "green", Analyzed: "blue", High: "green", Partial: "amber", Low: "red", Pending: "amber", NeedsReview: "amber", AnalysisError: "red", Rejected: "red", Reject: "red", Accept: "green", Submitted: "blue" }[status] || ""}">${esc(statusNames[status] || status)}</span>`;
export const heading = (
  title,
  subtitle,
  actions = "",
  eyebrow = "РОБОЧИЙ ПРОСТІР",
) =>
  `<div class="page-heading"><div><div class="eyebrow">${esc(eyebrow)}</div><h1>${esc(title)}</h1><p>${esc(subtitle)}</p></div><div class="heading-actions">${actions}</div></div>`;
export const empty = (title, detail, action = "") =>
  `<div class="empty">${icon("layers")}<h3>${esc(title)}</h3><p>${esc(detail)}</p>${action}</div>`;
export const notice = (title, text, warning = false) =>
  `<div class="notice ${warning ? "warning" : ""}">${icon(warning ? "alert" : "shield")}<div><strong>${esc(title)}</strong><p>${esc(text)}</p></div></div>`;
export const btn = (
  label,
  action,
  id = "",
  style = "btn-primary",
  symbol = "",
) =>
  `<button type="button" class="btn ${style}" data-action="${esc(action)}" data-id="${esc(id)}">${symbol ? icon(symbol) : ""}${esc(label)}</button>`;
export const field = (label, name, value = "", type = "text", extra = "") =>
  `<div class="field"><label class="form-label" for="${esc(name)}">${esc(label)}</label><input class="form-control" id="${esc(name)}" name="${esc(name)}" type="${type}" value="${esc(value)}" ${extra}></div>`;
export const area = (label, name, value = "", extra = "") =>
  `<div class="field"><label class="form-label" for="${esc(name)}">${esc(label)}</label><textarea class="form-control" id="${esc(name)}" name="${esc(name)}" ${extra}>${esc(value)}</textarea></div>`;
export const option = (value, label, selected = false) =>
  `<option value="${esc(value)}" ${selected ? "selected" : ""}>${esc(label)}</option>`;
export const select = (label, name, options, extra = "") =>
  `<div class="field"><label class="form-label" for="${esc(name)}">${esc(label)}</label><select class="form-select" id="${esc(name)}" name="${esc(name)}" ${extra}>${options}</select></div>`;
export function toast(message, error = false) {
  const node = document.createElement("div");
  node.className = `app-toast ${error ? "error" : ""}`;
  node.textContent = message;
  document.getElementById("toast-region").append(node);
  setTimeout(() => node.remove(), 6000);
}
export function formDialog(
  title,
  body,
  onSubmit,
  { wide = false, small = false, submitLabel = "Зберегти", ready } = {},
) {
  const modal = document.getElementById("modal");
  if (modal.open) modal.close();
  modal.className = small ? "modal-small" : "";
  modal.innerHTML = `<form id="modal-form"><div class="modal-header"><h2 id="modal-title">${esc(title)}</h2><button class="icon-button" type="button" data-action="close-dialog" aria-label="Закрити">${icon("close")}</button></div><div class="modal-body"><div id="form-error" role="alert"></div>${body}</div><div class="modal-footer"><button type="button" class="btn btn-light" data-action="close-dialog">Скасувати</button><button type="submit" class="btn btn-primary">${esc(submitLabel)}</button></div></form>`;
  modal.querySelector("form").addEventListener("submit", async (e) => {
    e.preventDefault();
    const button = e.submitter;
    button.disabled = true;
    modal.querySelector("#form-error").innerHTML = "";
    try {
      await onSubmit(new FormData(e.target), e.target);
      modal.close();
    } catch (error) {
      modal.querySelector("#form-error").innerHTML =
        `<div class="error-box">${esc(error.message)}</div>`;
      modal
        .querySelector(".modal-body")
        .scrollTo({ top: 0, behavior: "smooth" });
    } finally {
      button.disabled = false;
    }
  });
  modal.showModal();
  ready?.(modal.querySelector("form"));
  return modal;
}
export function confirmDialog(title, text, label = "Підтвердити") {
  return new Promise((resolve) => {
    const modal = formDialog(
      title,
      `<p class="muted">${esc(text)}</p>`,
      async () => resolve(true),
      { small: true, submitLabel: label },
    );
    modal.addEventListener("close", () => resolve(false), { once: true });
  });
}
export function infoDialog(title, body) {
  const modal = formDialog(title, body, async () => {}, {
    submitLabel: "Готово",
  });
  modal.querySelector(".modal-footer .btn-light").remove();
}
export function pager(data, path) {
  const last = Math.max(1, Math.ceil(data.total / data.pageSize));
  return `<div class="pagination-bar"><span>Усього: ${data.total} · Сторінка ${data.pageNumber} з ${last}</span><div class="pagination-controls"><a class="btn btn-light btn-sm ${data.pageNumber <= 1 ? "disabled" : ""}" href="#/${path}${path.includes("?") ? "&" : "?"}page=${data.pageNumber - 1}">Назад</a><a class="btn btn-light btn-sm ${data.pageNumber >= last ? "disabled" : ""}" href="#/${path}${path.includes("?") ? "&" : "?"}page=${data.pageNumber + 1}">Далі</a></div></div>`;
}
