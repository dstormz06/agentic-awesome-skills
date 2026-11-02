// The claims in SECURITY.md that can be checked from the files themselves: what the extension may do, and that it cannot call out.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative } from 'node:path';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const manifest = JSON.parse(readFileSync(join(root, 'manifest.json'), 'utf8'));
const files = (dir) => readdirSync(dir).flatMap((n) => (statSync(join(dir, n)).isDirectory() ? files(join(dir, n)) : [join(dir, n)]));
const sources = files(join(root, 'src')).filter((f) => /\.(js|html)$/.test(f));

test('the permissions are exactly the ones SECURITY.md lists, and there are no host permissions', () => {
  assert.deepEqual([...manifest.permissions].sort(), ['activeTab', 'contextMenus', 'scripting', 'storage']);
  assert.deepEqual(manifest.optional_permissions, ['nativeMessaging']);
  assert.equal(manifest.host_permissions, undefined);
  assert.equal(manifest.optional_host_permissions, undefined);
  assert.equal(manifest.content_scripts, undefined, 'no script runs on every page');
  assert.equal(manifest.web_accessible_resources, undefined, 'no file is exposed to web pages');
  assert.equal(manifest.externally_connectable, undefined, 'no web page can message the extension');
  assert.equal(manifest.manifest_version, 3);
});

test('extension pages cannot run remote or inline script', () => {
  const csp = manifest.content_security_policy.extension_pages;
  assert.match(csp, /script-src 'self'/);
  assert.doesNotMatch(csp, /unsafe-eval|unsafe-inline|https?:/);
  for (const f of sources.filter((s) => s.endsWith('.html'))) {
    const html = readFileSync(f, 'utf8');
    assert.doesNotMatch(html, /<script(?![^>]*\bsrc=)[^>]*>/i, `${relative(root, f)} has an inline script`);
    assert.doesNotMatch(html, /(src|href)="https?:/i, `${relative(root, f)} loads something from the network`);
  }
});

test('no source file can make a network request or load remote code', () => {
  const banned = [/\bfetch\s*\(/, /XMLHttpRequest/, /WebSocket/, /sendBeacon/, /EventSource/, /\bimportScripts\s*\(/, /\beval\s*\(/, /new Function\s*\(/, /https?:\/\/(?!www\.w3\.org)/];
  for (const f of sources) {
    const text = readFileSync(f, 'utf8').replace(/\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '');
    for (const re of banned) {
      const m = text.match(re);
      // the only URLs allowed are the schema links inside strings the code never fetches; none are expected
      assert.ok(!m, `${relative(root, f)} contains ${re}: ${m && m[0]}`);
    }
  }
});

test('the page script keeps its UI in a closed shadow root and never sets inline style attributes', () => {
  const content = readFileSync(join(root, 'src', 'content.js'), 'utf8');
  assert.match(content, /attachShadow\(\{ mode: 'closed' \}\)/);
  const ui = readFileSync(join(root, 'src', 'ui', 'card-ui.js'), 'utf8').replace(/\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '');
  assert.doesNotMatch(ui, /innerHTML|outerHTML|insertAdjacentHTML|document\.write/, 'data must reach the DOM as text, never as markup');
  assert.doesNotMatch(ui, /setAttribute\(\s*'style'/);
});

test('every file the manifest names exists, and the key gives the id the app allows', () => {
  const named = [manifest.background.service_worker, manifest.action.default_popup, manifest.options_ui.page, ...Object.values(manifest.icons), ...Object.values(manifest.action.default_icon)];
  for (const f of named) assert.ok(existsSync(join(root, f)), `${f} is missing`);
  const der = Buffer.from(manifest.key, 'base64');
  const id = [...createHash('sha256').update(der).digest('hex').slice(0, 32)].map((c) => String.fromCharCode(97 + parseInt(c, 16))).join('');
  assert.equal(id, 'kkohidikfneelkeiojlmdinpncfmgjcl');
  const app = readFileSync(join(root, '..', 'app', 'AndaCompanion', 'Services', 'NativeHostRegistration.cs'), 'utf8');
  assert.ok(app.includes(`DefaultExtensionId = "${id}"`), 'the app must allow exactly this id');
});

test('the commands and menus use only shortcuts Edge does not reserve for itself', () => {
  for (const c of Object.values(manifest.commands)) assert.ok(!/Ctrl\+Shift\+A$/i.test(c.suggested_key.default), 'Ctrl+Shift+A is Edge tab search');
  assert.deepEqual(Object.keys(manifest.commands).sort(), ['check-draft', 'peek-selection', 'scan-page']);
});
