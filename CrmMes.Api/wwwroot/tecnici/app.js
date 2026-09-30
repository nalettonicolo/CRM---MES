// Rapportini di cantiere for technicians' phones and tablets. Talks only to this same API (the page is
// served by it), keeps the session token in sessionStorage (gone when the tab closes) and never builds
// HTML from data: every value goes through textContent, so nothing typed by a user can inject markup.
(() => {
  "use strict";

  const state = { token: null, refreshToken: null, name: "", userId: null, status: "Draft", report: null, workOrder: null, pickMode: "report" };
  let refreshing = null;
  const $ = (id) => document.getElementById(id);
  const views = ["login", "list", "pick", "edit", "sign", "show", "phases", "hours"];
  let history = [];

  // ---------- helpers
  function show(view, remember = true) {
    const current = views.find((v) => !$(`view-${v}`).hidden);
    if (remember && current && current !== view) history.push(current);
    views.forEach((v) => { $(`view-${v}`).hidden = v !== view; });
    window.scrollTo(0, 0);
  }

  function back() {
    const previous = history.pop() || "list";
    show(previous, false);
    if (previous === "list") loadList();
  }

  function el(tag, props = {}, ...children) {
    const node = document.createElement(tag);
    Object.entries(props).forEach(([key, value]) => {
      if (key === "class") node.className = value;
      else if (key === "text") node.textContent = value;
      else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
      else node.setAttribute(key, value);
    });
    children.filter(Boolean).forEach((child) => node.append(child));
    return node;
  }

  const dateFmt = new Intl.DateTimeFormat("it-IT", { day: "2-digit", month: "2-digit", year: "numeric" });
  const formatDate = (iso) => (iso ? dateFmt.format(new Date(iso)) : "");
  const todayIso = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);

  function formatMinutes(minutes) {
    const total = Math.round(Number(minutes) || 0);
    return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
  }

  // "1:30" or "1,5" / "1.5" hours -> minutes; null when not a positive duration.
  function parseDuration(text) {
    const value = String(text || "").trim();
    let minutes;
    const hm = /^(\d{1,2}):([0-5]\d)$/.exec(value);
    if (hm) minutes = Number(hm[1]) * 60 + Number(hm[2]);
    else if (/^\d+([.,]\d+)?$/.test(value)) minutes = Math.round(parseFloat(value.replace(",", ".")) * 60);
    return minutes > 0 && minutes <= 1440 ? minutes : null;
  }

  function parseQuantity(text) {
    const value = String(text || "").trim().replace(/\./g, (m, i, s) => (s.includes(",") ? "" : m)).replace(",", ".");
    const number = Number(value);
    return value !== "" && Number.isFinite(number) && number > 0 ? number : null;
  }

  // The access token lasts 30 minutes, a site visit much longer: on a 401 the refresh token (30 days,
  // rotated at every use) gets a new pair once, then the request is retried. Only if that fails too
  // does the technician have to sign in again, and the report being written is kept locally anyway.
  async function refreshSession() {
    if (!state.refreshToken) return false;
    refreshing ??= (async () => {
      try {
        const response = await fetch("/api/auth/refresh", {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({ refreshToken: state.refreshToken }),
        });
        if (!response.ok) return false;
        const auth = await response.json();
        setSession(auth.token, auth.refreshToken, auth.name, auth.userId);
        return true;
      } catch {
        return false;
      } finally {
        refreshing = null;
      }
    })();
    return refreshing;
  }

  async function api(path, options = {}, retried = false) {
    const headers = { Accept: "application/json" };
    if (options.body !== undefined) headers["Content-Type"] = "application/json";
    if (state.token) headers.Authorization = `Bearer ${state.token}`;
    const response = await fetch(path, {
      method: options.method || "GET",
      headers,
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    });
    if (response.status === 401 && state.token) {
      if (!retried && await refreshSession()) return api(path, options, true);
      logout("Sessione scaduta: accedi di nuovo. Il rapportino in corso è stato conservato.");
      throw new Error("Sessione scaduta.");
    }
    if (!response.ok) {
      let message = `Errore ${response.status}`;
      try {
        const data = await response.json();
        message = data.message || data.title || message;
      } catch { /* not JSON */ }
      throw new Error(message);
    }
    return response.status === 204 ? null : response.json();
  }

  // ---------- session
  function setSession(token, refreshToken, name, userId) {
    state.token = token;
    state.refreshToken = refreshToken;
    state.name = name;
    if (userId) state.userId = userId;
    try {
      sessionStorage.setItem("crmmes.token", token);
      sessionStorage.setItem("crmmes.refresh", refreshToken || "");
      sessionStorage.setItem("crmmes.name", name);
      if (state.userId) sessionStorage.setItem("crmmes.user", state.userId);
    } catch { /* private mode: session lives in memory only */ }
    $("who").textContent = name;
    $("logout").hidden = false;
    $("main-nav").hidden = false;
  }

  function logout(message) {
    state.token = null;
    state.refreshToken = null;
    try {
      ["crmmes.token", "crmmes.refresh", "crmmes.name", "crmmes.user"].forEach((key) => sessionStorage.removeItem(key));
    } catch { /* ignore */ }
    $("who").textContent = "";
    $("logout").hidden = true;
    $("main-nav").hidden = true;
    history = [];
    $("login-error").textContent = message || "";
    show("login", false);
  }

  $("login-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = event.submitter;
    $("login-error").textContent = "";
    button.disabled = true;
    try {
      const auth = await api("/api/auth/login", {
        method: "POST",
        body: { email: $("login-email").value.trim(), password: $("login-password").value },
      });
      $("login-password").value = "";
      setSession(auth.token, auth.refreshToken, auth.name, auth.userId);
      show("list", false);
      loadList();
    } catch (error) {
      $("login-error").textContent = error.message;
    } finally {
      button.disabled = false;
    }
  });
  $("logout").addEventListener("click", () => logout());
  document.querySelectorAll("[data-back]").forEach((button) => button.addEventListener("click", back));

  // ---------- list
  document.querySelectorAll(".tab").forEach((tab) => tab.addEventListener("click", () => {
    document.querySelectorAll(".tab").forEach((t) => {
      t.classList.toggle("active", t === tab);
      t.setAttribute("aria-selected", String(t === tab));
    });
    state.status = tab.dataset.status;
    loadList();
  }));

  async function loadList() {
    const list = $("report-list");
    const info = $("list-state");
    list.replaceChildren();
    info.className = "state";
    info.textContent = "Caricamento...";
    try {
      const reports = await api(`/api/site-reports?status=${encodeURIComponent(state.status)}`);
      info.textContent = reports.length ? "" : state.status === "Draft" ? "Nessun rapportino da firmare. Tocca Nuovo per iniziarne uno." : "Nessun rapportino firmato.";
      reports.forEach((report) => list.append(el("li", {},
        el("button", { class: "card", type: "button", onclick: () => openReport(report.id) },
          el("span", { class: "title", text: `${report.code} · ${report.workOrderCode}` }),
          el("span", { class: "meta", text: `${formatDate(report.workDate)} · ${report.customerName || "cliente non indicato"} · ${formatMinutes(report.totalMinutes)} h` }),
          el("span", { class: `badge ${report.status === "Signed" ? "signed" : "draft"}`, text: report.status === "Signed" ? "Firmato" : "Bozza" })))));
    } catch (error) {
      info.className = "state error-state";
      info.textContent = error.message;
    }
  }

  // ---------- work order picker
  let searchTimer;
  function openPicker(mode) {
    state.pickMode = mode;
    $("pick-title").textContent = { report: "Nuovo rapportino", phases: "Fasi: scegli la commessa", hours: "Ore: scegli la commessa" }[mode];
    $("pick-search").value = "";
    show("pick", mode === "report");
    loadWorkOrders();
  }

  $("new-report").addEventListener("click", () => openPicker("report"));

  // ---------- sections
  document.querySelectorAll(".nav-item").forEach((item) => item.addEventListener("click", () => {
    document.querySelectorAll(".nav-item").forEach((i) => i.classList.toggle("active", i === item));
    history = [];
    if (item.dataset.section === "reports") {
      show("list", false);
      loadList();
    } else {
      openPicker(item.dataset.section);
    }
  }));
  $("pick-search").addEventListener("input", () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(loadWorkOrders, 300);
  });

  async function loadWorkOrders() {
    const list = $("pick-list");
    const info = $("pick-state");
    list.replaceChildren();
    info.className = "state";
    info.textContent = "Caricamento...";
    try {
      const q = $("pick-search").value.trim();
      const orders = await api(`/api/site-reports/open-work-orders${q ? `?q=${encodeURIComponent(q)}` : ""}`);
      info.textContent = orders.length ? "" : "Nessuna commessa aperta trovata.";
      orders.forEach((order) => list.append(el("li", {},
        el("button", { class: "card", type: "button", onclick: () => pickWorkOrder(order) },
          el("span", { class: "title", text: `${order.code} · ${order.customerName || "cliente non indicato"}` }),
          el("span", { class: "meta", text: [order.productName, order.customerAddress].filter(Boolean).join(" · ") })))));
    } catch (error) {
      info.className = "state error-state";
      info.textContent = error.message;
    }
  }

  function pickWorkOrder(order) {
    state.workOrder = order;
    if (state.pickMode === "phases") openPhases(order);
    else if (state.pickMode === "hours") openHours(order);
    else startReport(order);
  }

  // ---------- phases (same rules as the shop floor terminal: only a released job, one phase at a time)
  const phaseStatus = { Pending: ["pending", "Da fare"], InProgress: ["running", "In corso"], Completed: ["signed", "Completata"] };

  async function openPhases(order, remember = true) {
    $("phases-title").textContent = order.code;
    $("phases-sub").textContent = [order.customerName, order.productName].filter(Boolean).join(" · ");
    show("phases", remember);
    const list = $("phases-list");
    const info = $("phases-state");
    list.replaceChildren();
    info.className = "state";
    info.textContent = "Caricamento...";
    try {
      const workOrder = await api(`/api/work-orders/${order.id}`);
      info.textContent = workOrder.operations.length ? "" : "La commessa non ha fasi di lavoro.";
      if (!["Released", "InProgress"].includes(workOrder.status)) {
        info.textContent = "La commessa non è ancora rilasciata: le fasi si avviano dopo il rilascio in ufficio.";
      }
      workOrder.operations.forEach((operation) => {
        const [css, label] = phaseStatus[operation.status] || ["pending", operation.status];
        const card = el("li", { class: "phase" },
          el("span", { class: "title", text: `${operation.sequenceNumber}. ${operation.name}` }),
          el("span", { class: "meta", text: [operation.workCenter, `stimati ${Math.round(operation.estimatedMinutes)} min`].filter(Boolean).join(" · ") }),
          el("span", { class: `badge ${css}`, text: label }));
        const action = operation.status === "Pending" ? "start" : operation.status === "InProgress" ? "complete" : null;
        if (action && ["Released", "InProgress"].includes(workOrder.status)) {
          card.append(el("button", {
            class: action === "start" ? "secondary" : "primary", type: "button",
            text: action === "start" ? "Avvia fase" : "Completa fase",
            onclick: async (event) => {
              event.currentTarget.disabled = true;
              try {
                const query = new URLSearchParams({ operatorName: state.name });
                if (state.userId) query.set("operatorId", state.userId);
                await api(`/api/work-orders/${order.id}/operations/${operation.id}/${action}?${query}`, { method: "POST" });
                openPhases(order, false);
              } catch (error) {
                info.className = "state error-state";
                info.textContent = error.message;
                event.currentTarget.disabled = false;
              }
            },
          }));
        }
        list.append(card);
      });
    } catch (error) {
      info.className = "state error-state";
      info.textContent = error.message;
    }
  }

  // ---------- hours
  function openHours(order) {
    $("hours-title").textContent = `Ore · ${order.code}`;
    $("hours-sub").textContent = [order.customerName, order.productName].filter(Boolean).join(" · ");
    $("hours-date").value = todayIso();
    $("hours-value").value = "";
    $("hours-notes").value = "";
    $("hours-error").textContent = "";
    $("hours-ok").textContent = "";
    show("hours");
  }

  $("hours-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = event.submitter;
    $("hours-error").textContent = "";
    $("hours-ok").textContent = "";
    const minutes = parseDuration($("hours-value").value);
    if (minutes === null) {
      $("hours-error").textContent = "Ore non valide: scrivi per esempio 1:30 oppure 1,5.";
      return;
    }
    button.disabled = true;
    try {
      await api(`/api/work-orders/${state.workOrder.id}/labor`, {
        method: "POST",
        body: { minutes, workDate: $("hours-date").value || todayIso(), workCenterId: null, operationId: null, notes: $("hours-notes").value.trim() || null },
      });
      $("hours-ok").textContent = `Registrate ${formatMinutes(minutes)} ore su ${state.workOrder.code}.`;
      $("hours-value").value = "";
      $("hours-notes").value = "";
    } catch (error) {
      $("hours-error").textContent = error.message;
    } finally {
      button.disabled = false;
    }
  });

  function startReport(order) {
    state.report = null;
    state.workOrder = order;
    fillEditor({
      workDate: todayIso(), siteAddress: order.customerAddress || "", description: "", notes: "",
      hours: [{ technicianName: state.name, minutes: null }], materials: [],
    });
    $("edit-title").textContent = "Nuovo rapportino";
    $("edit-sub").textContent = `${order.code} · ${order.customerName || ""}`;
    $("delete-draft").hidden = true;
    restoreLocalCopy();
    show("edit");
  }

  // ---------- editor
  function hoursLine(line = {}) {
    const row = el("div", { class: "line" },
      el("label", {}, "Tecnico", el("input", { type: "text", value: line.technicianName || "", "data-field": "technician" })),
      el("label", {}, "Ore", el("input", { type: "text", inputmode: "decimal", value: line.minutes ? formatMinutes(line.minutes) : "", placeholder: "1:30", "data-field": "hours" })),
      el("button", { class: "remove", type: "button", "aria-label": "Rimuovi riga", text: "×", onclick: () => row.remove() }));
    $("hours-rows").append(row);
  }

  function materialLine(line = {}) {
    const row = el("div", { class: "line material" },
      el("label", {}, "Codice", el("input", { type: "text", value: line.materialCode || "", "data-field": "code" })),
      el("label", { class: "desc" }, "Descrizione", el("input", { type: "text", value: line.description || "", "data-field": "description" })),
      el("label", {}, "Q.tà", el("input", { type: "text", inputmode: "decimal", value: line.quantity != null ? String(line.quantity).replace(".", ",") : "", "data-field": "quantity" })),
      el("label", {}, "U.m.", el("input", { type: "text", value: line.unit || "pz", "data-field": "unit" })),
      el("button", { class: "remove", type: "button", "aria-label": "Rimuovi riga", text: "×", onclick: () => row.remove() }));
    $("material-rows").append(row);
  }

  $("add-hours").addEventListener("click", () => hoursLine({ technicianName: "" }));
  $("add-material").addEventListener("click", () => materialLine());

  // ---------- local copy of the report being written (survives a reload or an expired session)
  const draftKey = () => `crmmes.draft.${state.report ? state.report.id : `new-${state.workOrder ? state.workOrder.id : ""}`}`;

  function rawEditor() {
    const rows = (container) => [...container.children].map((row) =>
      Object.fromEntries([...row.querySelectorAll("[data-field]")].map((input) => [input.dataset.field, input.value])));
    return {
      savedAt: Date.now(),
      workDate: $("edit-date").value, siteAddress: $("edit-address").value,
      description: $("edit-description").value, notes: $("edit-notes").value,
      hours: rows($("hours-rows")), materials: rows($("material-rows")),
    };
  }

  function keepLocalCopy() {
    try { localStorage.setItem(draftKey(), JSON.stringify(rawEditor())); } catch { /* storage unavailable */ }
  }

  function dropLocalCopy(key = draftKey()) {
    try { localStorage.removeItem(key); } catch { /* ignore */ }
  }

  function restoreLocalCopy() {
    let copy = null;
    try { copy = JSON.parse(localStorage.getItem(draftKey()) || "null"); } catch { copy = null; }
    if (!copy) return false;
    $("edit-date").value = copy.workDate || todayIso();
    $("edit-address").value = copy.siteAddress || "";
    $("edit-description").value = copy.description || "";
    $("edit-notes").value = copy.notes || "";
    $("hours-rows").replaceChildren();
    $("material-rows").replaceChildren();
    (copy.hours || []).forEach((row) => {
      hoursLine({ technicianName: row.technician });
      $("hours-rows").lastElementChild.querySelector('[data-field="hours"]').value = row.hours || "";
    });
    (copy.materials || []).forEach((row) => materialLine({ materialCode: row.code, description: row.description, quantity: row.quantity || null, unit: row.unit }));
    $("edit-error").textContent = "";
    $("edit-sub").textContent += " · ripristinate le modifiche non salvate";
    return true;
  }

  $("edit-form").addEventListener("input", keepLocalCopy);
  $("hours-rows").addEventListener("click", () => setTimeout(keepLocalCopy));
  $("material-rows").addEventListener("click", () => setTimeout(keepLocalCopy));

  function fillEditor(report) {
    $("edit-date").value = (report.workDate || todayIso()).slice(0, 10);
    $("edit-address").value = report.siteAddress || "";
    $("edit-description").value = report.description || "";
    $("edit-notes").value = report.notes || "";
    $("hours-rows").replaceChildren();
    $("material-rows").replaceChildren();
    (report.hours.length ? report.hours : [{ technicianName: state.name }]).forEach(hoursLine);
    report.materials.forEach(materialLine);
    $("edit-error").textContent = "";
  }

  function readEditor() {
    const field = (row, name) => row.querySelector(`[data-field="${name}"]`).value.trim();
    const hours = [];
    for (const row of $("hours-rows").children) {
      const technician = field(row, "technician");
      const text = field(row, "hours");
      if (!technician && !text) continue;
      const minutes = parseDuration(text);
      if (!technician || minutes === null) throw new Error("Ogni riga ore vuole il tecnico e le ore (es. 1:30 o 1,5).");
      hours.push({ technicianName: technician, minutes });
    }
    const materials = [];
    for (const row of $("material-rows").children) {
      const code = field(row, "code");
      const description = field(row, "description");
      const quantityText = field(row, "quantity");
      if (!code && !description && !quantityText) continue;
      const quantity = parseQuantity(quantityText);
      if ((!code && !description) || quantity === null) throw new Error("Ogni materiale vuole codice o descrizione e una quantità.");
      materials.push({ materialCode: code || null, description: description || null, quantity, unit: field(row, "unit") || "pz" });
    }
    return {
      workOrderId: state.report ? state.report.workOrderId : state.workOrder.id,
      workDate: $("edit-date").value || todayIso(),
      siteAddress: $("edit-address").value.trim() || null,
      description: $("edit-description").value.trim(),
      notes: $("edit-notes").value.trim() || null,
      hours,
      materials,
    };
  }

  async function saveDraft() {
    const body = readEditor();
    const keyBefore = draftKey();
    state.report = state.report
      ? await api(`/api/site-reports/${state.report.id}`, { method: "PUT", body })
      : await api("/api/site-reports", { method: "POST", body });
    $("edit-title").textContent = state.report.code;
    $("delete-draft").hidden = false;
    dropLocalCopy(keyBefore);
    dropLocalCopy();
    return state.report;
  }

  $("edit-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = event.submitter;
    $("edit-error").textContent = "";
    button.disabled = true;
    try {
      await saveDraft();
      $("edit-error").textContent = "";
      back();
    } catch (error) {
      $("edit-error").textContent = error.message;
    } finally {
      button.disabled = false;
    }
  });

  $("delete-draft").addEventListener("click", async () => {
    if (!state.report || !window.confirm("Eliminare questa bozza?")) return;
    try {
      await api(`/api/site-reports/${state.report.id}`, { method: "DELETE" });
      dropLocalCopy();
      back();
    } catch (error) {
      $("edit-error").textContent = error.message;
    }
  });

  $("go-sign").addEventListener("click", async (event) => {
    const button = event.currentTarget;
    $("edit-error").textContent = "";
    if (!$("edit-description").value.trim()) {
      $("edit-error").textContent = "Descrivi i lavori eseguiti prima della firma.";
      return;
    }
    button.disabled = true;
    try {
      const report = await saveDraft();
      const minutes = report.hours.reduce((sum, h) => sum + Number(h.minutes), 0);
      $("sign-summary").textContent = `${report.code} · ${report.workOrderCode} · ${formatDate(report.workDate)} · ${formatMinutes(minutes)} h · ${report.materials.length} materiali`;
      $("sign-name").value = "";
      $("sign-error").textContent = "";
      show("sign");
      pad.reset();
    } catch (error) {
      $("edit-error").textContent = error.message;
    } finally {
      button.disabled = false;
    }
  });

  async function openReport(id) {
    try {
      const report = await api(`/api/site-reports/${id}`);
      state.report = report;
      if (report.status === "Signed") {
        renderSigned(report);
        show("show");
      } else {
        fillEditor(report);
        $("edit-title").textContent = report.code;
        $("edit-sub").textContent = `${report.workOrderCode} · ${report.customerName || ""}`;
        $("delete-draft").hidden = false;
        restoreLocalCopy();
        show("edit");
      }
    } catch (error) {
      $("list-state").className = "state error-state";
      $("list-state").textContent = error.message;
    }
  }

  function renderSigned(report) {
    $("show-title").textContent = report.code;
    const body = $("show-body");
    const kv = el("dl", { class: "kv" });
    [["Commessa", report.workOrderCode], ["Cliente", report.customerName], ["Data", formatDate(report.workDate)],
      ["Cantiere", report.siteAddress], ["Firmato da", report.signedByName], ["Firmato il", formatDate(report.signedAt)]]
      .forEach(([key, value]) => kv.append(el("dt", { text: key }), el("dd", { text: value || "-" })));
    const hours = el("ul", { class: "list-plain" });
    report.hours.forEach((h) => hours.append(el("li", { text: `${h.technicianName}: ${formatMinutes(h.minutes)} h` })));
    const materials = el("ul", { class: "list-plain" });
    report.materials.forEach((m) => materials.append(el("li", { text: `${String(m.quantity).replace(".", ",")} ${m.unit} ${m.description}${m.materialCode ? ` (${m.materialCode})` : ""}` })));
    const image = el("img", { class: "signature-img", alt: "Firma del cliente" });
    if (report.signatureImage && report.signatureImage.startsWith("data:image/png;base64,")) image.src = report.signatureImage;
    body.replaceChildren(kv,
      el("div", {}, el("h2", { text: "Lavori eseguiti" }), el("p", { text: report.description })),
      el("div", {}, el("h2", { text: "Ore" }), report.hours.length ? hours : el("p", { class: "muted", text: "Nessuna" })),
      el("div", {}, el("h2", { text: "Materiali" }), report.materials.length ? materials : el("p", { class: "muted", text: "Nessuno" })),
      image);
  }

  // ---------- signature pad
  const pad = (() => {
    const canvas = $("sign-pad");
    const context = canvas.getContext("2d");
    let drawing = false;
    let strokes = 0;
    function reset() {
      const ratio = window.devicePixelRatio || 1;
      const rect = canvas.getBoundingClientRect();
      canvas.width = Math.max(1, Math.round(rect.width * ratio));
      canvas.height = Math.max(1, Math.round(rect.height * ratio));
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
      context.fillStyle = "#FFFFFF";
      context.fillRect(0, 0, rect.width, rect.height);
      context.lineWidth = 2.2;
      context.lineCap = "round";
      context.lineJoin = "round";
      context.strokeStyle = "#111111";
      strokes = 0;
    }
    function point(event) {
      const rect = canvas.getBoundingClientRect();
      return [event.clientX - rect.left, event.clientY - rect.top];
    }
    canvas.addEventListener("pointerdown", (event) => {
      drawing = true;
      canvas.setPointerCapture(event.pointerId);
      const [x, y] = point(event);
      context.beginPath();
      context.moveTo(x, y);
      strokes++;
    });
    canvas.addEventListener("pointermove", (event) => {
      if (!drawing) return;
      const [x, y] = point(event);
      context.lineTo(x, y);
      context.stroke();
    });
    ["pointerup", "pointercancel", "pointerleave"].forEach((type) => canvas.addEventListener(type, () => { drawing = false; }));
    $("sign-clear").addEventListener("click", reset);
    window.addEventListener("resize", () => { if (!$("view-sign").hidden && strokes === 0) reset(); });
    return { reset, isEmpty: () => strokes === 0, toDataUrl: () => canvas.toDataURL("image/png") };
  })();

  $("sign-confirm").addEventListener("click", async (event) => {
    const button = event.currentTarget;
    $("sign-error").textContent = "";
    const name = $("sign-name").value.trim();
    if (!name) {
      $("sign-error").textContent = "Scrivi il nome di chi firma.";
      return;
    }
    if (pad.isEmpty()) {
      $("sign-error").textContent = "Manca la firma.";
      return;
    }
    button.disabled = true;
    try {
      const report = await api(`/api/site-reports/${state.report.id}/sign`, {
        method: "POST",
        body: { signedByName: name, signatureImage: pad.toDataUrl() },
      });
      state.report = report;
      history = ["list"];
      renderSigned(report);
      show("show", false);
    } catch (error) {
      $("sign-error").textContent = error.message;
    } finally {
      button.disabled = false;
    }
  });

  // ---------- start
  try {
    const token = sessionStorage.getItem("crmmes.token");
    if (token) {
      setSession(token, sessionStorage.getItem("crmmes.refresh") || null, sessionStorage.getItem("crmmes.name") || "", sessionStorage.getItem("crmmes.user"));
      show("list", false);
      loadList();
    } else {
      show("login", false);
    }
  } catch {
    show("login", false);
  }
})();
