// Frame math and drawing for sprite sheets laid out as one horizontal row of frames.

export function frameAt(anim, ms) {
  return Math.floor((ms / 1000) * anim.fps) % anim.frames;
}

// Draws with the feet on (centerX, groundY). Wider frames (like a hammer swing) stay centered.
export function drawAnim(ctx, anim, ms, centerX, groundY, flip = false) {
  const frame = frameAt(anim, ms);
  const x = Math.round(centerX - anim.frameW / 2);
  const y = Math.round(groundY - anim.frameH);
  if (!flip) {
    ctx.drawImage(anim.image, frame * anim.frameW, 0, anim.frameW, anim.frameH, x, y, anim.frameW, anim.frameH);
    return;
  }

  ctx.save();
  ctx.translate(x + anim.frameW, y);
  ctx.scale(-1, 1);
  ctx.drawImage(anim.image, frame * anim.frameW, 0, anim.frameW, anim.frameH, 0, 0, anim.frameW, anim.frameH);
  ctx.restore();
}

export function drawTile(ctx, tile, x, y) {
  ctx.drawImage(tile.image, tile.x, tile.y, tile.w, tile.h, Math.round(x), Math.round(y), tile.w, tile.h);
}
