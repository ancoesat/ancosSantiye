import { drawTile } from "./sprites.js";

// Draws the static parts: sky, ground. The building and the site props come in F4b.
export function drawScene(ctx, skin, cast, ms) {
  const { width, height, groundY } = skin.def.scene;

  for (let x = 0; x < width; x += skin.background.width) {
    ctx.drawImage(skin.background, x, 0);
  }

  const grass = skin.tile("grass");
  const dirt = skin.tile("dirt");
  for (let x = 0; x < width; x += grass.w) {
    drawTile(ctx, grass, x, groundY);
    for (let y = groundY + grass.h; y < height; y += dirt.h) drawTile(ctx, dirt, x, y);
  }

  cast.draw(ctx, ms, groundY);
}
