// Writes (WRITE=1) or checks mockup/round35-golden.json: the answers of the browser simulation for round 35 (the tray menu: its choices in
// order, in the focused edition and with all tools, and what each group holds; the version and what changed).
// The Windows app's tests (Round35Tests) must reproduce every answer. Run from this folder:  NODE_PATH=$(npm root -g) node round35-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round35-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => {
    const layout = all => {
      const keep = state.allTools; state.allTools = all; openMenu();
      // the choices a person can press, in order; a group lists its own choices, and a group inside it only by name
      const label = b => b.querySelector('span').textContent;
      const read = box => [...box.children].filter(e => e.tagName === 'BUTTON' && !e.disabled).map(btn => {
        const sub = btn.dataset.group ? btn.nextElementSibling : null;
        return sub && box === menu ? { label: label(btn), items: read(sub).map(x => typeof x === 'string' ? x : x.label) } : label(btn);
      });
      const out = read(menu); closeMenus(); state.allTools = keep; return out;
    };
    return {
      about: 'Shared cases for round 35, written by the browser simulation. The Windows app must give the same answers.',
      focused: layout(false), all: layout(true), changes: CHANGES.find(c => c.round === 35),   // the newest version: round 29's answers
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.focused.length} choices in the focused menu, ${doc.all.length} with all tools`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round35-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
