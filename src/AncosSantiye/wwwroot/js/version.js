// The running version in small type next to the logo, and an Update link when GitHub has a newer one.
// The update runs on the server (download, SHA-256 check, update script); this only asks for it.

const slot = document.getElementById("version");

function make(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = String(text);
  return node;
}

async function info() {
  const response = await fetch("/version");
  return response.ok ? response.json() : null;
}

// After the server stops for the update, wait until it answers with the new version, then reload.
async function waitFor(version, status) {
  for (let i = 0; i < 90; i += 1) {
    await new Promise((resolve) => setTimeout(resolve, 2000));
    try {
      const now = await info();
      if (now?.current === version) {
        window.location.reload();
        return;
      }
    } catch {
      // Still restarting.
    }
  }
  status.textContent = "Still not back. See the update window, or start ancos-santiye again.";
}

async function update(target, link, status) {
  if (!window.confirm(`Install ancosSantiye ${target}?\n\nThe server downloads it from GitHub, checks it, stops, installs it and starts again. Takes about a minute.`)) return;
  link.hidden = true;
  status.textContent = "Updating...";
  try {
    const { setupToken } = await (await fetch("/config")).json();
    const response = await fetch("/setup/update", {
      method: "POST",
      headers: { "content-type": "application/json", "X-Santiye-Token": setupToken },
      body: "{}",
    });
    const body = await response.json().catch(() => ({}));
    status.textContent = body.message ?? `The server answered ${response.status}.`;
    if (response.ok && body.ok) waitFor(target, status);
    else link.hidden = false;
  } catch {
    status.textContent = "The server did not answer.";
    link.hidden = false;
  }
}

async function show() {
  if (!slot) return;
  let data;
  try {
    data = await info();
  } catch {
    return;
  }
  if (!data) return;
  slot.replaceChildren(make("span", "version-current", `v${data.current}`));
  if (!data.latest) return;
  const link = make("button", "version-update", `v${data.latest} available, update`);
  link.title = "Downloads the release from GitHub, checks it and restarts the server";
  const status = make("span", "version-status");
  link.addEventListener("click", () => update(data.latest, link, status));
  slot.append(link, status);
  if (data.notes) {
    const notes = make("a", "version-notes", "what's new");
    notes.href = data.notes;
    notes.target = "_blank";
    notes.rel = "noopener noreferrer";
    slot.append(notes);
  }
}

show();
// The server checks GitHub at start and every 12 hours; look again now and then.
setInterval(show, 30 * 60 * 1000);
