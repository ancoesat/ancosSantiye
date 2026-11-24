// A fake site for ?demo: lets you see the scene without Claude Code running.
export function demoState() {
  const actor = (id, role, name, activity) => ({ id, role, name, activity });
  return {
    stage: "F4",
    actors: {
      chief: actor("chief", "chief", "Claude Code", "idle"),
      a1: actor("a1", "worker", "Explore", "working"),
      a2: actor("a2", "worker", "Plan", "idle"),
      ds: actor("ds", "subcontractor", "DeepSeek", "working"),
      rv: actor("rv", "inspector", "denetci", "idle"),
      gm: actor("gm", "supplier", "Gemini", "idle"),
    },
    pieces: {},
    gates: {},
    usage: {},
  };
}
