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
  { ai: null, agents: [], outlook: { where: 'login.microsoftonline.us and graph.microsoft.us' } },
  { ai: { name: 'Claude', where: 'api.anthropic.com' }, agents: [agent('Alert Hub', 'alerts.example.test', true), agent('Patent Analysis', 'patents.example.test', false)], outlook: { where: 'graph.microsoft.com' } },
  { ai: { name: 'Elsa', where: 'elsa.example.test' }, agents: [agent('A', 'a.example.test', false), agent('B', 'b.example.test', false), agent('C', 'c.example.test', true), agent('D', 'd.example.test', false)], outlook: { where: 'graph.microsoft.com' } },
];
const WHEN = Date.UTC(2026, 6, 16, 13, 5, 7, 42);
const EVENTS = [
  // every kind, with a sentence of the log's own
  ['connection', 'AI switched on'], ['connection', 'at start: AI off, agents on: 2, Outlook on'], ['connection', 'agents switched on: 0 to 8'], ['sent', 'AI request, 1,234 characters'],
  ['sent', 'AI request, 7 characters'], ['sent', 'AI request, 1,234,567 characters'], ['send_failed', 'AI request: timed out or canceled'], ['file', 'settings restored from a backup'],
  ['deleted', 'kept copies of the sheet'], ['error', ''], ['warning', ''], ['not-a-kind', 'connection test'], ['sent', null],
  // anything else is withheld: text of the person's, a name, an address, a path, a number of an ANDA, a number that is not a count
  ['sent', 'ANDA 216482 "quoted" text'], ['sent', 'a<b>c'], ['sent', 'x'.repeat(121)], ['sent', 'x'.repeat(120)], ['sent', 'line one\nline two'],
  ['sent', 'about 216482'], ['sent', 'AI request, 216482 characters'], ['sent', 'AI request, 1234 characters'], ['sent', 'AI request, # characters'], ['sent', 'AI request, 1,23 characters'],
  ['sent', 'John Smith, 12 Main St'], ['sent', 'ANDA 123 456'], ['sent', 'path C:/Users/jsmith/sheet.xlsx'], ['sent', 'host api.example.com'], ['sent', 'something'],
  ['sent', '=1+1'], ['sent', '+1'], ['sent', '-1'], ['sent', 'AI switched on '], ['sent', 'ai switched on'], ['sent', 'AI request, ١٢ characters'],
];
// what is written when what is switched on changes: [before, now] as numbers of STATES
const CHANGE_PAIRS = [[0, 0], [0, 1], [1, 0], [1, 2], [3, 4], [3, 6], [6, 7], [5, 6], [6, 5], [7, 0], [0, 7], [1, 1]];
const US = { ai: null, agents: [], outlook: { where: 'login.microsoftonline.com and graph.microsoft.com' } };
(async () => {
  const b = await chromium.launch(); const p = await b.newPage(); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(([states, events, when, pairs, us]) => {
    const entries = events.map(([type, detail], i) => eventEntry(type, detail, when + i * 1000));
    return {
      about: 'Shared cases for round 29, written by the browser simulation. The Windows app must give the same answers.',
      offline: CONN_OFFLINE, note: CONN_NOTE,
      connections: states.map(c => ({ state: c, result: connections(c) })),
      eventTypes: EV_TYPES, withheld: EV_WITHHELD, phrases: EV_PHRASES,
      starts: states.map(c => connStart(c)),
      connChanges: pairs.map(([i, j]) => ({ before: i, now: j, lines: connChanges(states[i], states[j]) })).concat([{ before: 5, now: -1, other: us, lines: connChanges(states[5], us) }]),
      events: events.map(([type, detail], i) => ({ type, detail, when: when + i * 1000, entry: entries[i] })),
      csv: eventCsv(entries), emptyCsv: eventCsv([]),
      version: APP_VERSION, changes: CHANGES, changesText: changesText(),
    };
  }, [STATES, EVENTS, WHEN, CHANGE_PAIRS, US]);
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.connections.length} connection states, ${doc.events.length} events, ${doc.phrases.length} sentences of the log, ${doc.connChanges.length} changes, ${doc.changes.length} rounds of changes`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round29-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
