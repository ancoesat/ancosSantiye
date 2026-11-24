// Alerts once when a new gate starts waiting. Every outside API is optional and wrapped.
export class Notifier {
  constructor(storage) {
    if (storage === undefined) {
      try {
        storage = window.localStorage;
      } catch {
        storage = null;
      }
    }
    this.storage = storage;
    this.seen = new Set();
    this.checked = false;
    this.audio = null;
  }

  get soundOn() {
    try {
      const value = this.storage ? this.storage.getItem("santiye.sound") : null;
      return value !== "off" && value !== "0" && value !== "false";
    } catch {
      return true;
    }
  }

  set soundOn(value) {
    try {
      if (this.storage) this.storage.setItem("santiye.sound", value ? "on" : "off");
    } catch {
      // No storage means the setting lives for this page only.
    }
  }

  check(state) {
    const gates =
      state && typeof state === "object" && state.gates && typeof state.gates === "object"
        ? state.gates
        : {};

    const current = new Map();
    for (const [key, raw] of Object.entries(gates)) {
      const gate = raw && typeof raw === "object" ? raw : {};
      const id = gate.id === undefined || gate.id === null || String(gate.id).trim() === ""
        ? key
        : String(gate.id);
      current.set(id, gate);
    }

    // A gate that clears leaves the set, so the same id alerts again when it comes back.
    for (const id of [...this.seen]) {
      if (!current.has(id)) this.seen.delete(id);
    }

    const fresh = [];
    for (const [id, gate] of current) {
      if (!this.seen.has(id)) fresh.push([id, gate]);
    }

    // The first check only learns what is already waiting: opening the page never alerts.
    if (!this.checked) {
      this.checked = true;
      for (const [id] of fresh) this.seen.add(id);
      return;
    }

    for (const [id, gate] of fresh) {
      this.seen.add(id);
      this.notifyGate(gate, id);
    }
  }

  async enableNotifications() {
    try {
      const api = this.notificationApi();
      if (!api) return null;
      if (api.permission === "granted") return "granted";
      return await api.requestPermission();
    } catch {
      return null;
    }
  }

  notificationApi() {
    try {
      return typeof window.Notification === "function" ? window.Notification : null;
    } catch {
      return null;
    }
  }

  notifyGate(gate, id) {
    const label = gate && gate.label !== undefined && gate.label !== null && String(gate.label).trim() !== ""
      ? String(gate.label)
      : id;

    if (this.soundOn) this.beep();

    try {
      const api = this.notificationApi();
      if (api && api.permission === "granted") {
        new api("ancosSantiye", { body: label });
      }
    } catch {
      // No notification API or permission: the gate still shows in the panel.
    }
  }

  beep() {
    try {
      const Ctor = window.AudioContext || window.webkitAudioContext;
      if (typeof Ctor !== "function") return;
      if (!this.audio) this.audio = new Ctor();

      const audio = this.audio;
      if (audio.state === "suspended" && typeof audio.resume === "function") {
        const resumed = audio.resume();
        if (resumed && typeof resumed.catch === "function") resumed.catch(() => {});
      }

      const oscillator = audio.createOscillator();
      const gain = audio.createGain();
      const start = audio.currentTime;
      oscillator.type = "sine";
      oscillator.frequency.setValueAtTime(880, start);
      gain.gain.setValueAtTime(0.1, start);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.15);
      oscillator.connect(gain);
      gain.connect(audio.destination);
      oscillator.start(start);
      oscillator.stop(start + 0.15);
      oscillator.onended = () => {
        try {
          oscillator.disconnect();
          gain.disconnect();
        } catch {
          // The context may already be closed.
        }
      };
    } catch {
      // No WebAudio: silence is fine.
    }
  }
}
