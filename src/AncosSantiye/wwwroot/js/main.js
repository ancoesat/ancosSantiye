import { loadSkin } from "./skin.js";
import { Cast } from "./cast.js";
import { drawScene } from "./scene.js";
import { connect } from "./stream.js";
import { demoState, demoEvents, demoLateGate } from "./demo.js";
import { buildingFrom } from "./layout.js";
import { renderPanels } from "./panels.js";
import { Notifier } from "./notify.js";

const canvas = document.getElementById("site");
const ctx = canvas.getContext("2d");
const tagLayer = document.getElementById("tags");
const connection = document.getElementById("connection");
const projectLabel = document.getElementById("project");
const panels = document.getElementById("panels");

const params = new URLSearchParams(location.search);
const demoMode = params.has("demo");
const demoName = params.get("demo");

const notifier = new Notifier();
const feed = [];
let latestProject = demoMode ? "demo" : "";
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

function drawHeader() {
  const parts = [];
  if (latestProject) parts.push(latestProject);
  if (state && state.stage) parts.push(String(state.stage));
  projectLabel.textContent = parts.join(" / ");
}

// The scene, the panels and the alert check move together on every state.
function paint() {
  if (!state) return;
  renderPanels(panels, state, feed, Date.now());
  notifier.check(state);
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
  let pending = 0;
  const refresh = () => {
    clearTimeout(pending);
    pending = setTimeout(async () => {
      try {
        const response = await fetch("/state");
        if (!response.ok) return;
        show(await response.json());
      } catch {
        // The next event or the reconnect will try again.
      }
    }, 50);
  };
  connect({
    onSnapshot: show,
    onEvent: (event) => {
      if (event && typeof event.project === "string" && event.project) latestProject = event.project;
      pushEvent(event);
      paint();
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

// Name tags live in HTML over the canvas, so text stays sharp at any scale.
function drawTags() {
  const tags = cast.tags();
  const seen = new Set();
  for (const tag of tags) {
    seen.add(tag.id);
    let el = tagLayer.querySelector(`[data-id="${CSS.escape(tag.id)}"]`);
    if (!el) {
      el = document.createElement("span");
      el.className = "tag";
      el.dataset.id = tag.id;
      tagLayer.append(el);
    }
    el.textContent = tag.label;
    el.style.left = `${tag.x * scale}px`;
    el.style.top = `${(tag.y - 36) * scale}px`;
  }
  for (const el of [...tagLayer.children]) {
    if (!seen.has(el.dataset.id)) el.remove();
  }
}
