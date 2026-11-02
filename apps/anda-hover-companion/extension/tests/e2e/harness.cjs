// Loads a copy of the extension into the Chromium that ships with Playwright (Edge is the same engine, but is not available here)
// and gives tests a page server, the extension id and a way to run the three requests without the browser's own menu or shortcut UI
// (which automation cannot press). The copy differs from the shipped extension in only these ways: it may read 127.0.0.1 without
// a user gesture, the shadow root is open so tests can look inside, and (when asked) it may use native messaging.
const { chromium } = require('playwright');
const fs = require('fs'), os = require('os'), path = require('path'), http = require('http');

const SRC = path.resolve(__dirname, '..', '..');
const EXPECTED_ID = 'kkohidikfneelkeiojlmdinpncfmgjcl';

function copyDir(from, to) {
  fs.mkdirSync(to, { recursive: true });
  for (const e of fs.readdirSync(from, { withFileTypes: true })) {
    if (e.name === 'tests' || e.name === 'node_modules') continue;
    const a = path.join(from, e.name), b = path.join(to, e.name);
    e.isDirectory() ? copyDir(a, b) : fs.copyFileSync(a, b);
  }
}

function buildTestExtension({ native = false } = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'anda-ext-'));
  copyDir(SRC, dir);
  const m = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
  m.host_permissions = ['http://127.0.0.1/*'];
  if (native) m.permissions.push('nativeMessaging');
  fs.writeFileSync(path.join(dir, 'manifest.json'), JSON.stringify(m, null, 2));
  const c = path.join(dir, 'src', 'content.js');
  const shipped = fs.readFileSync(c, 'utf8');
  if (!shipped.includes("mode: 'closed'")) throw new Error('the shipped page script must use a closed shadow root');
  fs.writeFileSync(c, shipped.replace("mode: 'closed'", "mode: 'open'"));
  return dir;
}

function servePages(pages) {
  const server = http.createServer((req, res) => {
    const p = pages[req.url.split('?')[0]];
    if (!p) { res.writeHead(404); res.end('not found'); return; }
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', ...(p.headers || {}) });
    res.end(p.body);
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve({ server, base: `http://127.0.0.1:${server.address().port}` })));
}

async function launch(opts = {}) {
  const extDir = buildTestExtension(opts);
  const userData = fs.mkdtempSync(path.join(os.tmpdir(), 'anda-profile-'));
  if (opts.beforeLaunch) opts.beforeLaunch(userData);
  const context = await chromium.launchPersistentContext(userData, {
    channel: 'chromium', headless: true, args: ['--headless=new', `--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`, '--no-sandbox'],
  });
  let worker = context.serviceWorkers()[0];
  if (!worker) worker = await context.waitForEvent('serviceworker', { timeout: 15000 });
  const id = new URL(worker.url()).host;
  return { context, worker, id, userData, extDir };
}

/** Runs one of the extension's three requests against the tab that is in front, as its own menu or shortcut would. */
const trigger = (worker, kind, menuText) => worker.evaluate(async ([kind, menuText]) => {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const t = globalThis.andaTest;
  return kind === 'peek' ? t.peek(tab, menuText) : kind === 'draft' ? t.checkDraft(tab, menuText) : t.scanPage(tab);
}, [kind, menuText]);

const setSettings = (worker, patch) => worker.evaluate((p) => globalThis.andaTest.service.saveSettings(p), patch);

/** Selects the text of the first element matching a selector, the way a double-click or drag would. */
const select = (page, selector) => page.evaluate((sel) => {
  const el = document.querySelector(sel);
  const r = document.createRange(); r.selectNodeContents(el);
  const s = getSelection(); s.removeAllRanges(); s.addRange(r);
}, selector);

let failures = 0;
const ok = (cond, msg) => { console.log((cond ? 'PASS ' : 'FAIL ') + msg); if (!cond) failures++; };
const done = () => { console.log(failures ? `FAILED: ${failures}` : 'ALL PASS'); return failures; };

module.exports = { launch, servePages, trigger, setSettings, select, ok, done, EXPECTED_ID, SRC };
