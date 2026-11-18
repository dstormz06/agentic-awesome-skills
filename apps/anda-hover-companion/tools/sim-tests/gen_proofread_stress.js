// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Writes (WRITE=1) or checks mockup/proofread-stress-golden.json: several hundred drafts made by a fixed, seeded mix of the shared drafts,
// slips of the fingers and awkward characters. The Windows app must answer each one exactly as the simulation does.
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = SIMROOT + '/mockup/';
const SIM = 'file://' + DIR + 'windows-simulation.html?clock=fixed';
const OUT = DIR + 'proofread-stress-golden.json';
const base = JSON.parse(fs.readFileSync(DIR + 'proofread-golden.json', 'utf8')).cases.map(c => c.draft).filter(Boolean);
let seed = 20261117; const rnd = () => (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff, pick = a => a[Math.floor(rnd() * a.length)], int = n => Math.floor(rnd() * n);
const C = n => String.fromCharCode(n);
const BITS = [' ', '  ', '   ', ' ,', ' .', ',', ';', ';;', '..', '...', '!!', ' ?', '\n', '\r\n', '\n\n', '\t', C(0xa0), C(0x2009), C(0x201c), C(0x201d), '"', '(', ')', ' i ', ' I ', ' a ', ' an ', ' the the ', ' its ', " it's ",
  ' their ', ' there ', ' then ', ' than ', ' to late ', ' of ', ' ANDA 216482 ', ' anda 214990 ', ' ANDA 123456 ', ' NDA 021234 ', ' N216482 ', ' A216482 ', ' 2164820 ', ' 500 mg ', ' 750mg ', ' 0.5 mcg ', ' cycle 0 ', ' cycle 2 ', ' third-cycle ', ' 5th cycle ',
  ' due Jul 21 ', ' due 7/21/2026 ', ' due July 28, 2026 ', ' Feb 30 ', ' 2/30/2027 ', ' 13/1/2026 ', ' Jun 30 ', ' Jul 8 ', ' Jul 2 ', ' goal date Oct 3, 2026 ', ' forecast of 10/17/2026 ', ' 2026-10-03 ', ' TBD ', ' tbd ', ' [TBD] ', ' ??? ', ' {{name}} ', ' <<date>> ',
  ' Metfromin ', ' Meridan ', ' METFORMNI ', ' hydrocloride ', ' Meridians ', ' recieve ', ' Recieved ', ' SEPERATE ', ' fda ', ' Fda ', ' e.g. the ', ' i.e. this ', ' Dr. smith ', ' No. 5 ', ' on monday ', ' in january ', ' dont ', ' alot ', ' me and him will ',
  ' between you and I ', ' attached ', ' a hour ', ' an user ', ' a NDA ', ' an PSG ', ' a ANDA ', ' An apple ', ' x@fda.hhs.gov ', ' www.fda.gov/drugs ', ' eCTD ', ' 3.2.P.1 ', C(0xe9), C(0x130), C(0x212a), C(0x17f) + 'hould of ', C(0xfeff), C(0x85), C(0x2028), C(0)];
function mutate(t) {
  const ops = 1 + int(4);
  for (let k = 0; k < ops; k++) {
    const at = int(t.length + 1), op = int(7);
    if (op === 0 && t.length > 2) t = t.slice(0, at) + t.slice(at + 1);                                    // drop a character
    else if (op === 1 && at < t.length - 1) t = t.slice(0, at) + t[at + 1] + t[at] + t.slice(at + 2);      // swap two neighbors
    else if (op === 2) t = t.slice(0, at) + pick(BITS) + t.slice(at);                                      // put something in
    else if (op === 3) t = t + ' ' + pick(base).split(/(?<=[.!?])\s+/).slice(0, 2).join(' ');              // add sentences of another draft
    else if (op === 4) t = t.replace(/[A-Za-z]+/g, w => rnd() < 0.08 ? (rnd() < 0.5 ? w.toUpperCase() : w.toLowerCase()) : w);
    else if (op === 5) t = t.slice(0, at) + pick(BITS) + pick(BITS) + t.slice(at);
    else t = t.replace(/ /g, s => rnd() < 0.05 ? pick(['  ', ' ,', '', '\n']) : s);
  }
  return t.slice(0, 420);
}
const drafts = [];
for (let i = 0; i < 360; i++) drafts.push(mutate(i % 6 === 5 ? pick(BITS) + pick(BITS) + pick(BITS) : pick(base)));
drafts.push('i '.repeat(30), 'word '.repeat(50) + 'end.', 'a  b  c  d  e '.repeat(12), '(((', 'x ,'.repeat(9), 'Hello,world;next,one;two,three,four', 'ok.Next one.Then two.And three.Four',
  'the the and and of of to to in in', 'TBD TBC XXX [TODO] ??? {{a}} <<b>> lorem ipsum tbd', '   ', '\n\n', 'ANDA 216482 is due Feb 29. ANDA 216482 cycle 0. ANDA 214990 first cycle.');
(async () => {
  const b = await chromium.launch(); const p = await b.newPage(); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto(SIM); await p.waitForTimeout(300);
  const cases = await p.evaluate(list => list.map(draft => { const pr = proofread(draft), fx = factExtras(draft), rv = draftReview(draft, demoToday()), q = beforeSend(draft, rv, fx); return { draft, proofread: { items: pr.items.map(i => i.text), corrected: pr.corrected, fixes: pr.fixes }, facts: { differences: fx.diffs.map(d => d.text), notes: fx.notes.map(n => n.text), alsoChecked: factExtrasSummary(fx) }, review: rv.findings.map(f => f.kind + ' | ' + f.text), question: q ? (q.gap ? 'gap | ' : 'none | ') + q.text : null }; }), drafts);
  // The same checks with one quick note of the person's own on ANDA 216482, so that "also in your note" and the recall row are covered.
  const NOTE = 'Call with the applicant on Jul 21 about the IR response; labeling follows on 8/1.';
  const noted = await p.evaluate(([note, list]) => { addQuickNote('216482', note); const who = notesFor('216482')[0].who;
    return { number: '216482', note, who, cases: list.map(draft => { const fx = factExtras(draft), rc = recallFor(draft);
      return { draft, notes: fx.notes.map(n => n.text), differences: fx.diffs.map(d => d.text),
        recall: rc.map(g => ({ id: g.id, latest: g.latest, notes: g.notes.map(n => ({ who: n.who, shared: n.shared })), items: g.items.map(i => [i.title, i.due, i.urgent, i.source].join(' | ')) })) }; }) }; },
    [NOTE, ['ANDA 216482: the IR response is due Jul 21.', 'For ANDA 216482 the call is on July 21, 2026 and labeling on Aug 1.', 'ANDA 216482 and ANDA 214990 both moved; see Jul 21.', 'ANDA 214990: the facility memo was filed Jul 2. ANDA 216482: receipt was confirmed 7/21.',
      'ANDA 216482 response expected by 7/21/2026, applicant confirmed receipt.', 'Status of ANDA 214990: labeling agreed, final format pending. Nothing on Jul 21.', 'No number here, only Jul 21.', 'ANDA 216482\tJul 21\tIR response']]);
  const doc = { about: 'Stress cases for Proofread and the extra fact checks: a seeded mix of the shared drafts, slips of the fingers and awkward characters, answered by the browser simulation with its clock fixed at Jul 16, 2026: Proofread, the extra fact checks, the earlier review (kind | text) and the one question before sending (gap or none | text). The Windows app must give the same answers. Made by a script; not written by hand.', today: '2026-07-16', cases, noted };
  const text = JSON.stringify(doc) + '\n';
  const withItems = cases.filter(c => c.proofread.items.length).length, withDiffs = cases.filter(c => c.facts.differences.length).length, withNotes = cases.filter(c => c.facts.notes.length).length;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${cases.length} cases (${withItems} with wording items, ${withDiffs} with differences, ${withNotes} with note hits), ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${cases.length} stress cases, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with proofread-stress-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
