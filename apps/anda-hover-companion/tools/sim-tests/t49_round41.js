// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 41 in the browser simulation: what used to need a source edit is in Settings. Elsa's request body, reply path and token header,
// and MCP servers that are programs on this PC (by full path and SHA-256, started only on a second press; this page starts nothing). Words
// and rules are held to mockup/round41-golden.json, which the Windows app is also tested against.
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round41-golden.json'), 'utf8'));
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);

(async () => {
  const b = await chromium.launch(); const errs = [], requests = [];
  const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' });
  const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
  p.on('console', m => { if (m.type() === 'error') errs.push(m.type() + ': ' + m.text()); });
  p.on('request', r => { if (!r.url().startsWith('file:') && !r.url().startsWith('data:')) requests.push(r.url()); });
  await p.goto(SIM); await p.waitForTimeout(300);
  const settle = (ms = 200) => p.waitForTimeout(ms);
  const axe = async sel => {
    if (!AXE_FILE) return ['axe-core not found: set AXE_PATH'];
    await p.addScriptTag({ path: AXE_FILE });
    return p.evaluate(async s => (await axe.run(document.querySelector(s), { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map(v => v.id), sel);
  };
  const status = () => p.evaluate(() => document.getElementById('elsaStatus').textContent);
  await p.evaluate(() => { showApp('settings'); document.getElementById('secConnections').open = true; document.getElementById('elsaApi').open = true; }); await settle();

  // ---------- Elsa ----------
  eq(await p.evaluate(() => [document.getElementById('elsaHint').textContent, document.getElementById('elsaHeader').value, document.getElementById('elsaScheme').value]),
    [GOLD.elsaSaid.hint, 'Authorization', 'Bearer'], 'E1 Elsa\'s fields start blank, with the usual header, and the shared hint');
  eq(await status(), GOLD.elsaSaid.blank, 'E2 left blank, Elsa stays offline and says so');
  await p.fill('#elsaTemplate', '{"q":"{{user}}","agent":"{{agent}}"}'); await settle();
  ok((await status()).includes('reply path'), 'E3 a body without a reply path asks for it');
  await p.fill('#elsaPath', 'output.text'); await settle();
  eq(await status(), GOLD.elsaSaid.filled, 'E4 both filled in: said to be used once IT has set its variables');
  await p.fill('#elsaHeader', 'Host'); await settle();
  ok((await status()).startsWith('Host is a header this app sets itself'), 'E5 a header the app sets itself is refused, as in the app');
  await p.fill('#elsaHeader', 'Authorization'); await settle();
  eq(await p.evaluate(() => { const t = backupText(); const saved = JSON.parse(t).settings.elsa; state.elsa = { header: 'x', scheme: '', template: '', path: '' }; importBackup(t);
    const back = { ...state.elsa }; const x = JSON.parse(t); delete x.settings.elsa; importBackup(JSON.stringify(x)); return [saved.path, back.template, state.elsa.path, document.getElementById('elsaPath').value]; }),
    ['output.text', '{"q":"{{user}}","agent":"{{agent}}"}', 'output.text', 'output.text'], 'E6 Elsa\'s fields are in a backup and come back; an older backup keeps what the person has now');
  eq(await axe('#settings'), [], 'E7 accessibility scan of Settings with Elsa\'s fields open');

  // ---------- a program on this PC ----------
  await p.evaluate(() => { state.mcp = []; paintMcpList(); }); await p.click('#mcpAddLocal'); await settle();
  eq(await p.evaluate(() => [...document.querySelectorAll('#mcpList [data-mcp] [aria-label]')].map(e => e.getAttribute('aria-label'))),
    ['Use MCP server 1', 'Name of MCP server 1', 'Program of MCP server 1', 'SHA-256 of MCP server 1', 'Arguments of MCP server 1'], 'P1 a program row asks for its path, SHA-256 and arguments, each named for a screen reader');
  await p.click('#mcpList [data-mcp] input[type=checkbox]'); await settle();
  eq(await p.evaluate(() => [state.mcp[0].enabled, document.querySelector('#mcpList [role=status]').textContent]), [false, GOLD.programs[0].problem], 'P2 a program with no path cannot be switched on, and the reason is said');
  await p.fill('#mcpList [aria-label="Program of MCP server 1"]', 'C:\\Tools\\run.bat'); await p.fill('#mcpList [aria-label="SHA-256 of MCP server 1"]', 'a'.repeat(64));
  await p.click('#mcpList [data-mcp] input[type=checkbox]'); await settle();
  eq(await p.evaluate(() => state.mcp[0].enabled), false, 'P3 a script is refused: only an .exe by its full path');
  await p.fill('#mcpList [aria-label="Program of MCP server 1"]', '"C:\\Program Files\\Example\\mcp-server.exe"');
  await p.fill('#mcpList [aria-label="Arguments of MCP server 1"]', '--stdio\n\n --quiet ');
  await p.click('#mcpList [data-mcp] input[type=checkbox]'); await settle();
  eq(await p.evaluate(() => [state.mcp[0].enabled, state.mcp[0].args, connState().mcp]), [true, ['--stdio', '--quiet'], [{ name: 'Program 1', where: 'this PC: mcp-server.exe' }]],
    'P4 named as IT approved it, it can be switched on; the Connections window says it is on this PC');
  await p.click('#mcpList [data-mcp] button:has-text("Test")'); await settle();
  const t1 = await p.evaluate(() => document.querySelector('#mcpList [role=status]').textContent);
  await p.click('#mcpList [data-mcp] button:has-text("Test")'); await settle();
  eq([t1, await p.evaluate(() => document.querySelector('#mcpList [role=status]').textContent)], [GOLD.programSaid.starts, await p.evaluate(() => MCP_SAID.programSim)],
    'P5 Test says what it would start, and only a second press goes on (this page starts nothing)');
  await p.evaluate(() => openMcp('')); await settle();
  await p.click('#mwList'); await settle();
  const w1 = await p.evaluate(() => document.getElementById('mwStatus').textContent);
  await p.click('#mwList'); await settle();
  eq([w1, await p.evaluate(() => document.getElementById('mwStatus').textContent)], [GOLD.programSaid.starts, await p.evaluate(() => MCP_SAID.programSim)], 'P6 MCP tools asks the same way before a program would start');
  eq(await axe('#mcpw'), [], 'P7 accessibility scan of MCP tools with a program');
  await p.evaluate(() => closeApp('mcpw')); await settle();
  eq(await p.evaluate(() => { const t = backupText(); importBackup(t); return [state.mcp[0].program, state.mcp[0].enabled]; }), ['C:\\Program Files\\Example\\mcp-server.exe', false],
    'P8 a restored backup keeps the program\'s path and brings it back switched off');
  eq(await axe('#settings'), [], 'P9 accessibility scan of Settings with a program row');

  // ---------- what changed ----------
  eq(await p.evaluate(() => [CHANGES.find(c => c.round === 41).items, APP_VERSION]), [GOLD.changes.items, GOLD.version], 'F1 what changed in round 41, and the version');
  eq(requests, [], 'F2 the page asked no server for anything');
  eq(errs, [], 'F3 no error in the page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
