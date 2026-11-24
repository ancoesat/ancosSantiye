// Commit and push from the screen. Only shown when the profile turns actions on (/config says so).
// You pick the files one by one; there is no "all". Every action asks first with the browser's own confirm.
// Text from git or the server goes in with textContent only.

function make(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = String(text);
  return node;
}

function button(text, onClick) {
  const node = make("button", null, text);
  node.type = "button";
  node.addEventListener("click", onClick);
  return node;
}

export async function setupActions(root) {
  let config;
  try {
    const response = await fetch("/config");
    config = await response.json();
  } catch {
    return;
  }
  if (!config || !config.actionsEnabled || !config.token) return;

  const repos = Array.isArray(config.repos) ? config.repos : [];
  let repo = 0;
  let busy = false;

  const picker = make("select");
  repos.forEach((name, index) => picker.append(new Option(String(name), String(index))));
  picker.hidden = repos.length < 2;
  picker.addEventListener("change", () => {
    repo = Number(picker.value);
    refresh();
  });

  const files = make("div", "action-files");
  const message = make("textarea");
  message.rows = 3;
  message.placeholder = "Commit message";
  const result = make("p", "action-result");
  const commit = button("Commit", onCommit);
  const push = button("Push", onPush);
  const reload = button("Refresh", () => refresh());
  const bar = make("div", "panel-controls");
  bar.append(reload, commit, push);

  root.replaceChildren(make("h2", null, "Commit"), picker, files, message, bar, result);
  root.hidden = false;

  async function post(path, body) {
    const response = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Santiye-Token": config.token },
      body: JSON.stringify(body),
    });
    let data = {};
    try {
      data = await response.json();
    } catch {
      // A refused request (403, 401, 415) has no body.
    }
    return { ok: response.ok && data.ok === true, message: data.message ?? `Refused (${response.status})`, files: data.files };
  }

  function say(text, ok) {
    result.textContent = text;
    result.className = ok ? "action-result ok" : "action-result off";
  }

  function picked() {
    return [...files.querySelectorAll("input:checked")].map((box) => box.value);
  }

  async function refresh() {
    const answer = await post("/actions/status", { repo });
    files.replaceChildren();
    if (!answer.ok) {
      say(answer.message, false);
      return;
    }
    if (!answer.files || answer.files.length === 0) {
      files.append(make("p", "empty", "Nothing to commit."));
      return;
    }
    for (const file of answer.files) {
      const row = make("label", "row action-file");
      const box = make("input");
      box.type = "checkbox";
      box.value = String(file.path);
      row.append(box, make("span", "action-code", String(file.code).trim()), make("span", "row-main", String(file.path)));
      files.append(row);
    }
  }

  async function run(work) {
    if (busy) return;
    busy = true;
    commit.disabled = push.disabled = true;
    try {
      await work();
    } catch {
      say("The server did not answer.", false);
    } finally {
      busy = false;
      commit.disabled = push.disabled = false;
    }
  }

  function onCommit() {
    run(async () => {
      const paths = picked();
      const text = message.value.trim();
      if (paths.length === 0) return say("Pick at least one file.", false);
      if (!text) return say("Write a commit message.", false);

      const shown = paths.slice(0, 20).join("\n") + (paths.length > 20 ? `\n...and ${paths.length - 20} more` : "");
      const lines = text.split("\n");
      const said = lines.slice(0, 10).join("\n") + (lines.length > 10 ? "\n..." : "");
      const sure = window.confirm(`Commit ${paths.length} file(s) in ${repos[repo]}?\n\n${shown}\n\nMessage:\n${said}`);
      if (!sure) return;

      const answer = await post("/actions/commit", { repo, paths, message: text });
      say(answer.message, answer.ok);
      if (answer.ok) message.value = "";
      await refresh();
    });
  }

  function onPush() {
    run(async () => {
      if (!window.confirm(`Push ${repos[repo]} to its upstream branch?`)) return;
      say("Pushing...", true);
      const answer = await post("/actions/push", { repo });
      say(answer.message, answer.ok);
    });
  }

  await refresh();
}
