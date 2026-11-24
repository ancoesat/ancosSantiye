// A fake site for ?demo: lets you see the scene without Claude Code running.
export function demoState(name) {
  const actor = (id, role, name, activity) => ({ id, role, name, activity });
  const actors = {
    chief: actor("chief", "chief", "Claude Code", "idle"),
    a1: actor("a1", "worker", "Explore", "working"),
    a2: actor("a2", "worker", "Plan", "idle"),
    ds: actor("ds", "subcontractor", "DeepSeek", "working"),
    rv: actor("rv", "inspector", "denetci", "idle"),
    gm: actor("gm", "supplier", "Gemini", "idle"),
  };
  const at = "2026-01-01T00:00:00Z";
  const piece = (id, status, verdict = null) => ({ id, status, verdict, updated: at });
  const gate = (id) => ({ id, piece: null, label: id, since: at });

  const states = {
    empty: {
      stage: null,
      actors,
      pieces: {},
      gates: {},
      tasksPlanned: 0,
      tasksDone: 0,
      usage: {},
    },
    r1: {
      stage: "R1",
      actors,
      pieces: {},
      gates: {},
      tasksPlanned: 3,
      tasksDone: 0,
      usage: {},
    },
    r2: {
      stage: "R2",
      actors,
      pieces: {},
      gates: {},
      tasksPlanned: 4,
      tasksDone: 1,
      usage: {},
    },
    r3: {
      stage: "R3",
      actors,
      pieces: {},
      gates: {},
      tasksPlanned: 6,
      tasksDone: 3,
      usage: {},
    },
    build: {
      stage: "F4",
      actors,
      pieces: {
        p01: piece("p01", "bitti"),
        p02: piece("p02", "onay bekliyor"),
        p03: piece("p03", "brif"),
        p04: piece("p04", "bitti"),
        p05: piece("p05", "bitti", "DÜZELT"),
        p06: piece("p06", "teslim"),
        p07: piece("p07", "bitti"),
        p08: piece("p08", "sahada"),
        p09: piece("p09", "denetimde", "DUZELT"),
        p10: piece("p10", null),
      },
      gates: { g1: gate("g1"), g2: gate("g2") },
      tasksPlanned: 10,
      tasksDone: 6,
      usage: {},
    },
    done: {
      stage: "done",
      actors,
      pieces: {
        p01: piece("p01", "bitti"),
        p02: piece("p02", "bitti"),
        p03: piece("p03", "bitti"),
        p04: piece("p04", "bitti"),
        p05: piece("p05", "bitti"),
        p06: piece("p06", "bitti"),
        p07: piece("p07", "bitti"),
        p08: piece("p08", "bitti"),
        p09: piece("p09", "bitti"),
        p10: piece("p10", "bitti"),
        p11: piece("p11", "bitti"),
        p12: piece("p12", "bitti"),
      },
      gates: {},
      tasksPlanned: 12,
      tasksDone: 12,
      usage: {},
    },
  };

  const key = String(name ?? "").trim().toLowerCase();
  return states[key] ?? states.build;
}
