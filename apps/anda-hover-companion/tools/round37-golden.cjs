// Writes (WRITE=1) or checks mockup/round37-golden.json: the answers of the browser simulation for round 37 (the switch for Today's
// "Since you last looked": its label, its hint, what is said when it is turned off and on, and what changed). The Windows app's tests
// (Round37Tests) must give the same words.
// Run from this folder:  NODE_PATH=$(npm root -g) node round37-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round37-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => ({
    about: 'Shared cases for round 37, written by the browser simulation. The Windows app must give the same words.',
    label: document.getElementById('setLastLook').parentElement.textContent.trim(),
    hint: LOOK_SAID.hint, off: LOOK_SAID.off, on: LOOK_SAID.on, backupKey: 'sinceLastLook' in snapSettings() ? 'sinceLastLook' : null,
    changes: CHANGES.find(c => c.round === 37),
  }));
  const text = JSON.stringify(doc) + '\n';
  const counts = `the switch's label, hint and two sentences, and what changed`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round37-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
