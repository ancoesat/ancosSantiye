import { drawAnim } from "./sprites.js";

// The building and the scaffold ladder, in scene pixels (see layout.js and props.js).
const BUILDING_LEFT = 104;
const BUILDING_RIGHT = 184;
const LADDER_X = 202;
const FLOOR_HEIGHT = 32;
const WALK_SPEED = 40; // pixels per second
const OFF_LEFT = -24;

// Ground spots outside the building, so nobody hides the walls.
// Each role has its own spot; idle builders share a pool by the site office.
const GROUND = {
  chief: 262,
  inspector: 214,
  architect: 86,
  supplier: 30,
};
const IDLE_POOL = [238, 300, 86, 52, 226];

// Where each kind of action happens (see Doing in SiteState.cs). x is the first place, step the next
// one for a second person there, faceLeft where they look. Building has its own rule below.
const STATIONS = {
  read: { x: 288, step: -10, faceLeft: false }, // at the board by the gate
  run: { x: 60, step: -12, faceLeft: false }, // at the crane controls
  plan: { x: 262, step: -10, faceLeft: true }, // at the site office
};

// The people on screen. Follows SiteState.actors: newcomers walk in from the left, leavers walk out.
// Each one stands where its current action happens, and only a builder at the building plays "work".
// Moving only happens between stations, so every walk means the agent changed what it does.
export class Cast {
  constructor(skin) {
    this.skin = skin;
    this.members = new Map();
  }

  // Another project on screen: its people were never here, so they do not walk off, they vanish.
  clear() {
    this.members.clear();
  }

  sync(actors, width, model) {
    const groundY = this.skin.def.scene.groundY;
    const floors = model?.floors?.length ?? 0;
    const roofY = groundY - FLOOR_HEIGHT * floors;
    const list = Object.values(actors ?? {}).sort((a, b) => a.id.localeCompare(b.id));

    // The supplier always stays by its truck: deliveries are its only job.
    const actionOf = (actor) => (actor.role === "supplier" ? null : actor.action);
    const builders = list.filter((a) => actionOf(a) === "build");
    const gap = builders.length > 1 ? (BUILDING_RIGHT - BUILDING_LEFT) / (builders.length - 1) : 0;
    const taken = {};
    let idle = 0;

    const ids = new Set();
    for (const actor of list) {
      ids.add(actor.id);
      const action = actionOf(actor);
      let spot;
      if (action === "build") {
        // On the roof when there are floors, at the walls when there are none yet.
        const index = builders.indexOf(actor);
        const x = builders.length === 1 ? (BUILDING_LEFT + BUILDING_RIGHT) / 2 : BUILDING_LEFT + index * gap;
        spot = { x, y: floors > 0 ? roofY : groundY, faceLeft: false };
      } else if (STATIONS[action]) {
        const station = STATIONS[action];
        const n = taken[action] ?? 0;
        taken[action] = n + 1;
        spot = { x: station.x + n * station.step, y: groundY, faceLeft: station.faceLeft };
      } else {
        // Waiting, starting, failed or unknown: its own place, standing still.
        spot = { x: GROUND[actor.role] ?? IDLE_POOL[idle++ % IDLE_POOL.length], y: groundY, faceLeft: false };
      }

      let member = this.members.get(actor.id);
      if (!member) {
        member = {
          actor,
          character: this.skin.characterFor(actor.role, actor.name ?? actor.id),
          x: OFF_LEFT,
          y: groundY,
          path: [],
          goal: null,
          leaving: false,
          facingLeft: false,
        };
        this.members.set(actor.id, member);
      }

      member.actor = actor;
      member.leaving = false;
      this.route(member, spot);
    }

    for (const [id, member] of this.members) {
      if (!ids.has(id) && !member.leaving) {
        member.leaving = true;
        this.route(member, { x: width + 24, y: groundY });
      }
    }
  }

  // Plans a walk from where the member is now. Changing level always goes by the ladder on the scaffold.
  route(member, spot) {
    if (member.goal && member.goal.x === spot.x && member.goal.y === spot.y) {
      member.goal = spot;
      return;
    }
    member.goal = spot;
    member.path = member.y === spot.y
      ? [spot]
      : [{ x: LADDER_X, y: member.y }, { x: LADDER_X, y: spot.y }, spot];
  }

  update(dt) {
    for (const [id, member] of this.members) {
      let budget = WALK_SPEED * dt;
      while (budget > 0 && member.path.length) {
        const next = member.path[0];
        const dx = next.x - member.x;
        const dy = next.y - member.y;
        const distance = Math.hypot(dx, dy);
        if (distance <= budget) {
          member.x = next.x;
          member.y = next.y;
          member.path.shift();
          budget -= distance;
        } else {
          member.x += (dx / distance) * budget;
          member.y += (dy / distance) * budget;
          if (dx !== 0) member.facingLeft = dx < 0;
          budget = 0;
        }
      }

      // Arrived: look at the work (the board, the machine, the office), not where it came from.
      if (!member.path.length && member.goal && typeof member.goal.faceLeft === "boolean") {
        member.facingLeft = member.goal.faceLeft;
      }

      if (member.leaving && !member.path.length) this.members.delete(id);
    }
  }

  animFor(member) {
    if (member.path.length) return "walk";
    return member.actor.action === "build" && member.actor.activity === "working" ? "work" : "idle";
  }

  draw(ctx, ms) {
    // Lower on screen is closer to the viewer: draw it last.
    const order = [...this.members.values()].sort((a, b) => a.y - b.y);
    for (const member of order) {
      const anim = this.skin.anim(member.character, this.animFor(member));
      drawAnim(ctx, anim, ms, member.x, member.y, member.facingLeft);
    }
  }

  // Positions for the name tags and speech bubbles, in scene pixels (feet position).
  tags() {
    return [...this.members.values()].map((m) => ({
      id: m.actor.id,
      label: m.actor.name ?? m.actor.role,
      say: m.actor.say ?? null,
      activity: m.actor.activity,
      x: m.x,
      y: m.y,
    }));
  }
}
