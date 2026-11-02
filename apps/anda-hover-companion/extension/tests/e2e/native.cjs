// End to end WITH the desktop app: the real .NET executable runs as the browser's native-messaging host (stdin/stdout framing,
// the extension's origin as its first argument) and the extension draws what it answers. Linux here; the same code path is
// exercised by the Windows self-test, which starts the published exe the way the browser does.
const h = require('./harness.cjs');
const path = require('path'), fs = require('fs'), os = require('os');

const REPO = path.resolve(__dirname, '..', '..', '..');
const DLL = path.join(REPO, 'app', 'AndaCompanion', 'bin', 'Debug', 'net8.0', 'AndaCompanion.dll');
const HOST = 'com.anda_hover_companion.host';
if (!fs.existsSync(DLL)) { console.error('Build the app first: dotnet build app/AndaCompanion'); process.exit(2); }

function installHost(userData, { origin = `chrome-extension://${h.EXPECTED_ID}/`, home }) {
  const dir = path.join(userData, 'NativeMessagingHosts');
  fs.mkdirSync(dir, { recursive: true });
  const wrapper = path.join(home, 'host.sh');
  fs.writeFileSync(wrapper, `#!/bin/sh\nexport ANDA_COMPANION_HOME="${path.join(home, 'data')}"\nexec dotnet "${DLL}" "$@"\n`, { mode: 0o755 });
  fs.writeFileSync(path.join(dir, HOST + '.json'), JSON.stringify({ name: HOST, description: 'test', path: wrapper, type: 'stdio', allowed_origins: [origin] }));
}

