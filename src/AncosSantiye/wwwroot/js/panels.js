// The four side panels: waiting gates, pieces, budget and activity.
// Everything that comes from outside the browser goes in with textContent only.
import { lookOf } from "./layout.js";

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

function make(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = String(text);
  return node;
}

function section(title) {
  const node = document.createElement("section");
  node.className = "panel";
  node.append(make("h2", null, title));
  return node;
}

function emptyLine(text) {
  return make("p", "empty", text);
}

function timeOf(value) {
  const at = Date.parse(String(value ?? ""));
  return Number.isFinite(at) ? at : 0;
}

function isMissing(value) {
  return value === undefined || value === null || String(value).trim() === "";
}

function numeric(value) {
  if (isMissing(value)) return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function clock(date) {
  return `${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}`;
}

function clockSeconds(date) {
  return `${clock(date)}:${String(date.getSeconds()).padStart(2, "0")}`;
}

function waitedText(now, since) {
  const at = timeOf(since);
  if (at === 0) return "just now";
  const minutes = Math.floor(Math.max(0, now - at) / 60000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `${hours} h`;
  return `${Math.floor(hours / 24)} d`;
}

function soundOn() {
  try {
    const value = window.localStorage.getItem("santiye.sound");
    return value !== "off" && value !== "0" && value !== "false";
  } catch {
    return true;
  }
}

function notificationApi() {
  try {
    return typeof window.Notification === "function" ? window.Notification : null;
  } catch {
    return null;
  }
}

function canAskNotifications() {
  const api = notificationApi();
  if (!api) return false;
  try {
    return api.permission !== "granted";
  } catch {
    return false;
  }
}

// Built here, wired by main.js through one delegated listener on #panels.
function controls() {
  const wrap = make("div", "panel-controls");
  const on = soundOn();

  const sound = make("button", null, on ? "Sound on" : "Sound off");
  sound.type = "button";
  sound.dataset.action = "sound";
  sound.setAttribute("aria-pressed", on ? "true" : "false");

  const notify = make("button", null, "Enable notifications");
  notify.type = "button";
  notify.dataset.action = "notify";
  notify.hidden = !canAskNotifications();

  wrap.append(sound, notify);
  return wrap;
}

function waitingSection(gates, now) {
  const rows = Object.entries(gates)
    .map(([key, gate]) => ({ key, gate: gate && typeof gate === "object" ? gate : {} }))
    .sort((a, b) => timeOf(b.gate.since) - timeOf(a.gate.since));

  const node = section(rows.length > 0 ? `Waiting for you (${rows.length})` : "Waiting for you");
  node.append(controls());

  if (rows.length === 0) {
    node.append(emptyLine("Nothing waiting"));
    return node;
  }

  for (const { key, gate } of rows) {
    const fallback = isMissing(gate.id) ? key : String(gate.id);
    const label = isMissing(gate.label) ? fallback : String(gate.label);
    const row = make("div", "row");
    row.append(make("span", "row-main", label));
    if (!isMissing(gate.piece)) row.append(make("span", "row-sub", gate.piece));
    row.append(make("span", "row-note", waitedText(now, gate.since)));
    node.append(row);
  }

  return node;
}

function piecesSection(site, pieces) {
  const planned = Math.max(0, Math.floor(Number(site.tasksPlanned) || 0));
  const done = Math.max(0, Math.floor(Number(site.tasksDone) || 0));

  // A plain session has no pieces, only its task list: then the task list is what this panel shows.
  if (Object.keys(pieces).length === 0 && planned > 0) return tasksSection(site, done, planned);

  const node = section("Pieces");
  const stage = isMissing(site.stage) ? null : String(site.stage);
  const meta = [stage, planned > 0 ? `${done} / ${planned} tasks` : null].filter(Boolean).join(", ");
  if (meta) node.append(make("p", "meta", meta));

  const rows = Object.entries(pieces)
    .map(([key, piece]) => ({ key, piece: piece && typeof piece === "object" ? piece : {} }))
    .sort(
      (a, b) =>
        timeOf(b.piece.updated) - timeOf(a.piece.updated) ||
        String(a.key).localeCompare(String(b.key)),
    );

  if (rows.length === 0) {
    node.append(emptyLine("No pieces yet"));
    return node;
  }

  for (const { key, piece } of rows) {
    const id = isMissing(piece.id) ? key : String(piece.id);
    const row = make("div", "row");
    row.append(make("span", `dot ${lookOf(piece.status, piece.verdict)}`));
    row.append(make("span", "row-main", id));
    if (!isMissing(piece.status)) row.append(make("span", "row-sub", piece.status));
    if (!isMissing(piece.verdict)) row.append(make("span", "row-note", piece.verdict));
    node.append(row);
  }

  return node;
}

function tasksSection(site, done, planned) {
  const node = section("Tasks");
  node.append(make("p", "meta", `${done} of ${planned} done`));
  const tasks = Array.isArray(site.currentTasks) ? site.currentTasks : [];
  const open = tasks.find((task) => task && !task.done);
  for (const task of tasks) {
    if (!task || typeof task !== "object") continue;
    const row = make("div", "row");
    row.append(make("span", `dot ${task.done ? "solid" : task === open ? "half" : "planned"}`));
    row.append(make("span", "row-main", isMissing(task.subject) ? String(task.id ?? "") : String(task.subject)));
    row.append(make("span", "row-sub", task.done ? "done" : task === open ? "now" : ""));
    node.append(row);
  }
  return node;
}

function resetText(value, withWeekday) {
  const seconds = numeric(value);
  if (seconds === null || seconds <= 0) return null;
  const date = new Date(seconds * 1000);
  if (Number.isNaN(date.getTime())) return null;
  return withWeekday ? `resets ${WEEKDAYS[date.getDay()]} ${clock(date)}` : `resets ${clock(date)}`;
}

function bar(label, value, note) {
  const wrap = make("div", "bar");
  const percent = numeric(value);
  const head = make("div", "bar-head");
  head.append(make("span", "bar-label", label));
  head.append(make("span", "bar-value", percent === null ? "no data" : `${value}%`));
  wrap.append(head);

  if (percent !== null) {
    const track = make("div", "bar-track");
    const fill = make("div", "bar-fill");
    fill.style.width = `${Math.max(0, Math.min(100, percent))}%`;
    track.append(fill);
    wrap.append(track);
  }

  if (note) wrap.append(make("div", "bar-note", note));
  return wrap;
}

function line(label, value) {
  const row = make("div", "stat");
  row.append(make("span", "stat-label", label));
  row.append(make("span", "stat-value", value));
  return row;
}

function thousands(value) {
  const number = numeric(value);
  return number === null ? "no data" : number.toLocaleString("en-US");
}

function budgetSection(usage) {
  const node = section("Budget");
  if (Object.keys(usage).length === 0) {
    node.append(emptyLine("No data yet. Set up the status line: ancos-santiye hooks install"));
    return node;
  }

  const cost = numeric(usage.costUsd);
  node.append(
    bar("Context", usage.contextUsedPercentage, null),
    bar("5 hour limit", usage.fiveHourUsedPercentage, resetText(usage.fiveHourResetsAt, false)),
    bar("7 day limit", usage.sevenDayUsedPercentage, resetText(usage.sevenDayResetsAt, true)),
    line("Cost", cost === null ? "no data" : `$${cost.toFixed(2)}`),
    line("Input tokens", thousands(usage.totalInputTokens)),
    line("Output tokens", thousands(usage.totalOutputTokens)),
    line("Model", isMissing(usage.model) ? "no data" : String(usage.model)),
  );
  return node;
}

function clockOf(value) {
  const at = timeOf(value);
  if (at <= 0) return "";
  return clockSeconds(new Date(at));
}

function activitySection(events) {
  const node = document.createElement("section");
  node.className = "panel";
  const details = document.createElement("details");
  details.open = true;
  const summary = document.createElement("summary");
  summary.append(make("h2", null, "Activity"));
  details.append(summary);

  const list = make("ol", "activity");
  const recent = events.slice(-20).reverse();
  if (recent.length === 0) {
    list.append(make("li", "empty", "No activity yet"));
  }

  for (const event of recent) {
    if (!event || typeof event !== "object") continue;
    const actor = event.actor && typeof event.actor === "object" ? event.actor : {};
    const who = actor.name || actor.role || "unknown";
    let text = `${who} ${isMissing(event.kind) ? "" : event.kind}`.trim();
    const tool = event.data && typeof event.data === "object" ? event.data.tool : null;
    const extra = !isMissing(tool) ? tool : event.piece;
    if (!isMissing(extra)) text += ` ${extra}`;

    const item = document.createElement("li");
    item.append(make("time", null, clockOf(event.ts)));
    item.append(make("span", null, text));
    list.append(item);
  }

  details.append(list);
  node.append(details);
  return node;
}

export function renderPanels(root, state, events, now) {
  if (!root) return;
  while (root.firstChild) root.removeChild(root.firstChild);

  const site = state && typeof state === "object" ? state : {};
  const gates = site.gates && typeof site.gates === "object" ? site.gates : {};
  const pieces = site.pieces && typeof site.pieces === "object" ? site.pieces : {};
  const usage = site.usage && typeof site.usage === "object" ? site.usage : {};
  const feed = Array.isArray(events) ? events : [];
  const at = Number.isFinite(now) ? now : Date.now();

  root.append(
    waitingSection(gates, at),
    piecesSection(site, pieces),
    budgetSection(usage),
    activitySection(feed),
  );
}
