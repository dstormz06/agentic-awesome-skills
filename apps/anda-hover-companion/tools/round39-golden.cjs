// Writes (WRITE=1) or checks mockup/round39-golden.json: the answers of the browser simulation for round 39 (the switch for the to-do
// items in Today's next week: its label, its hint, and what changed). The Windows app's tests
// (Round39Tests) must give the same words.
// Run from this folder:  NODE_PATH=$(npm root -g) node round39-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round39-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => ({
    about: 'Shared cases for round 39, written by the browser simulation. The Windows app must give the same words.',
    label: document.getElementById('setWeekTodos').parentElement.textContent.trim(), hint: WEEK_TODOS_SAID.hint,
    backupKey: 'weekTodos' in snapSettings() ? 'weekTodos' : null, changes: CHANGES.find(c => c.round === 39),
  }));
  const text = JSON.stringify(doc) + '\n';
  const counts = `the switch's label and hint, and what changed`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round39-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
