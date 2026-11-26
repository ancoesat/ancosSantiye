import { loadSkin } from "./skin.js";
import { Cast } from "./cast.js";
import { drawScene } from "./scene.js";
import { connect } from "./stream.js";
import { demoState, demoEvents, demoLateGate } from "./demo.js";
import { buildingFrom } from "./layout.js";
import { renderPanels } from "./panels.js";
import { Notifier } from "./notify.js";
import { setupActions } from "./actions.js";

const canvas = document.getElementById("site");
const ctx = canvas.getContext("2d");
const tagLayer = document.getElementById("tags");
const connection = document.getElementById("connection");
const projectLabel = document.getElementById("project");
const picker = document.getElementById("picker");
const panels = document.getElementById("panels");

const params = new URLSearchParams(location.search);
const demoMode = params.has("demo");
const demoName = params.get("demo");

const notifier = new Notifier();
const feed = [];
// One project at a time. ?project=x pins it; without it the page follows the latest active project.
const pinned = params.get("project");
let current = demoMode ? "demo" : pinned ?? "";
let state = null;
let scale = 1;

const skin = await loadSkin("/skin");
const { width, height } = skin.def.scene;
canvas.width = width;
canvas.height = height;
const cast = new Cast(skin);

// Largest whole-number scale that fits next to the side panel.
function fit() {
  const room = Math.min((window.innerWidth - 280) / width, (window.innerHeight - 80) / height);
  scale = Math.max(1, Math.floor(room));
  canvas.style.width = `${width * scale}px`;
  canvas.style.height = `${height * scale}px`;
}
window.addEventListener("resize", fit);
fit();

function pushEvent(event) {
  if (!event || typeof event !== "object") return;
  feed.push(event);
  if (feed.length > 20) feed.shift();
}

// The header says how far the work is and what it is about right now.
function drawHeader() {
  const parts = [];
  if (demoMode && current) parts.push(current);
  if (state && state.stage) parts.push(String(state.stage));
  const planned = Math.max(0, Math.floor(Number(state?.tasksPlanned) || 0));
  const done = Math.max(0, Math.floor(Number(state?.tasksDone) || 0));
  if (planned > 0) parts.push(`${done} of ${planned} tasks done`);
  if (state && typeof state.currentTask === "string" && state.currentTask) parts.push(`now: ${state.currentTask}`);
  projectLabel.textContent = parts.join(" / ");
}

// The scene, the panels and the alert check move together on every state.
function paint() {
  if (!state) return;
  renderPanels(panels, state, feed, Date.now());
  if (demoMode) notifier.check(state);
}

// Alerts cover every project, not only the one on screen, so a gate elsewhere is never missed.
function alertAll(projects) {
  const gates = {};
  for (const project of projects) {
    for (const gate of project.gates ?? []) {
      const label = project.name === current ? gate.label ?? gate.id : `${project.name}: ${gate.label ?? gate.id}`;
      gates[`${project.name}/${gate.id}`] = { ...gate, label };
    }
  }
  notifier.check({ gates });
}

function fillPicker(projects) {
  const options = [new Option(pinned ? "Follow latest" : `Latest: ${current || "none yet"}`, "")];
  for (const project of projects) {
    const waiting = (project.gates ?? []).length;
    const label = waiting > 0 ? `${project.name} (${waiting} waiting)` : String(project.name);
    options.push(new Option(label, String(project.name)));
  }
  picker.replaceChildren(...options);
  picker.value = pinned ?? "";
  picker.hidden = projects.length === 0;
}

function show(next) {
  state = next;
  cast.sync(state.actors, width, buildingFrom(state));
  drawHeader();
  paint();
}

function setConnected(on) {
  connection.textContent = on ? "live" : "offline";
  connection.className = on ? "on" : "off";
}

