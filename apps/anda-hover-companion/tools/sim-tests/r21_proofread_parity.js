// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Writes (WRITE=1) or checks mockup/proofread-golden.json: the cases the Windows app and the extension must answer exactly as the simulation does
// when Proofread and the extra fact checks are ported. Run with the fixed test clock so that dates never go stale.
const { chromium } = require('playwright');
const fs = require('fs');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const OUT = SIMROOT + '/mockup/proofread-golden.json';
const DRAFTS = [
  'Hi all, quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026. ANDA 214990 is running late. ANDA 123456 needs a call. The IR responce for ANDA 216482 (750 mg, cycle 3) is due Jul 28.',
  'hi team, i recieved the the ammendment for anda 216482 .It is a ANDA for Metfromin tablets,500 mg (cycle 2.',
  'The applicant will submit the responce tommorow.The team agreed.',
  'Please see the status ,as discussed ; thanks !',
  'ANDA 216482: Meridan has not replied;;',
  'The FDA labelling guidance was recieved.  Thanks,,',
  'Their going to send the the responce tommorow and me and him will review it. ANDA 216482 are on track for there goal date.',
  'Its a late response, and the applicant dont know weather or not they was told. We should of asked sooner then last week.',
  'Between you and I, this is a honest mistake by a applicant; an review is not needed. It is to late.',
  'The team has went over the draft on monday. They will submitted it in january. Your welcome to check there notes.',
  'i think its not ready; we was waiting for a NDA holder and an PSG, and noone replied.',
  'The plan (see below is ready. He said “yes.',
  'Goal Oct 3, 2026 and 10/17/2026.',
  'There is not enough time. There will be a meeting. Their goal date is Oct 3. It was their response.',
  'Does ANDA 216482 have an open IR? What should it do? If it were late we would say so. Should it have a note?',
  'This may of course change. We sent it to many applicants; it led to much discussion. From early to late July.',
  'Option A applies. An NDA, an ANDA, an RLD, an FDA letter, a PSG, a MAPP, a user fee, a one-time event, an hour, a unit, an SOP and an IR.',
  "Its goal date is its own. It's on track and it's late. The applicant and its response. Your draft, their team, you're right, they're going.",
  'She and I will review it. Please send it to him and me. They are ready; we are too. More than enough, and then we wait.',
  'The response is due on Monday, March 3. He may march in May. The review has gone well and they have done it.',
  'See eCTD section 3.2.P.1, e.g. the table. No. 5 applies. Dr. Smith agreed, i.e. no change.',
  'Tablets and tablet strengths (500 mg and 750 mg) are listed at www.fda.gov; write to a.b@fda.gov.',
  'ANDA\tApplicant\tGoal date\n216482\tFictional Pharma A\tOct 10, 2026',
  'That that is fine. He had had enough.',
  'ANDA 216482 (500 mg, cycle 2): the IR response is due Jul 21.',
  '2. ANDA 214990 - the 750 mg strength from the same applicant. It shares a manufacturing site with the 500 mg application, so any facility finding on one matters to both.',
  'ANDA 214990 is in its second cycle.',
  'ANDA 999999 is 750 mg in cycle 9, due Jul 1.',
  'ANDA 216482 and ANDA 214990 are both 750 mg.',
  'The goal date is Jan 37, 2027. Due 13/45/2026. Feb 29, 2027 and Feb 29, 2028. June 31.',
  'Fine: Oct 3, 2026, 10/17/2026, Feb 29, Sep 30, version 3/4, ratio 12/40.',
  '',
];
(async () => {
  const b = await chromium.launch(); const p = await b.newPage(); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto(SIM); await p.waitForTimeout(300);
  const cases = await p.evaluate(list => list.map(draft => { const pr = proofread(draft), fx = factExtras(draft); return { draft, proofread: { items: pr.items.map(i => i.text), corrected: pr.corrected, fixes: pr.fixes }, facts: { differences: fx.diffs.map(d => d.text), notes: fx.notes.map(n => n.text), alsoChecked: factExtrasSummary(fx) } }; }), DRAFTS);
  const doc = { about: 'Shared cases for Proofread and the extra fact checks (rounds 20 and 21). Written by the browser simulation with its clock fixed at Jul 16, 2026 and the fictional demo records. A port to the Windows app or the extension must give the same answers.', today: '2026-07-16', cases };
  const text = JSON.stringify(doc, null, 1) + '\n';
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${cases.length} cases`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; const same = have === text; console.log(same ? `${cases.length} cases, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with proofread-golden.json'); if (!same || errs.length) process.exitCode = 1; }
  await b.close();
})();
