// A fake site for ?demo: lets you see the scene without Claude Code running.
function minutesAgo(minutes) {
  return new Date(Date.now() - minutes * 60000).toISOString();
}

function demoUsage() {
  const now = Math.floor(Date.now() / 1000);
  return {
    model: "Opus",
    contextUsedPercentage: "34",
    totalInputTokens: "1284102",
    totalOutputTokens: "318842",
    costUsd: "1.27",
    fiveHourUsedPercentage: "23.5",
    fiveHourResetsAt: String(now + 2 * 60 * 60),
    sevenDayUsedPercentage: "41.2",
    sevenDayResetsAt: String(now + 3 * 24 * 60 * 60),
  };
}

export function demoState(name) {
  const actor = (id, role, name, activity, say = null, action = null) => ({ id, role, name, activity, say, action });
  const actors = {
    chief: actor("chief", "chief", "Claude Code", "idle", "Waiting for you", "wait"),
    a1: actor("a1", "worker", "Explore", "working", "Reading SPEC.md", "read"),
    a2: actor("a2", "worker", "Plan", "idle", "Calling Explore", "plan"),
    ds: actor("ds", "subcontractor", "DeepSeek", "working", "Editing panels.js", "build"),
    rv: actor("rv", "inspector", "denetci", "working", "Run the unit tests", "run"),
    gm: actor("gm", "supplier", "Gemini", "idle"),
  };
  const piece = (id, status, verdict = null, minutes = 0) => ({
    id,
    status,
    verdict,
    updated: minutesAgo(minutes),
  });
  const gate = (id, label = id, pieceId = null, minutes = 0) => ({
    id,
    piece: pieceId,
    label,
    since: minutesAgo(minutes),
  });
  const usage = demoUsage();

  const states = {
    empty: {
      stage: null,
      actors,
      pieces: {},
      gates: {},
      tasksPlanned: 0,
      tasksDone: 0,
      usage,
    },
    r1: {
      stage: "R1",
      actors,
      pieces: {},
      gates: {},
      tasksPlanned: 3,
      tasksDone: 0,
      usage,
    },
    r2: {
      stage: "R2",
      actors,
      pieces: {},
      gates: {},
      tasksPlanned: 4,
      tasksDone: 1,
      usage,
    },
    r3: {
      stage: "R3",
      actors,
      pieces: {},
      gates: {},
      tasksPlanned: 6,
      tasksDone: 3,
      usage,
    },
    build: {
      stage: "F4",
      actors,
      pieces: {
        p01: piece("p01", "bitti", null, 14),
        p02: piece("p02", "onay bekliyor", null, 2),
        p03: piece("p03", "brif", null, 9),
        p04: piece("p04", "bitti", null, 25),
        p05: piece("p05", "bitti", "DÜZELT", 4),
        p06: piece("p06", "teslim", null, 1),
        p07: piece("p07", "bitti", null, 40),
        p08: piece("p08", "sahada", null, 6),
        p09: piece("p09", "denetimde", "DUZELT", 3),
        p10: piece("p10", null, null, 55),
      },
      gates: {
        g1: gate("g1", "Approve plan", "p02", 12),
        g2: gate("g2", "Review diff", "p09", 3),
      },
      tasksPlanned: 10,
      tasksDone: 6,
      currentTask: "Wire the panels to live data",
      usage,
    },
    done: {
      stage: "done",
      actors,
      pieces: {
        p01: piece("p01", "bitti", null, 50),
        p02: piece("p02", "bitti", null, 45),
        p03: piece("p03", "bitti", null, 40),
        p04: piece("p04", "bitti", null, 35),
        p05: piece("p05", "bitti", null, 30),
        p06: piece("p06", "bitti", null, 25),
        p07: piece("p07", "bitti", null, 20),
        p08: piece("p08", "bitti", null, 15),
        p09: piece("p09", "bitti", null, 10),
        p10: piece("p10", "bitti", null, 5),
        p11: piece("p11", "bitti", null, 3),
        p12: piece("p12", "bitti", null, 1),
      },
      gates: {},
      tasksPlanned: 12,
      tasksDone: 12,
      usage,
    },
  };

  const key = String(name ?? "").trim().toLowerCase();
  return states[key] ?? states.build;
}

export function demoEvents() {
  const now = Date.now();
  const chief = { id: "chief", role: "chief", name: "Claude Code" };
  const explore = { id: "a1", role: "worker", name: "Explore" };
  const plan = { id: "a2", role: "worker", name: "Plan" };
  const denetci = { id: "rv", role: "inspector", name: "denetci" };
  const rows = [
    [9, 42, chief, "session.start", null, null],
    [9, 10, chief, "stage.changed", null, { stage: "F4" }],
    [8, 40, explore, "agent.start", null, null],
    [8, 30, explore, "tool.start", null, { tool: "Grep" }],
    [7, 55, explore, "tool.end", null, { tool: "Grep" }],
    [6, 30, plan, "agent.start", null, null],
    [6, 20, plan, "tool.start", null, { tool: "Read" }],
    [5, 48, plan, "tool.end", null, { tool: "Read" }],
    [4, 45, chief, "tool.start", "p05", { tool: "Edit" }],
    [4, 12, chief, "piece.status", "p05", { status: "bitti" }],
    [3, 30, denetci, "review.verdict", "p05", { verdict: "DUZELT" }],
    [2, 40, chief, "tool.start", "p09", { tool: "Bash" }],
    [1, 50, denetci, "review.verdict", "p09", { verdict: "DUZELT" }],
    [0, 30, chief, "gate.pending", "p09", { gate: "g2", label: "Review diff" }],
  ];

  return rows.map(([minutes, seconds, actor, kind, pieceId, data], index) => ({
    id: `demo-${index}`,
    ts: new Date(now - minutes * 60000 - seconds * 1000).toISOString(),
    source: "claude-code",
    project: "ancosSantiye",
    session: "demo",
    actor,
    kind,
    piece: pieceId,
    data,
  }));
}

// Only the build demo adds a gate after the page opens, to show the alert path.
export function demoLateGate(name) {
  if (String(name ?? "").trim().toLowerCase() !== "build") return null;
  return { delayMs: 5000, id: "K3-demo", piece: "p03", label: "approval note" };
}
