// Writes (WRITE=1) or checks mockup/round34-golden.json: the answers of the browser simulation for round 34 (what the person's own shortcut
// and a middle-click open first, under each consent choice; the consent choices and which is the default; the tail of the watch line).
// The Windows app's tests (Round34Tests) must reproduce every answer.
// Run from this folder with Playwright available:  NODE_PATH=$(npm root -g) node round34-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round34-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => {
    const keep = [state.mode, state.escalate], steps = [];
    for (const mode of ['full', 'peek', 'whisper']) for (const escalate of [true, false]) for (const id of ['216482', '214990']) for (const via of ['shortcut', 'middle']) {
      state.mode = mode; state.escalate = escalate;
      steps.push({ consent: mode, escalate, urgent: isUrgent(id), via, step: firstStep(id, via === 'middle' ? 'middle' : null) });
    }
    [state.mode, state.escalate] = keep;
    return {
      about: 'Shared cases for round 34, written by the browser simulation. The Windows app must give the same answers.',
      defaultConsent: DEFAULT_SETTINGS.consent, consents: CONSENTS, steps,
      watchMore: [0, 1, 2, 5].map(n => ({ n, text: watchMoreText(n) })),
      version: APP_VERSION, changes: CHANGES[0],
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.steps.length} first steps, ${doc.consents.length} consent choices, ${doc.watchMore.length} watch-line tails`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round34-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
