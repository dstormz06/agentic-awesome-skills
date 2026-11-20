// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 40 in the browser simulation: MCP servers in Settings, the MCP tools window (nothing sent until List tools, Send only after the person
// has seen what goes) and the My API presets. The page contacts no server: the fictional demo server is answered by the page itself. Its words
// and rules are held to mockup/round40-golden.json, which the Windows app is also tested against.
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round40-golden.json'), 'utf8'));
const V29 = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round29-golden.json'), 'utf8')).version;
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
  const log = () => p.evaluate(() => evLog.map(e => e.detail));

  // ---------- Settings ----------
  await p.evaluate(() => { showApp('settings'); document.getElementById('secConnections').open = true; });
  await settle();
  eq(await p.evaluate(() => [document.getElementById('mcpHint').textContent, state.mcp.length, document.getElementById('mcpList').children.length]), [GOLD.said.hint, 0, 0], 'S1 no server at first, and the shared hint is shown');
  await p.click('#mcpAdd'); await settle();
  const box = '#mcpList [data-mcp] input[type=checkbox]';
  await p.click(box); await settle();
  eq(await p.evaluate(b => [document.querySelector(b).checked, state.mcp[0].enabled, document.querySelector('#mcpList [role=status]').textContent], box), [false, false, GOLD.addresses[0].problem],
    'S2 a server with no address cannot be switched on, and the reason is said');
  await p.fill('#mcpList [data-mcp] input[aria-label="Address of MCP server 1"]', 'http://mcp.example.test/mcp'); await p.click(box); await settle();
  eq(await p.evaluate(b => [document.querySelector(b).checked, document.querySelector('#mcpList [role=status]').textContent], box), [false, GOLD.addresses.find(a => a.endpoint === 'http://mcp.example.test/mcp').problem],
    'S3 plain http to another computer is refused, as for My API');
  await p.fill('#mcpList [data-mcp] input[aria-label="Address of MCP server 1"]', 'https://mcp.example.test/mcp'); await p.click(box); await settle();
  eq(await p.evaluate(() => [state.mcp[0].enabled, connState().mcp]), [true, [{ name: 'MCP server 1', where: 'mcp.example.test' }]], 'S4 with an https address it is switched on and named in what is switched on');
  await p.fill('#mcpList [data-mcp] input[aria-label="Address of MCP server 1"]', ''); await settle();
  eq(await p.evaluate(b => [document.querySelector(b).checked, state.mcp[0].enabled, connState().mcp.length], box), [false, false, 0], 'S5 emptying the address switches it off');
  eq(await p.evaluate(() => [...document.querySelectorAll('#mcpList [data-mcp] [aria-label], #mcpList [data-mcp] input, #mcpList [data-mcp] button')].map(e => e.getAttribute('aria-label') || e.textContent).filter((x, i, a) => a.indexOf(x) === i)),
    ['Use MCP server 1', 'Name of MCP server 1', 'Address of MCP server 1', 'Token header of MCP server 1', 'Before the token of MCP server 1', 'Token variable of MCP server 1', 'Test', 'Remove'], 'S6 each field of a row is named for a screen reader, as in the app');
  await p.click('#mcpList [data-mcp] button:has-text("Remove")'); await settle();
  for (let i = 0; i < 9; i++) await p.evaluate(() => document.getElementById('mcpAdd').click());
  eq(await p.evaluate(() => [state.mcp.length, document.getElementById('mcpAdd').disabled, document.getElementById('mcpAddDemo').disabled, new Set(state.mcp.map(s => s.id)).size]), [8, true, true, 8], 'S7 at most eight servers, each with its own id');
  await p.evaluate(() => { state.mcp = []; paintMcpList(); });
  await p.click('#mcpAddDemo'); await settle();
  const before = (await log()).length;
  await p.click('#mcpList [data-mcp] button:has-text("Test")'); await settle();
  eq([await p.evaluate(() => document.querySelector('#mcpList [role=status]').textContent), (await log()).slice(before)], [await p.evaluate(() => MCP_SAID.testSim(2)), ['MCP tools listed, 2 tools']],
    'S8 Test on the demo server lists its two tools and writes only the count to the event log');
  eq(await axe('#settings'), [], 'S9 accessibility scan of Settings with an MCP server');

  // ---------- presets ----------
  eq(await p.evaluate(() => API_PRESETS), GOLD.presets, 'P1 the presets are the ones the app offers');
  await p.click('#apiPresets button:has-text("Fill in for Azure OpenAI")'); await settle();
  eq(await p.evaluate(() => [state.myApi.header, state.myApi.scheme, state.myApi.path, document.getElementById('apiHeader').value, document.getElementById('apiEndpoint').placeholder, document.getElementById('myApi').open]),
    ['api-key', '', 'choices[0].message.content', 'api-key', GOLD.presets[1].hint, true], 'P2 a preset fills the header, prefix, body and reply path, and leaves the address to the person');

  // ---------- the window ----------
  await p.evaluate(() => openMcp()); await settle();
  const n0 = (await log()).length;
  eq(await p.evaluate(() => [document.getElementById('mwStatus').textContent, document.getElementById('mwTool').options.length, document.getElementById('mwSend').disabled]), [GOLD.said.nothingYet, 0, true],
    'W1 nothing is asked for until List tools');
  await p.click('#mwPreview'); await settle();
  eq(await p.evaluate(() => document.getElementById('mwStatus').textContent), GOLD.said.chooseFirst, 'W2 Show what will be sent before a tool is chosen says what to do');
  await p.evaluate(() => { document.getElementById('mwArgs').dataset.sel = '  Metformin ER  '; }); await p.click('#mwList'); await settle();
  eq(await p.evaluate(() => [document.getElementById('mwTool').options.length, document.getElementById('mwStatus').textContent, document.getElementById('mwArgs').value]),
    [2, await p.evaluate(() => MCP_SAID.listed(2)), '{\n  "query": "Metformin ER",\n  "max_results": 0\n}'], 'W3 List tools offers the two tools and fills the arguments in from the selection');
  await p.fill('#mwArgs', '[1]'); await p.click('#mwPreview'); await settle();
  eq(await p.evaluate(() => [document.getElementById('mwStatus').textContent, document.getElementById('mwSend').disabled]), [GOLD.said.notJson, true], 'W4 arguments that are not one object are refused, and Send stays off');
  await p.fill('#mwArgs', '{"query": "goal date", "max_results": 2}'); await p.click('#mwPreview'); await settle();
  eq(await p.evaluate(() => [document.getElementById('mwShown').textContent, document.getElementById('mwSend').disabled]),
    [await p.evaluate(() => mcpPreview({ name: 'Fictional demo server' }, 'search_fictional_guidance', { query: 'goal date', max_results: 2 }, 'mcp.example.invalid')), false], 'W5 the person sees exactly what goes, and where, before Send');
  await p.type('#mwArgs', ' '); await settle();
  eq(await p.evaluate(() => [document.getElementById('mwShown').textContent, document.getElementById('mwSend').disabled]), ['', true], 'W6 a change after that needs a new look before Send');
  await p.click('#mwPreview'); await p.click('#mwSend'); await settle();
  eq(await p.evaluate(() => [document.getElementById('mwAnswer').textContent, document.getElementById('mwSend').disabled, document.getElementById('mwCopy').disabled]),
    ['Fictional note 2: a goal date that moves is noted with the reason given.\n(simulated)', true, false], 'W7 Send shows the answer; Send is off again until the next look');
  eq((await log()).slice(n0), ['MCP tools listed, 2 tools', 'MCP request (simulated), 37 characters'], 'W8 the event log holds only counts, never the arguments or the answer');
  eq(await axe('#mcpw'), [], 'W9 accessibility scan of the MCP tools window');
  await p.evaluate(() => { state.mcp.forEach(s => s.enabled = false); openMcp(); }); await settle();
  await p.click('#mwList'); await settle();
  eq(await p.evaluate(() => document.getElementById('mwStatus').textContent), GOLD.said.noServer, 'W10 with no server on, the window says where to add one');

  // ---------- backups, the menu, what changed ----------
  eq(await p.evaluate(() => { state.mcp = mcpNormalize([{ name: 'Fictional backup server', endpoint: 'https://mcp.example.test/b', enabled: true, env: 'FICTIONAL_TOKEN' }]); const t = backupText();
    state.mcp = []; importBackup(t); const kept = state.mcp.map(s => [s.name, s.enabled, s.env]); const x = JSON.parse(t); delete x.settings.mcpServers; importBackup(JSON.stringify(x));
    return [JSON.parse(t).settings.mcpServers.length, kept, state.mcp.length, document.querySelectorAll('#mcpList [data-mcp]').length]; }),
    [1, [['Fictional backup server', true, 'FICTIONAL_TOKEN']], 1, 1], 'B1 the servers are in a backup and come back (the variable NAME only); an older backup keeps the servers the person has now');
  eq(await p.evaluate(() => [CHANGES.find(c => c.round === 40).items, APP_VERSION]), [GOLD.changes.items, V29], 'F1 what changed in round 40 is listed, and the version is the one the app is held to');
  eq(requests, [], 'F2 the page asked no server for anything');
  eq(errs, [], 'F3 no error in the page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
