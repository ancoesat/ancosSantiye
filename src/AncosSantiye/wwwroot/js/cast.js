import { drawAnim } from "./sprites.js";

// Where each role stands when it is not walking. Workers line up from the left of the building.
const SLOTS = {
  chief: 226,
  worker: 104,
  subcontractor: 150,
  inspector: 196,
  architect: 70,
  supplier: 46,
};
const WORKER_GAP = 22;
const WALK_SPEED = 40; // pixels per second
const OFF_LEFT = -24;

// The people on screen. Follows SiteState.actors: newcomers walk in from the left,
// leavers walk out, everyone else animates by activity.
export class Cast {
  constructor(skin) {
    this.skin = skin;
    this.members = new Map();
  }

  sync(actors, width) {
    const ids = new Set(Object.keys(actors ?? {}));
    let workerIndex = 0;
    for (const actor of Object.values(actors ?? {}).sort((a, b) => a.id.localeCompare(b.id))) {
      const slot = actor.role === "worker" ? SLOTS.worker + workerIndex++ * WORKER_GAP : SLOTS[actor.role] ?? SLOTS.worker;
      const member = this.members.get(actor.id);
      if (member) {
        Object.assign(member, { actor, target: slot, leaving: false });
      } else {
        this.members.set(actor.id, {
          actor,
          character: this.skin.characterFor(actor.role, actor.name ?? actor.id),
          x: OFF_LEFT,
          target: slot,
          leaving: false,
          facingLeft: false,
        });
      }
    }

    for (const [id, member] of this.members) {
      if (!ids.has(id) && !member.leaving) {
        Object.assign(member, { leaving: true, target: width + 24 });
      }
    }
  }

  update(dt) {
    for (const [id, member] of this.members) {
      const distance = member.target - member.x;
      const step = Math.sign(distance) * Math.min(Math.abs(distance), WALK_SPEED * dt);
      member.x += step;
      if (step !== 0) member.facingLeft = step < 0;
      if (member.leaving && Math.abs(member.target - member.x) < 0.5) this.members.delete(id);
    }
  }

  animFor(member) {
    if (Math.abs(member.target - member.x) >= 0.5) return "walk";
    return member.actor.activity === "working" ? "work" : "idle";
  }

  draw(ctx, ms, groundY) {
    for (const member of this.members.values()) {
      const anim = this.skin.anim(member.character, this.animFor(member));
      drawAnim(ctx, anim, ms, member.x, groundY, member.facingLeft);
    }
  }

  // Positions for the name tags, in scene pixels.
  tags() {
    return [...this.members.values()].map((m) => ({ id: m.actor.id, label: m.actor.name ?? m.actor.role, x: m.x }));
  }
}
