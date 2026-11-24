import { drawTile } from "./sprites.js";
import { palette } from "./palette.js";

const LEFT = 96;
const SLOT = 16;
const FLOOR_HEIGHT = 32;
const SLAB_LEFT = 92;
const SLAB_WIDTH = 104;
const COLUMNS = [96, 136, 176];
const PATTERN = ["brick", "glass", "brick", "brick", "glass", "brick"];
const RANK = { foundation: 0, frame: 1, ready: 2, building: 3, done: 4 };

function atLeast(phase, name) {
  return (RANK[phase] ?? -1) >= RANK[name];
}

function dottedOutline(ctx, x, y, w, h) {
  ctx.fillStyle = "rgba(255, 255, 255, 0.8)";
  for (let i = 0; i < w; i += 4) {
    ctx.fillRect(x + i, y, 1, 1);
    ctx.fillRect(x + i, y + h - 1, 1, 1);
  }
  for (let i = 0; i < h; i += 4) {
    ctx.fillRect(x, y + i, 1, 1);
    ctx.fillRect(x + w - 1, y + i, 1, 1);
  }
}

// Deterministic rubble so the scene does not flicker between frames.
const RUBBLE = [
  [2, 24, 5, 4],
  [8, 28, 4, 3],
  [4, 29, 3, 2],
  [12, 26, 3, 3],
];

function drawSlot(ctx, tiles, slot, index, x, top) {
  const name = PATTERN[index % PATTERN.length];

  if (slot.look === "solid" || slot.look === "pending") {
    drawTile(ctx, tiles[name], x, top);
    drawTile(ctx, tiles[name], x, top + 16);
    if (slot.look === "pending") {
      ctx.fillStyle = "rgba(12, 12, 20, 0.35)";
      ctx.fillRect(x, top, SLOT, FLOOR_HEIGHT);
    }
    return;
  }

  if (slot.look === "half") {
    drawTile(ctx, tiles[name], x, top + 16);
    return;
  }

  if (slot.look === "torn") {
    drawTile(ctx, tiles[name], x, top + 16);
    RUBBLE.forEach(([dx, dy, w, h], i) => {
      ctx.fillStyle = i % 2 === 0 ? palette.concrete : palette.concreteDark;
      ctx.fillRect(x + dx, top + dy, w, h);
    });
    return;
  }

  dottedOutline(ctx, x, top, SLOT, FLOOR_HEIGHT);
}

export function drawBuilding(ctx, skin, model) {
  const groundY = skin.def.scene.groundY;
  const tiles = { brick: skin.tile("brick"), glass: skin.tile("glass") };
  const floors = model.floors;
  const phase = model.phase;

  if (atLeast(phase, "foundation")) {
    ctx.fillStyle = palette.concrete;
    ctx.fillRect(SLAB_LEFT, groundY - 4, SLAB_WIDTH, 4);
    ctx.fillStyle = palette.concreteDark;
    ctx.fillRect(SLAB_LEFT, groundY - 1, SLAB_WIDTH, 1);
  }

  for (let floor = 0; floor < floors.length; floor += 1) {
    const top = groundY - FLOOR_HEIGHT * (floor + 1);

    floors[floor].forEach((slot, index) => {
      drawSlot(ctx, tiles, slot, index, LEFT + index * SLOT, top);
    });

    ctx.fillStyle = palette.concrete;
    ctx.fillRect(SLAB_LEFT, top, SLAB_WIDTH, 3);
    ctx.fillStyle = palette.concreteDark;
    ctx.fillRect(SLAB_LEFT, top + 2, SLAB_WIDTH, 1);
  }

  // Columns go over the walls, the same way the reference frame shows them.
  if (atLeast(phase, "frame")) {
    const height = Math.max(1, floors.length) * FLOOR_HEIGHT;
    const top = groundY - height;
    for (const x of COLUMNS) {
      ctx.fillStyle = palette.concrete;
      ctx.fillRect(x, top, 6, height);
      ctx.fillStyle = palette.concreteDark;
      ctx.fillRect(x + 5, top, 1, height);
    }
  }

  if (phase === "done" && floors.length > 0) {
    const top = groundY - FLOOR_HEIGHT * floors.length;
    ctx.fillStyle = palette.concrete;
    ctx.fillRect(SLAB_LEFT, top - 4, SLAB_WIDTH, 4);
    ctx.fillStyle = palette.concreteDark;
    ctx.fillRect(SLAB_LEFT, top - 4, SLAB_WIDTH, 1);

    const sx = 132;
    const sy = top - 20;
    ctx.fillStyle = palette.concreteDark;
    ctx.fillRect(sx - 1, sy - 1, 26, 12);
    ctx.fillStyle = palette.ok;
    ctx.fillRect(sx, sy, 24, 10);
    ctx.fillStyle = palette.alert;
    ctx.fillRect(sx + 10, sy + 3, 4, 4);
    ctx.fillStyle = palette.concreteDark;
    ctx.fillRect(sx + 11, sy + 11, 2, 6);
  }
}
