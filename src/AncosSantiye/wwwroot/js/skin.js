// Loads a skin: its skin.json plus every sheet it names. Frame counts come from the image width.

function loadImage(url) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error(`Could not load ${url}`));
    image.src = url;
  });
}

export async function loadSkin(base) {
  const response = await fetch(`${base}/skin.json`);
  if (!response.ok) {
    throw new Error(`Could not load ${base}/skin.json (${response.status})`);
  }

  const def = await response.json();
  const sources = new Set([def.background]);
  for (const tile of Object.values(def.tiles)) sources.add(tile.sheet);
  for (const anims of Object.values(def.characters)) {
    for (const anim of Object.values(anims)) sources.add(anim.sheet);
  }

  const images = new Map();
  await Promise.all([...sources].map(async (src) => images.set(src, await loadImage(`${base}/${src}`))));

  const anims = new Map();
  for (const [character, set] of Object.entries(def.characters)) {
    for (const [name, anim] of Object.entries(set)) {
      const image = images.get(anim.sheet);
      anims.set(`${character}/${name}`, { ...anim, image, frames: Math.max(1, Math.floor(image.width / anim.frameW)) });
    }
  }

  return {
    def,
    background: images.get(def.background),
    tile(name) {
      const tile = def.tiles[name];
      return { ...tile, image: images.get(tile.sheet) };
    },
    anim(character, name) {
      return anims.get(`${character}/${name}`) ?? anims.get(`${character}/idle`);
    },
    // A stable character for an actor: same role and same key always give the same character.
    characterFor(role, key) {
      const list = def.roles[role] ?? def.roles.worker;
      let hash = 0;
      for (const ch of key) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
      return list[hash % list.length];
    },
  };
}
