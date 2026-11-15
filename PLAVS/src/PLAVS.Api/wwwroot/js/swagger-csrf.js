// Swagger's embedded UI performs same-origin fetches; refresh the identity-bound token after every login.
(() => {
  const originalFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const url = new URL(
      typeof input === "string" ? input : input.url,
      location.origin,
    );
    const method = (
      init.method || (input instanceof Request ? input.method : "GET")
    ).toUpperCase();
    if (
      url.origin === location.origin &&
      url.pathname.startsWith("/api/") &&
      !["GET", "HEAD", "OPTIONS"].includes(method)
    ) {
      const response = await originalFetch("/api/v1/auth/csrf", {
        credentials: "same-origin",
      });
      const data = await response.json();
      const headers = new Headers(
        init.headers || (input instanceof Request ? input.headers : undefined),
      );
      headers.set("X-CSRF-TOKEN", data.token);
      init = { ...init, headers, credentials: "same-origin" };
    }
    return originalFetch(input, init);
  };
})();
