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

// Tools the server can connect to. The buttons write that tool's config file, so each one asks first.
const CONNECTABLE = {
  "claude-code": "adds hooks and a status line to ~/.claude/settings.json (a backup is made first)",
  agy: "adds an \"ancos-santiye\" entry to ~/.gemini/config/hooks.json (a backup is made first)",
  opencode: "writes the plugin file ~/.config/opencode/plugins/ancos-santiye.js",
};

let token = null;

async function call(path, tool) {
  token ??= (await (await fetch("/config")).json()).setupToken;
  const response = await fetch(path, {
    method: "POST",
    headers: { "content-type": "application/json", "X-Santiye-Token": token },
    body: JSON.stringify({ tool }),
  });
  const body = await response.json().catch(() => ({}));
  return { ok: response.ok && body.ok, message: body.message ?? `The server answered ${response.status}.`, backup: body.backup };
}

function buttons(tool) {
  const what = CONNECTABLE[tool.id];
  if (!what || !tool.found) return null;
  const row = make("div", "tool-actions");
  const result = make("span", "meta");
  const connected = tool.status === "connected";
  const run = async (path, question) => {
    if (!window.confirm(question)) return;
    for (const b of row.querySelectorAll("button")) b.disabled = true;
    try {
      const answer = await call(path, tool.id);
      result.className = answer.ok ? "meta ok" : "meta off";
      result.textContent = answer.backup ? `${answer.message} Backup: ${answer.backup}` : answer.message;
      if (answer.ok) setTimeout(load, 800);
    } catch {
      result.className = "meta off";
      result.textContent = "The server did not answer.";
    } finally {
      for (const b of row.querySelectorAll("button")) b.disabled = false;
    }
  };
  const connect = make("button", "primary", connected ? "Connect again" : "Connect");
  connect.addEventListener("click", () => run("/setup/connect", `Connect ${tool.name}?\n\nThis ${what}.`));
  row.append(connect);
  if (tool.status !== "not connected") {
    const remove = make("button", null, "Disconnect");
    remove.addEventListener("click", () => run("/setup/disconnect", `Disconnect ${tool.name}?\n\nOnly ancosSantiye's own entries are removed.`));
    row.append(remove);
  }
  row.append(result);
  return row;
}

function card(tool) {
  const node = make("div", "tool");
  const head = make("div", "tool-head");
  head.append(make("strong", null, tool.name), make("span", `badge ${STATUS_CLASS[tool.status] ?? "quiet"}`, tool.status));
  node.append(head);
  if (Array.isArray(tool.foundBy) && tool.foundBy.length > 0) node.append(make("p", "meta", `Found: ${tool.foundBy.join(", ")}`));
  if (tool.lastEvent) node.append(make("p", "meta", `Last event: ${new Date(tool.lastEvent).toLocaleString()}`));
  for (const note of Array.isArray(tool.notes) ? tool.notes : []) node.append(make("p", "note", note));
  const actions = buttons(tool);
  if (actions) node.append(actions);
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
