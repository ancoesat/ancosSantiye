import { loadSkin } from "./skin.js";
import { Cast } from "./cast.js";
import { drawScene } from "./scene.js";
import { connect } from "./stream.js";
import { demoState } from "./demo.js";
import { buildingFrom } from "./layout.js";

const canvas = document.getElementById("site");
const ctx = canvas.getContext("2d");
const tagLayer = document.getElementById("tags");
const connection = document.getElementById("connection");

const skin = await loadSkin("/skins/grafxkid");
const { width, height } = skin.def.scene;
canvas.width = width;
canvas.height = height;
const cast = new Cast(skin);
let state = null;
let scale = 1;

// Largest whole-number scale that fits next to the side panel.
function fit() {
  const room = Math.min((window.innerWidth - 280) / width, (window.innerHeight - 80) / height);
  scale = Math.max(1, Math.floor(room));
  canvas.style.width = `${width * scale}px`;
  canvas.style.height = `${height * scale}px`;
}
window.addEventListener("resize", fit);
fit();

function show(next) {
  state = next;
  cast.sync(state.actors, width, buildingFrom(state));
}

function setConnected(on) {
  connection.textContent = on ? "live" : "offline";
  connection.className = on ? "on" : "off";
}

if (new URLSearchParams(location.search).has("demo")) {
  show(demoState(new URLSearchParams(location.search).get("demo")));
  setConnected(true);
  connection.textContent = "demo";
} else {
  // The server keeps the real state. After an event, read it again (local, cheap, never out of step).
  let pending = 0;
  const refresh = () => {
    clearTimeout(pending);
    pending = setTimeout(async () => show(await (await fetch("/state")).json()), 50);
  };
  connect({ onSnapshot: show, onEvent: refresh, onStatus: setConnected });
}

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
