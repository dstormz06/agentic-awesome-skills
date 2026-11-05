const fs = require('fs');
const OTHER_LABEL = /(?:^|[^A-Za-z0-9_])(?:NDA|BLA|IND|DMF|NDC|MF)(?:[-\s#:.]|No(?![A-Za-z])|Number(?![A-Za-z]))*$/i;
function extractNumber(text){ if(!text) return null; const re = /\d+/g; let m; while((m = re.exec(text))){ if(m[0].length !== 6) continue; if(OTHER_LABEL.test(text.slice(0, m.index))) continue; return m[0]; } return null; }
// the reader exactly as it ships in the browser simulation
const html = fs.readFileSync(require('path').join(__dirname, '..', '..', '..', 'mockup', 'windows-simulation.html'), 'utf8');
const src = html.slice(html.indexOf('const NP = (() => {'), html.indexOf('/* ---- the to-do list store (MyTodoService) ---- */'));
const NP = new Function('extractNumber', src + '\nreturn NP;')(extractNumber);
const notes = JSON.parse(fs.readFileSync(process.argv[2], 'utf8')), cs = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const today = Date.UTC(2026, 10, 4), iso = t => t == null ? '' : new Date(t).toISOString().slice(0, 10);
const norm = i => i && ({ title: i.title, category: i.category, due: iso(i.due), high: i.high, repeat: i.repeat, repeatDay: i.repeatDay, number: i.number || '', actionable: i.actionable });
let bad = 0, shown = 0, items = 0;
notes.forEach((n, k) => {
  const js = { items: NP.parse(n, today).map(norm), one: norm(NP.parseOne(n, today)) || null, due: iso(NP.readDue(n, today)) };
  const c = { items: cs[k].items, one: cs[k].one || null, due: cs[k].due };
  items += js.items.length;
  if (JSON.stringify(js) !== JSON.stringify(c)) { bad++; if (shown++ < 6) { console.log('DIFF', JSON.stringify(n.slice(0, 160))); console.log('  js', JSON.stringify(js).slice(0, 300)); console.log('  cs', JSON.stringify(c).slice(0, 300)); } }
});
console.log(`${notes.length - bad}/${notes.length} notes read identically (${items} items)`);
process.exit(bad ? 1 : 0);
