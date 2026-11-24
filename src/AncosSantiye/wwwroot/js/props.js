import { drawTile } from "./sprites.js";
import { palette } from "./palette.js";

const FLOOR_HEIGHT = 32;

// A one pixel staircase, so the diagonals stay crisp like the tiles around them.
function pixelDiagonal(ctx, x1, x2, y, height) {
  for (let i = 0; i < height; i += 1) {
    const t = height <= 1 ? 0 : i / (height - 1);
    const x = Math.round(x1 + (x2 - x1) * t);
    ctx.fillRect(x, y + i, 1, 1);
  }
}

function drawCrane(ctx, skin, model, ms, groundY) {
  const mastTop = 18;

  ctx.fillStyle = palette.crane;
  ctx.fillRect(64, mastTop, 2, groundY - mastTop);
  ctx.fillRect(70, mastTop, 2, groundY - mastTop);

  ctx.fillStyle = palette.craneDark;
  let down = true;
  for (let y = mastTop; y < groundY; y += 8) {
    const height = Math.min(8, groundY - y);
    if (down) pixelDiagonal(ctx, 66, 70, y, height);
    else pixelDiagonal(ctx, 70, 66, y, height);
    down = !down;
  }

  ctx.fillStyle = palette.crane;
  ctx.fillRect(36, 14, 142, 4);
  ctx.fillStyle = palette.craneDark;
  ctx.fillRect(36, 17, 142, 1);

  ctx.fillStyle = palette.concrete;
  ctx.fillRect(36, 18, 20, 10);
  ctx.fillStyle = palette.concreteDark;
  ctx.fillRect(36, 26, 20, 2);

  ctx.fillStyle = palette.truck;
  ctx.fillRect(72, 18, 14, 12);
  ctx.fillStyle = palette.truckDark;
  ctx.fillRect(72, 18, 14, 2);
  ctx.fillStyle = palette.glass;
  ctx.fillRect(75, 21, 6, 5);

  const target = model.craneTarget;
  const cableX = target ? Math.round(target.x) : 150;
  let hookY = 60;
  if (target) {
    const bob = Math.round(Math.sin(ms / 350) * 2);
    hookY = Math.max(19, target.y - 18 + bob);
  }

  ctx.fillStyle = palette.cable;
  ctx.fillRect(cableX, 17, 1, hookY - 17);
  ctx.fillStyle = palette.craneDark;
  ctx.fillRect(cableX - 1, hookY, 3, 1);
  ctx.fillRect(cableX - 1, hookY + 1, 1, 2);

  if (target) drawTile(ctx, skin.tile("crate"), cableX - 8, hookY + 2);
}

function drawScaffold(ctx, model, groundY) {
  const top = groundY - FLOOR_HEIGHT * Math.max(1, model.floors.length);

  ctx.fillStyle = palette.pole;
  ctx.fillRect(196, top, 2, groundY - top);
  ctx.fillRect(208, top, 2, groundY - top);

  ctx.fillStyle = palette.plank;
  for (let y = top + 16; y < groundY; y += 16) {
    ctx.fillRect(196, y, 14, 2);
  }

  ctx.fillStyle = palette.pole;
  let down = true;
  for (let y = top; y + 16 <= groundY; y += 16) {
    if (down) pixelDiagonal(ctx, 198, 208, y, 16);
    else pixelDiagonal(ctx, 208, 198, y, 16);
    down = !down;
  }
}

function drawTruck(ctx, groundY) {
  ctx.fillStyle = palette.concrete;
  ctx.fillRect(4, groundY - 24, 27, 20);
  ctx.fillStyle = palette.concreteDark;
  ctx.fillRect(4, groundY - 24, 27, 2);

  ctx.fillStyle = palette.truck;
  ctx.fillRect(31, groundY - 20, 16, 16);
  ctx.fillStyle = palette.truckDark;
  ctx.fillRect(31, groundY - 20, 16, 2);
  ctx.fillStyle = palette.glass;
  ctx.fillRect(39, groundY - 17, 6, 6);

  ctx.fillStyle = palette.pole;
  for (const x of [12, 41]) {
    ctx.beginPath();
    ctx.arc(x, groundY - 4, 4, 0, Math.PI * 2);
    ctx.fill();
  }
}

function drawOffice(ctx, groundY) {
  const x = 240;
  const top = groundY - 26;
  const width = 46;
  const height = 26;

  ctx.fillStyle = palette.container;
  ctx.fillRect(x, top, width, height);
  ctx.fillStyle = palette.containerDark;
  ctx.fillRect(x, top, width, 2);
  for (let i = 4; i < width; i += 4) ctx.fillRect(x + i, top + 2, 1, height - 2);

  ctx.fillStyle = palette.containerDark;
  ctx.fillRect(245, top + 6, 12, 9);
  ctx.fillStyle = palette.glass;
  ctx.fillRect(246, top + 7, 10, 7);

  ctx.fillStyle = palette.containerDark;
  ctx.fillRect(272, top + 6, 10, height - 6);
}

function drawGateBoard(ctx, skin, model) {
  drawTile(ctx, skin.tile("fence"), 288, 104);

  ctx.fillStyle = palette.pole;
  ctx.fillRect(298, 124, 2, 28);

  ctx.fillStyle = palette.plank;
  ctx.fillRect(292, 112, 14, 12);
  ctx.fillStyle = palette.craneDark;
  ctx.fillRect(292, 112, 14, 1);
  ctx.fillRect(292, 123, 14, 1);

  const papers = Math.min(3, model.gateCount);
  for (let i = 0; i < papers; i += 1) {
    const px = 294;
    const py = 113 + i * 3;
    ctx.fillStyle = palette.paper;
    ctx.fillRect(px, py, 6, 3);
    ctx.fillStyle = palette.alert;
    ctx.fillRect(px + 4, py + 1, 1, 1);
  }
}

export function drawProps(ctx, skin, model, ms) {
  const groundY = skin.def.scene.groundY;

  if (model.showCrane) drawCrane(ctx, skin, model, ms, groundY);
  if (model.showScaffold) drawScaffold(ctx, model, groundY);
  if (model.truckParked) drawTruck(ctx, groundY);

  drawOffice(ctx, groundY);
  drawGateBoard(ctx, skin, model);
}
