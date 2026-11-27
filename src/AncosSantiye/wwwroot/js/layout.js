// Pure: the server state goes in, the building model comes out. No imports, no DOM, no canvas.

const LEFT = 96;
const SLOT = 16;
const FLOOR_HEIGHT = 32;
const GROUND = 152;
const SLOTS_PER_FLOOR = 6;
const MAX_FLOORS = 4;

function emptyModel() {
  return {
    phase: "empty",
    floors: [],
    showCrane: false,
    showScaffold: false,
    truckParked: false,
    gateCount: 0,
    craneTarget: null,
  };
}

// A stage line is free text ("F4", "P6 part 1 approved, waiting for Esat to push"). Only its first word counts.
function phaseOf(stage) {
  const value = String(stage ?? "").trim().toLowerCase().split(/[\s:,.(]/)[0];
  if (!value) return "empty";
  if (value === "r1") return "foundation";
  if (value === "r2") return "frame";
  if (value === "r3") return "ready";
  if (value === "done" || value === "bitti") return "done";
  if (/^[fp]\d/.test(value)) return "building";
  return "empty";
}

// The verdict wins: a torn piece stays torn whatever its status says.
export function lookOf(status, verdict) {
  const v = String(verdict ?? "").trim().toUpperCase();
  if (v === "DÜZELT" || v === "DUZELT") return "torn";

  const s = String(status ?? "").trim().toLowerCase();
  if (s === "bitti") return "solid";
  if (s === "onay bekliyor" || s === "denetimde" || s === "teslim") return "pending";
  if (s === "sahada") return "half";
  return "planned";
}

// Finished work at the bottom, open work on top, like a real building. With more pieces than slots, the
// open ones always get a place and the finished ones fill what is left: the Pieces panel lists them all.
const ORDER = { solid: 0, torn: 1, pending: 2, half: 3, planned: 4 };

function slotsFromPieces(pieces) {
  const slots = Object.entries(pieces)
    .map(([key, piece]) => ({
      id: String(piece?.id ?? key),
      look: lookOf(piece?.status, piece?.verdict),
    }))
    .sort((a, b) => ORDER[a.look] - ORDER[b.look] || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
  const room = SLOTS_PER_FLOOR * MAX_FLOORS;
  if (slots.length <= room) return slots;
  const open = slots.filter((slot) => slot.look !== "solid").slice(-room);
  const solid = slots.filter((slot) => slot.look === "solid").slice(0, room - open.length);
  return [...solid, ...open];
}

function slotsFromTasks(tasksDone, tasksPlanned) {
  const done = Math.max(0, Math.floor(Number(tasksDone) || 0));
  const planned = Math.max(0, Math.floor(Number(tasksPlanned) || 0));
  const slots = [];
  for (let i = 0; i < done; i += 1) slots.push({ id: `task-${i}`, look: "solid" });
  for (let i = 0; i < planned - done; i += 1) slots.push({ id: `task-${done + i}`, look: "planned" });
  return slots;
}

export function buildingFrom(state) {
  const model = emptyModel();
  if (!state || typeof state !== "object") return model;

  model.phase = phaseOf(state.stage);

  // A status table is the progress whatever its stage line says: every piece done means a finished building.
  const pieceList = state.pieces && typeof state.pieces === "object" ? Object.values(state.pieces) : [];
  if (pieceList.length > 0 && (model.phase === "empty" || model.phase === "building")) {
    model.phase = pieceList.every((piece) => lookOf(piece?.status, piece?.verdict) === "solid") ? "done" : "building";
  }

  // No orchestra stage: the session's own task list is the progress. Open tasks mean building,
  // a fully done list means a finished building. No tasks means no building: nothing is pretended.
  const planned = Math.max(0, Math.floor(Number(state.tasksPlanned) || 0));
  const done = Math.max(0, Math.floor(Number(state.tasksDone) || 0));
  if (model.phase === "empty" && planned > 0) model.phase = done >= planned ? "done" : "building";

  const pieces = state.pieces && typeof state.pieces === "object" ? state.pieces : {};
  const hasPieces = Object.keys(pieces).length > 0;
  const slots = hasPieces
    ? slotsFromPieces(pieces)
    : slotsFromTasks(state.tasksDone, state.tasksPlanned);

  const floors = [];
  for (let i = 0; i < slots.length; i += SLOTS_PER_FLOOR) {
    floors.push(slots.slice(i, i + SLOTS_PER_FLOOR));
  }
  model.floors = floors.slice(0, MAX_FLOORS);

  const actors = state.actors && typeof state.actors === "object" ? state.actors : {};
  model.truckParked = Object.values(actors).some(
    (actor) => String(actor?.role ?? "").trim().toLowerCase() === "supplier",
  );
  model.showScaffold = model.phase === "frame" || model.phase === "ready" || model.phase === "building";
  model.showCrane = model.phase === "ready" || model.phase === "building";

  const gates = state.gates && typeof state.gates === "object" ? state.gates : {};
  model.gateCount = Object.keys(gates).length;

  for (let floor = 0; floor < model.floors.length && !model.craneTarget; floor += 1) {
    const row = model.floors[floor];
    for (let index = 0; index < row.length; index += 1) {
      if (row[index].look !== "half") continue;
      model.craneTarget = {
        x: LEFT + index * SLOT + SLOT / 2,
        y: GROUND - FLOOR_HEIGHT * (floor + 1),
      };
      break;
    }
  }

  return model;
}
