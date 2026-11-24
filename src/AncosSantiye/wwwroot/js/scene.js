import { drawTile } from "./sprites.js";
import { buildingFrom } from "./layout.js";
import { drawBuilding } from "./building.js";
import { drawProps } from "./props.js";

// Draws the static parts (sky, ground), then the site props, the building and the cast.
export function drawScene(ctx, skin, cast, ms, state) {
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

  const model = buildingFrom(state);
  drawProps(ctx, skin, model, ms);
  drawBuilding(ctx, skin, model);

  cast.draw(ctx, ms);
}
