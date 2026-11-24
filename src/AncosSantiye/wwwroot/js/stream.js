// Live link to the server: a "snapshot" first, then every "event".
// The browser reconnects on its own when the server comes back.
export function connect({ onSnapshot, onEvent, onStatus }) {
  const source = new EventSource("/events");
  source.addEventListener("open", () => onStatus(true));
  source.addEventListener("error", () => onStatus(false));
  source.addEventListener("snapshot", (e) => onSnapshot(JSON.parse(e.data)));
  source.addEventListener("event", (e) => onEvent(JSON.parse(e.data)));
  return source;
}
