// The line to the desktop app, over the browser's native messaging. It is optional: the extension asks for the "nativeMessaging"
// permission only when the person turns the connection on, and everything still works without it.
export const HOST_NAME = 'com.anda_hover_companion.host';

export class NativeError extends Error {
  constructor(code, message) { super(message); this.code = code; }
}

const friendly = (raw) => {
  const t = String(raw || '');
  if (/not found/i.test(t)) return new NativeError('not-installed', 'The desktop app connection is not set up. In the app, open Settings and choose Edge extension, Connect.');
  if (/forbidden|not allowed|access/i.test(t)) return new NativeError('forbidden', 'The desktop app does not allow this extension. In the app, connect it again with this extension\'s ID.');
  return new NativeError('disconnected', 'The desktop app did not answer. Is it installed on this computer?');
};

export class NativeBridge {
  /** @param api the chrome object (or a fake in tests) */
  constructor(api, hostName = HOST_NAME) { this.api = api; this.hostName = hostName; this.port = null; this.nextId = 1; this.pending = new Map(); }

  async hasPermission() {
    try { return await this.api.permissions.contains({ permissions: ['nativeMessaging'] }); } catch (e) { return false; }
  }

  #connect() {
    if (this.port) return this.port;
    const port = this.api.runtime.connectNative(this.hostName);
    port.onMessage.addListener((msg) => {
      const p = msg && this.pending.get(msg.id);
      if (!p) return;
      this.pending.delete(msg.id); clearTimeout(p.timer); p.resolve(msg);
    });
    port.onDisconnect.addListener(() => {
      const err = friendly(this.api.runtime.lastError && this.api.runtime.lastError.message);
      this.port = null;
      for (const [, p] of this.pending) { clearTimeout(p.timer); p.reject(err); }
      this.pending.clear();
    });
    this.port = port;
    return port;
  }

  /** Sends one request and resolves with the host's reply object. Rejects with a NativeError. */
  request(body, timeoutMs = 6000) {
    return new Promise((resolve, reject) => {
      if (!this.api.runtime.connectNative) { reject(new NativeError('not-permitted', 'The desktop app connection is turned off. Choose “Allow the connection to the desktop app…” on the extension\'s Settings page.')); return; }
      let port;
      try { port = this.#connect(); } catch (e) { reject(friendly(e && e.message)); return; }
      const id = this.nextId++;
      const timer = setTimeout(() => { this.pending.delete(id); reject(new NativeError('timeout', 'The desktop app took too long to answer.')); }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      try { port.postMessage({ ...body, id }); } catch (e) { this.pending.delete(id); clearTimeout(timer); this.port = null; reject(friendly(e && e.message)); }
    });
  }

  close() { if (this.port) { try { this.port.disconnect(); } catch (e) { /* already closed */ } this.port = null; } }
}
