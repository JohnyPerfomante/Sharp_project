let csrf;
export async function refreshCsrf() {
  const r = await fetch("/api/v1/auth/csrf", {
    credentials: "same-origin",
    cache: "no-store",
  });
  if (!r.ok) throw new Error("Не вдалося встановити захищене з’єднання.");
  csrf = (await r.json()).token;
}
export async function api(path, { method = "GET", body, ...options } = {}) {
  if (method !== "GET" && !csrf) await refreshCsrf();
  const response = await fetch(`/api/v1/${path}`, {
    ...options,
    method,
    credentials: "same-origin",
    cache: "no-store",
    headers: {
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(method !== "GET" ? { "X-CSRF-TOKEN": csrf } : {}),
    },
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  });
  if (!response.ok) {
    let data;
    try {
      data = await response.json();
    } catch {
      data = {};
    }
    const error = new Error(
      response.status === 401
        ? "Сесію завершено або облікові дані невірні. Увійдіть знову."
        : response.status === 403
          ? "Для цієї дії недостатньо прав."
          : response.status === 429
            ? "Забагато запитів. Повторіть вхід за хвилину."
            : data.errors
              ? Object.values(data.errors).flat().join(" ")
              : data.title || `Не вдалося виконати запит (${response.status}).`,
    );
    error.status = response.status;
    throw error;
  }
  return response.status === 204 ||
    response.headers.get("content-length") === "0"
    ? null
    : response.headers.get("content-type")?.includes("json")
      ? response.json()
      : null;
}