if (demoMode) {
  show(demoState(demoName));
  for (const event of demoEvents()) pushEvent(event);
  paint();
  setConnected(true);
  connection.textContent = "demo";

  const late = demoLateGate(demoName);
  if (late) {
    setTimeout(() => {
      const gate = {
        id: late.id,
        piece: late.piece ?? null,
        label: late.label,
        since: new Date().toISOString(),
      };
      const gates = { ...(state && state.gates ? state.gates : {}), [gate.id]: gate };
      show({ ...(state || {}), gates });
    }, late.delayMs);
  }
} else {
  // The server keeps the real state. After an event, read it again (local, cheap, never out of step).
  // In follow mode, the project on screen is the first one /projects lists (the latest active).
  let pending = 0;
  const refresh = () => {
    clearTimeout(pending);
    pending = setTimeout(async () => {
      try {
        const projects = await (await fetch("/projects")).json();
        if (!pinned) {
          const latest = projects.length > 0 ? String(projects[0].name) : "";
          if (latest.toLowerCase() !== current.toLowerCase()) {
            feed.length = 0;
            cast.clear();
          }
          current = latest;
        }
        fillPicker(projects);
        alertAll(projects);
        const query = current ? `?project=${encodeURIComponent(current)}` : "";
        // A fresh page (or a new project on screen) starts with what already happened, not an empty list.
        if (feed.length === 0) {
          const recent = await (await fetch(`/activity${query}`)).json();
          if (Array.isArray(recent) && feed.length === 0) recent.forEach(pushEvent);
        }
        const response = await fetch(`/state${query}`);
        if (!response.ok) return;
        show(await response.json());
      } catch {
        // The next event or the reconnect will try again.
      }
    }, 50);
  };

  picker.addEventListener("change", () => {
    const url = new URL(location.href);
    if (picker.value) url.searchParams.set("project", picker.value);
    else url.searchParams.delete("project");
    location.assign(url);
  });

  // Workers who went quiet leave on the server's clock, so ask again now and then even without events.
  setInterval(refresh, 60000);

  setupActions(document.getElementById("actions"));
  connect({
    project: pinned,
    onSnapshot: (snapshot) => {
      show(snapshot);
      refresh();
    },
    onEvent: (event) => {
      const project = event && typeof event.project === "string" ? event.project : "";
      if (project.toLowerCase() === current.toLowerCase()) pushEvent(event);
      refresh();
    },
    onStatus: setConnected,
  });
}

// Keep the "waited" times fresh even when nothing happens.
setInterval(() => {
  if (state) renderPanels(panels, state, feed, Date.now());
}, 30000);

// The panel buttons are rebuilt on every render, so wire them once through the root.
panels.addEventListener("click", async (event) => {
  const target = event.target instanceof Element ? event.target.closest("[data-action]") : null;
  if (!target || !panels.contains(target)) return;

  if (target.dataset.action === "sound") {
    notifier.soundOn = !notifier.soundOn;
    if (state) renderPanels(panels, state, feed, Date.now());
  } else if (target.dataset.action === "notify") {
    await notifier.enableNotifications();
    if (state) renderPanels(panels, state, feed, Date.now());
  }
});

let last = performance.now();
function frame(now) {
  const dt = Math.min(0.1, (now - last) / 1000);
  last = now;
  ctx.imageSmoothingEnabled = false;
  cast.update(dt);
  drawScene(ctx, skin, cast, now, state);
  drawTags();
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

// Name tags and speech bubbles live in HTML over the canvas, so text stays sharp at any scale.
// The bubble says what the worker is doing; text goes in with textContent only.
function drawTags() {
  const tags = cast.tags();
  const seen = new Set();
  for (const tag of tags) {
    seen.add(tag.id);
    let el = tagLayer.querySelector(`[data-id="${CSS.escape(tag.id)}"]`);
    if (!el) {
      el = document.createElement("div");
      el.className = "tag";
      el.dataset.id = tag.id;
      el.append(document.createElement("span"), document.createElement("span"));
      el.children[0].className = "say";
      el.children[1].className = "name";
      tagLayer.append(el);
    }
    const [say, name] = el.children;
    say.textContent = tag.say ?? "";
    say.hidden = !tag.say;
    say.classList.toggle("failed", tag.activity === "failed");
    say.classList.toggle("waiting", tag.say === "Waiting for you");
    name.textContent = tag.label;
    el.style.left = `${tag.x * scale}px`;
    el.style.top = `${(tag.y - 36) * scale + 15}px`;
  }
  for (const el of [...tagLayer.children]) {
    if (!seen.has(el.dataset.id)) el.remove();
  }
  stackBubbles(tags);
}

// Workers standing close would cover each other's bubbles. Left to right, each bubble takes
// the lowest row where it does not touch the one before it in that row.
function stackBubbles(tags) {
  const rows = [];
  for (const tag of [...tags].sort((a, b) => a.x - b.x)) {
    const say = tagLayer.querySelector(`[data-id="${CSS.escape(tag.id)}"] .say`);
    if (!say || say.hidden) continue;
    const width = say.offsetWidth;
    const left = tag.x * scale - width / 2;
    let row = rows.findIndex((right) => left > right + 4);
    if (row === -1) row = rows.push(0) - 1;
    rows[row] = left + width;
    say.style.marginBottom = `${row * 22}px`;
  }
}
