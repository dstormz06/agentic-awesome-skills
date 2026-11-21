// Writes (WRITE=1) or checks mockup/round42-golden.json: the answers of the browser simulation for round 42: the timeline checks (immediate
// and longer term), the summary for a team lead, the goal dates as a calendar file, the division news read from paragraphs, and the limits.
// The Windows app's tests (Round42Tests) must give the same answers. Fictional numbers only.
// Run from this folder:  NODE_PATH=$(npm root -g) node round42-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round42-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => {
    const today = '2026-11-20';     // a Friday; Thanksgiving is Thu Nov 26 and Christmas Fri Dec 25
    const records = [
      { number: '100001', drug: 'Fictional tablets A', goal: '2026-11-18', forecast: '2026-11-30' },
      { number: '100002', drug: 'Fictional tablets B', goal: '2026-11-20', forecast: null },
      { number: '100003', drug: 'Fictional capsules C', goal: '2026-12-02', forecast: '2026-12-11' },
      { number: '100004', drug: 'Fictional solution D', goal: '2027-01-15', forecast: '2027-01-12' },
      { number: '100005', drug: 'Fictional cream E', goal: '2027-01-15', forecast: '2027-01-15' },
      { number: '100006', drug: 'Fictional tablets F', goal: '2027-01-14', forecast: '2026-12-01' },
      { number: '100007', drug: 'Fictional patch G', goal: '2027-03-01', forecast: null },
      { number: '100008', drug: 'Fictional tablets H', goal: '2026-12-24', forecast: '2026-12-28' },
      { number: '100009', drug: 'Fictional gel I', goal: '2026-12-26', forecast: '2026-12-27' },
      { number: '100010', drug: 'Fictional spray J', goal: '2026-12-08', forecast: null },
      { number: '100011', drug: 'Fictional drops K', goal: '2026-02-30', forecast: '2026-13-01' },
      { number: '12345', drug: 'Not an ANDA number', goal: '2026-11-19', forecast: null },
      // round 42 review: exactly at the limit of a close goal date (10 business days, Thanksgiving skipped), with a thin margin that is not said
      { number: '100012', drug: 'Fictional tablets L', goal: '2026-12-07', forecast: '2026-12-04' },
      { number: '100003', drug: 'A second row for the same number', goal: '2026-12-03', forecast: null },
      { number: '100013', drug: 'Fictional tablets M', goal: '9999-12-31', forecast: null },
      { number: '100014', drug: 'Fictional tablets N', goal: '2026-12-16', forecast: '9999-12-31' },
      { number: '100015', drug: 'Fictional tablets O', goal: '2028-06-01', forecast: '2026-12-01' },
    ];
    const look = { '100003': { goal: '2026-12-09', forecast: '2026-12-11' }, '100004': { goal: '2027-01-15', forecast: '2027-01-05' }, '100006': { goal: '2027-01-10', forecast: '2026-12-01' } };
    const statusSince = { '100006': '2026-10-01', '100007': '2026-11-01', '100010': '2026-10-21', '100008': 'not a date' };
    const todos = [
      { id: 't1', number: '100006', title: 'Send the labeling comments', due: '2026-11-19', done: false },
      { id: 't2', number: '100007', title: 'Ask for the stability update', due: '2026-11-20', done: false },
      { id: 't3', number: null, title: 'Team huddle notes', due: '2026-11-24', done: false },
      { id: 't4', number: '100001', title: 'A finished item', due: '2026-11-01', done: true },
      { id: 't5', number: '100002', title: 'Not due for a while', due: '2026-11-30', done: false },
      { id: 't6', number: 'abc', title: 'Call​ back about the   facility', due: '2026-11-20', done: false },
      { id: 't7', number: '100003', title: 'x'.repeat(120), due: '2026-11-23', done: false },
      { id: 't8', number: '100012', title: 'A far placeholder date', due: '9999-12-31', done: false },
      { id: null, number: '100013', title: 'An item with no id', due: '2026-11-23', done: false },
    ];
    const sets = [
      { name: 'the usual limits', settings: {} },
      { name: 'wider limits', settings: { goalClose: 15, noForecast: 60, thinMargin: 3, crunchCount: 2, crunchHorizon: 30, statusDays: 14, todoSoon: 5 } },
      { name: 'limits that are not choices', settings: { goalClose: 7, noForecast: '30', thinMargin: null, crunchCount: 99, crunchHorizon: -1, statusDays: 30.5, todoSoon: 2 } },
    ];
    const cases = [];
    for (const s of sets) for (const withLook of [true, false]) {
      const input = { today, records, look: withLook ? look : null, todos, statusSince, settings: s.settings };
      cases.push({ name: `${s.name}, ${withLook ? 'with' : 'without'} a last look`, input, alerts: timelineAlerts(input) });
    }
    const drugOf = n => (records.find(r => r.number === n) || {}).drug;
    const summary = tlSummary(cases[0].alerts, today, drugOf);
    const summaryNone = tlSummary([], today, drugOf);
    const summaryLaterOnly = tlSummary(cases[0].alerts.filter(a => a.horizon === 'later'), today, drugOf);
    const calendar = tlCalendar(records, today);
    const paragraphs = ['2026-11-20: The huddle moves to 2:00 PM.', '11/18/2026 – Status updates are due Friday.', 'Nov 10, 2026: Within the fourteen days.',
      'November 5, 2026 The labeling template was updated.', '2026-11-21: Not yet.', '2026-02-30: Not a date.', 'No date here.', 'Sep. 19, 2026: Too old.',
      '2026-11-19:', '2026-11-19: ' + 'y'.repeat(300), '2026-11-1912: Not a date either.', 'Dec 1, 2025: Last year.', '2026-11-17 Hidden​character removed.',
      '11/17/2026: A short line.', '   2026-11-16: Leading spaces.', 'sept 12, 2026: lower case, too old.', 'NOV 9, 2026 upper case month', '2026-11-15 — an em dash.', '1/2/2026: old',
      '2026-11-20: The huddle moves to 2:00 PM.', ...Array(2100).fill(''), '2026-11-19: The newest line, after a long document.'];
    const news = newsFrom(paragraphs, today);
    const attention = { key: '100001|attention|x', number: '100001', code: 'attention', horizon: 'now', sev: 'warn', date: null, text: 'Needs action: Fictional words from the sheet' };
    const briefs = cases[0].alerts.concat([attention]).map(a => [a.code, tlBrief(a)]);
    return {
      about: 'Shared cases for round 42, written by the browser simulation. The Windows app must give the same answers.',
      today, defaults: TL_DEFAULTS, choices: TL_CHOICES, labels: TL_LABELS, normalized: sets.map(s => tlNormalize(s.settings)),
      cases, summary, summaryNone, summaryLaterOnly, calendar, paragraphs, news, briefs,
      said: { todayTitle: TL_SAID.todayTitle, todayNone: TL_SAID.todayNone, todayMore: TL_SAID.todayMore(3), copied: TL_SAID.copied(2, 1), calendar: TL_SAID.calendar(4), hint: TL_SAID.hint },
      changes: CHANGES.find(c => c.round === 42), version: APP_VERSION,
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.cases.length} sets of timeline alerts (${doc.cases.reduce((n, c) => n + c.alerts.length, 0)} alerts), a summary, ${doc.calendar.count} calendar events, ${doc.news.length} of ${doc.paragraphs.length} news paragraphs`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round42-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
