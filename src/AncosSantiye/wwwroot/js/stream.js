// Live link to the server: a "snapshot" first, then every "event".
// The browser reconnects on its own when the server comes back.
// The snapshot is of one project (or the latest); events come from every project.
export function connect({ project, onSnapshot, onEvent, onStatus }) {
  const source = new EventSource(project ? `/events?project=${encodeURIComponent(project)}` : "/events");
  source.addEventListener("open", () => onStatus(true));
  source.addEventListener("error", () => onStatus(false));
  source.addEventListener("snapshot", (e) => onSnapshot(JSON.parse(e.data)));
  source.addEventListener("event", (e) => onEvent(JSON.parse(e.data)));
  return source;
}
