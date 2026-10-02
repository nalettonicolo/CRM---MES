// Applies the company visual theme as CSS variables on <html>. Loaded under CSP (same origin only).
window.nicolomes = window.nicolomes || {};
window.nicolomes.applyTheme = function (cssVariables, backgroundStyle) {
  const root = document.documentElement;
  if (!cssVariables || typeof cssVariables !== "object") {
    root.removeAttribute("data-company-theme");
    root.removeAttribute("data-bg-style");
    return;
  }
  root.setAttribute("data-company-theme", "1");
  for (const [key, value] of Object.entries(cssVariables)) {
    if (typeof key === "string" && key.startsWith("--") && typeof value === "string") {
      root.style.setProperty(key, value);
    }
  }
  if (backgroundStyle) {
    root.setAttribute("data-bg-style", backgroundStyle);
  }
};
