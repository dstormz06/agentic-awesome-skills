// Writes (WRITE=1) or checks mockup/round29-golden.json: the answers of the browser simulation for round 29 (what is switched on, said in
// every window; the event log; what changed). The Windows app's tests (Round29Tests) must reproduce every answer.
// Run from this folder with Playwright available:  NODE_PATH=$(npm root -g) node round29-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round29-golden.json';
const agent = (name, where, auto) => ({ name, where, auto });
const STATES = [
  { ai: null, agents: [], outlook: null },
  { ai: { name: 'Claude', where: 'api.anthropic.com' }, agents: [], outlook: null },
  { ai: { name: 'Division API', where: '' }, agents: [], outlook: null },
  { ai: null, agents: [agent('Alert Hub', 'alerts.example.test', false)], outlook: null },
  { ai: null, agents: [agent('Alert Hub', '', true)], outlook: null },
  { ai: null, agents: [], outlook: { where: 'graph.microsoft.us' } },
  { ai: { name: 'Claude', where: 'api.anthropic.com' }, agents: [agent('Alert Hub', 'alerts.example.test', true), agent('Patent Analysis', 'patents.example.test', false)], outlook: { where: 'graph.microsoft.com' } },
  { ai: { name: 'Elsa', where: 'elsa.example.test' }, agents: [agent('A', 'a.example.test', false), agent('B', 'b.example.test', false), agent('C', 'c.example.test', true), agent('D', 'd.example.test', false)], outlook: { where: 'graph.microsoft.com' } },
];
const WHEN = Date.UTC(2026, 6, 16, 13, 5, 7, 42);
const EVENTS = [
  ['connection', 'AI switched on'], ['sent', 'AI request, 1,234 characters'], ['send_failed', 'agent request, status 503'], ['file', 'settings restored from a backup'],
  ['deleted', 'kept copies of the sheet'], ['error', 'the event log could not be written'], ['warning', ''], ['not-a-kind', 'something'],
  ['sent', 'ANDA 216482 "quoted" text'], ['sent', 'a<b>c'], ['sent', 'x'.repeat(121)], ['sent', 'x'.repeat(120)], ['sent', 'line one\nline two'], ['sent', null],
  ['sent', 'about 216482'], ['sent', '99,999 characters'], ['sent', '1234567 characters'],
];
(async () => {
  const b = await chromium.launch(); const p = await b.newPage(); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(([states, events, when]) => {
    const entries = events.map(([type, detail], i) => eventEntry(type, detail, when + i * 1000));
    return {
      about: 'Shared cases for round 29, written by the browser simulation. The Windows app must give the same answers.',
      offline: CONN_OFFLINE, note: CONN_NOTE,
      connections: states.map(c => ({ state: c, result: connections(c) })),
      eventTypes: EV_TYPES, withheld: EV_WITHHELD,
      events: events.map(([type, detail], i) => ({ type, detail, when: when + i * 1000, entry: entries[i] })),
      csv: eventCsv(entries), emptyCsv: eventCsv([]),
      version: APP_VERSION, changes: CHANGES, changesText: changesText(),
    };
  }, [STATES, EVENTS, WHEN]);
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.connections.length} connection states, ${doc.events.length} events, ${doc.changes.length} rounds of changes`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round29-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
