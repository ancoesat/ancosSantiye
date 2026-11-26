// The setup page: what /diagnose found, one card per tool. Text goes in with textContent only.

function make(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = String(text);
  return node;
}

const STATUS_CLASS = {
  connected: "ok",
  "partly connected": "warn",
  "not connected": "warn",
  "found, not supported yet": "quiet",
  "not found": "quiet",
};

function card(tool) {
  const node = make("div", "tool");
  const head = make("div", "tool-head");
  head.append(make("strong", null, tool.name), make("span", `badge ${STATUS_CLASS[tool.status] ?? "quiet"}`, tool.status));
  node.append(head);
  if (Array.isArray(tool.foundBy) && tool.foundBy.length > 0) node.append(make("p", "meta", `Found: ${tool.foundBy.join(", ")}`));
  if (tool.lastEvent) node.append(make("p", "meta", `Last event: ${new Date(tool.lastEvent).toLocaleString()}`));
  for (const note of Array.isArray(tool.notes) ? tool.notes : []) node.append(make("p", "note", note));
  return node;
}

async function load() {
  const root = document.getElementById("tools");
  try {
    const tools = await (await fetch("/diagnose")).json();
    const found = tools.filter((t) => t.found);
    const missing = tools.filter((t) => !t.found);
    root.replaceChildren(...found.map(card));
    if (found.length === 0) root.append(make("p", "empty", "No AI coding tools found."));
    if (missing.length > 0) root.append(make("p", "meta", `Not found: ${missing.map((t) => t.name).join(", ")}`));
  } catch {
    root.replaceChildren(make("p", "empty", "The server did not answer."));
  }
}

load();
