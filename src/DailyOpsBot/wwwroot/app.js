/* DailyOps dashboard — fetches /api/latest and /api/runs, refreshes every 30s. */

const REFRESH_MS = 30_000;

const $ = (id) => document.getElementById(id);

const fmtInt = new Intl.NumberFormat("en-US");
const fmtCurrency = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 0,
});

function fmtDate(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toISOString().replace("T", " ").slice(0, 19);
}

function fmtDuration(ms) {
  if (ms == null) return "—";
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
}

function setStatusPill(anomalyCount) {
  const pill = $("status-pill");
  pill.classList.remove("pill-idle", "pill-ok", "pill-warning", "pill-critical");
  if (anomalyCount == null) {
    pill.textContent = "—";
    pill.classList.add("pill-idle");
  } else if (anomalyCount === 0) {
    pill.textContent = "All clear";
    pill.classList.add("pill-ok");
  } else {
    pill.textContent = `${anomalyCount} anomal${anomalyCount === 1 ? "y" : "ies"}`;
    pill.classList.add("pill-warning");
  }
}

function renderLatest(run) {
  const m = run.metrics ?? {};
  $("kpi-rows").textContent = m.rowsProcessed != null ? fmtInt.format(m.rowsProcessed) : "—";
  $("kpi-symbols").textContent = m.symbolsFetched ?? "—";
  $("kpi-anomalies").textContent = m.anomalyCount ?? "—";
  $("kpi-revenue").textContent =
    m.totalRevenue != null ? fmtCurrency.format(m.totalRevenue) : "—";

  $("last-run-label").textContent = `Last run ${fmtDate(run.runAtUtc)} UTC`;
  setStatusPill(m.anomalyCount ?? null);

  $("run-meta").textContent =
    `${m.filesProcessed ?? 0} sales file(s) · ${fmtInt.format(m.totalUnits ?? 0)} units · ` +
    `run took ${fmtDuration(run.durationMs)}`;

  renderAnomalies(run.anomalies ?? []);
  renderReports(run.reports ?? [], run.emailStatus);
}

function renderAnomalies(anomalies) {
  const list = $("anomaly-list");
  const empty = $("anomalies-empty");
  $("anomalies-sub").textContent =
    anomalies.length > 0 ? `${anomalies.length} detected` : "";
  list.innerHTML = "";
  empty.classList.toggle("hidden", anomalies.length > 0);

  for (const a of anomalies) {
    const li = document.createElement("li");
    li.className = "anomaly-item";

    const sev = (a.severity || "info").toLowerCase();
    li.innerHTML =
      `<span class="badge badge-${sev}">${a.severity ?? "Info"}</span>` +
      `<div class="anomaly-body">` +
      `<div class="anomaly-type">${a.type ?? ""}</div>` +
      `<div class="anomaly-desc"></div>` +
      `</div>`;
    li.querySelector(".anomaly-desc").textContent = a.description ?? "";

    if (a.value) {
      const v = document.createElement("span");
      v.className = "anomaly-value";
      v.textContent = a.value;
      li.appendChild(v);
    }
    list.appendChild(li);
  }
}

function renderReports(reports, emailStatus) {
  const actions = $("report-actions");
  actions.innerHTML = "";

  $("email-sub").textContent =
    emailStatus === "sent" ? "Report emailed" :
    emailStatus === "demo" ? "Email in demo mode" : "";

  if (reports.length === 0) {
    actions.innerHTML = '<p class="empty-state" style="padding:0">No reports generated yet.</p>';
    return;
  }

  for (const name of reports) {
    const a = document.createElement("a");
    a.className = "btn" + (name.endsWith(".pdf") ? " btn-primary" : "");
    a.href = `/api/reports/${encodeURIComponent(name)}`;
    a.textContent = name.endsWith(".pdf") ? "Download PDF" : "Download Excel";
    a.title = name;
    actions.appendChild(a);
  }
}

function renderHistory(runs) {
  const body = $("history-body");
  const empty = $("history-empty");
  body.innerHTML = "";
  empty.classList.toggle("hidden", runs.length > 0);
  $("history-sub").textContent = runs.length > 0 ? `${runs.length} run(s) recorded` : "";

  for (const run of runs) {
    const m = run.metrics ?? {};
    const tr = document.createElement("tr");
    const anomalies = m.anomalyCount ?? 0;
    tr.innerHTML =
      `<td>${fmtDate(run.runAtUtc)}</td>` +
      `<td class="num">${m.rowsProcessed != null ? fmtInt.format(m.rowsProcessed) : "—"}</td>` +
      `<td class="num">${m.totalRevenue != null ? fmtCurrency.format(m.totalRevenue) : "—"}</td>` +
      `<td class="num">${anomalies}</td>` +
      `<td class="num">${fmtDuration(run.durationMs)}</td>` +
      `<td><span class="tag tag-${run.emailStatus ?? ""}">${run.emailStatus ?? "—"}</span></td>`;
    body.appendChild(tr);
  }
}

async function fetchJson(url) {
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) return null;
  return res.json();
}

async function refresh() {
  try {
    const [latest, runs] = await Promise.all([
      fetchJson("/api/latest"),
      fetchJson("/api/runs"),
    ]);
    if (latest) renderLatest(latest);
    else setStatusPill(null);
    if (runs) renderHistory(runs);
  } catch {
    // Keep the last good render if the API is briefly unavailable.
  }
}

refresh();
setInterval(refresh, REFRESH_MS);