(async () => {
  const home = fs.mkdtempSync(path.join(os.tmpdir(), 'anda-home-'));
  fs.mkdirSync(path.join(home, 'data'), { recursive: true });
  const csv = path.join(home, 'Together.csv');
  fs.writeFileSync(csv, 'ANDA,Product,Applicant,Goal date,Forecast date,Status\n930001,Together drug,Birch,2026-10-03,2026-10-17,In review\n930002,Quiet drug,Birch,2027-03-02,2027-03-01,Waiting\n');
  // the app's own settings: its portfolio, and "peek first" as its consent model (ConsentModel.PeekFirst = 1)
  fs.writeFileSync(path.join(home, 'data', 'settings.json'), JSON.stringify({ PortfolioPath: csv, Consent: 1, FirstRunDone: true }));

  const { server, base } = await h.servePages({ '/': { body: '<!doctype html><meta charset=utf-8><body style="padding:40px;font:16px sans-serif"><p>Check <span id="t">930001</span> and <span id="d">216482</span> and <span id="q">930002</span>.</p><textarea id="draft" style="width:500px;height:80px">ANDA 930001 is on track and its goal date is Oct 20, 2026.</textarea></body>' } });
  let { context, worker, id } = await h.launch({ native: true, beforeLaunch: (ud) => installHost(ud, { home }) });
  const page = await context.newPage();
  await page.goto(base + '/'); await page.bringToFront();
  const popup = await context.newPage();
  const open = async () => { await popup.goto(`chrome-extension://${id}/src/popup.html`); };
  const text = async (loc) => (await loc.first().innerText()).replace(/\s+/g, ' ');

  await h.setSettings(worker, { dataSource: 'app', followApp: true });

  await open();
  await popup.locator('#source').filter({ hasText: 'Source:' }).waitFor();
  await popup.waitForFunction(() => document.getElementById('source').textContent.includes('Your portfolio: Together.csv'), null, { timeout: 15000 });
  h.ok(true, 'the popup says the data is the desktop app\'s portfolio: ' + (await popup.locator('#source').innerText()));

  await popup.fill('#num', '930001'); await popup.keyboard.press('Enter');
  await popup.locator('.card').waitFor({ timeout: 15000 });
  let t = await text(popup.locator('.card'));
  h.ok(t.includes('Together drug') && t.includes('YOUR FILE') && t.includes('Oct 17, 2026') && t.includes('In review'), 'a lookup is answered by the real host from the person\'s portfolio');
  h.ok(/goal date \(Sat Oct 3\) passed/i.test(t) && t.includes('OFFLINE BRIEF'), 'with the app\'s watch rules and offline brief');

  await popup.fill('#num', '216482'); await popup.keyboard.press('Enter');
  await popup.locator('#lookMsg').waitFor({ state: 'visible' });
  h.ok((await popup.locator('#lookMsg').innerText()).includes('216482 is not in your portfolio (Together.csv)'), 'a demo number is not found in the portfolio, and the app says so (no silent demo fallback)');

  await popup.click('#t-draft'); await popup.fill('#draftText', 'ANDA 930001 is on track and its goal date is Oct 20, 2026. ANDA 930002 is delayed.');
  await popup.click('#draftGo');
  await popup.locator('.find').first().waitFor({ timeout: 15000 });
  t = await text(popup.locator('#draftOut'));
  h.ok(t.includes('the draft gives the goal date as Oct 20, 2026, but the record says Oct 3, 2026') && t.includes('ANDA 930002: the draft sounds late or at risk'), 'the draft check runs in the app against the portfolio');

  await popup.click('#t-watch');
  await popup.locator('#watchOut .item').first().waitFor({ timeout: 15000 });
  h.ok((await text(popup.locator('#watchOut .item').first())).includes('930001'), 'the watch list is the app\'s, over the whole portfolio');

  // follow the app's settings: the app says "peek first", so a plain selection opens a small card, not the whisper chip
  await page.bringToFront();
  await h.select(page, '#t'); await h.trigger(worker, 'peek');
  await page.locator('.card').waitFor({ timeout: 15000 });
  h.ok((await text(page.locator('.card'))).includes('Full brief'), 'following the app: its "peek first" consent decides what opens');
  await page.keyboard.press('Escape');
  await h.setSettings(worker, { followApp: false });
  await page.bringToFront();
  await h.select(page, '#q'); await h.trigger(worker, 'peek');
  await page.locator('.chip:not(.notice)').waitFor({ timeout: 15000 });
  h.ok(true, 'with "follow the app" off, the extension\'s own whisper default applies (a quiet ANDA gets the invite)');
  await page.keyboard.press('Escape');
  await h.setSettings(worker, { followApp: true });

  // the host never lets one question's content leak into another's answer
  await page.bringToFront();
  await h.select(page, '#d'); await h.trigger(worker, 'peek');
  await page.locator('.chip.notice').waitFor({ timeout: 15000 });
  h.ok((await text(page.locator('.chip.notice'))).includes('216482 is not in your portfolio'), 'a selection of a number the app does not have is a plain notice');
  await context.close();

  // ---- the connection is not set up: a plain message, never a crash, never other data ----
  const second = await h.launch({ native: true });
  await h.setSettings(second.worker, { dataSource: 'app' });
  const p2 = await second.context.newPage();
  await p2.goto(`chrome-extension://${second.id}/src/popup.html`);
  await p2.fill('#num', '216482'); await p2.keyboard.press('Enter');
  await p2.locator('#lookMsg').waitFor({ state: 'visible', timeout: 15000 });
  h.ok((await p2.locator('#lookMsg').innerText()).includes('connection is not set up'), 'host not installed: ' + (await p2.locator('#lookMsg').innerText()));
  await second.context.close();

  // ---- the host manifest lists a different extension: the browser refuses, and the message says so ----
  const third = await h.launch({ native: true, beforeLaunch: (ud) => installHost(ud, { home, origin: 'chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/' }) });
  await h.setSettings(third.worker, { dataSource: 'app' });
  const p3 = await third.context.newPage();
  await p3.goto(`chrome-extension://${third.id}/src/popup.html`);
  await p3.fill('#num', '930001'); await p3.keyboard.press('Enter');
  await p3.locator('#lookMsg').waitFor({ state: 'visible', timeout: 15000 });
  h.ok(/does not allow this extension/.test(await p3.locator('#lookMsg').innerText()), 'a host that lists another extension refuses this one: ' + (await p3.locator('#lookMsg').innerText()));
  await third.context.close();

  server.close();
  process.exit(h.done() ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(2); });
