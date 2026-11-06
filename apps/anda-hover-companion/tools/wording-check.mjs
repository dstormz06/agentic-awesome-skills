// Wording check: reads the words a person sees (the app's windows and messages, the browser simulation, the Appearance Lab, the Edge extension,
// the demo files and the documents) and lists anything that breaks the house style or uses a term the product has retired.
// It prints file:line for each finding and exits 1 if there is one. A finding is not always a mistake (a quotation in VERIFICATION.md is allowed to
// show an old term), so a line can be allowed with the comment  wording-ok  on the same line. Run from this folder:  node wording-check.mjs
// Lines that hold word lists or regular expressions for reading the person's own notes (NoteParser, the simulation's copy of it) are not prose and are skipped.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const SKIP_DIRS = new Set(['node_modules', 'bin', 'obj', 'tests', 'AndaCompanion.Tests', 'fuzz', 'tools', '.git', 'out']);
const PRODUCT = /\.(html|axaml|cs|js|mjs|json|md|txt)$/i;
const SKIP_FILES = [/golden/i, /VERIFICATION\.md$/, /\.min\./, /package-lock/, /sample-memo\.txt$/, /manifest\.json$/];   // fixtures and the record of past defects quote old words on purpose
const files = [];
(function walk(dir) {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) { if (!SKIP_DIRS.has(name)) walk(full); }
    else if (PRODUCT.test(name) && !SKIP_FILES.some(r => r.test(full))) files.push(full);
  }
})(root);

// A quoted piece of prose: at least two words, so identifiers and regular expressions are not read as sentences.
const prose = line => (line.match(/(["'`])((?:\\.|(?!\1).){8,})\1/g) || []).filter(q => /[A-Za-z]{2,}\s+[A-Za-z]{2,}/.test(q));
const isCode = line => /\bR\(|new Regex\(|("[a-z ]+",\s*){6,}/.test(line);   // a regular expression or a long word list, not a sentence
const isMarkup = f => /\.(md|txt)$/i.test(f);

const RULES = [
  { id: 'exclamation', why: 'No exclamation marks: the tone is calm and professional.',
    test: (line, f) => { const t = isMarkup(f) ? [line] : prose(line).concat(/>[^<>]*[A-Za-z]{2,}\s+[A-Za-z]{2,}[^<>]*</.test(line) ? [line] : []); return t.some(s => /[A-Za-z0-9)\]’'"]!(?=\s|["'<`]|$)/.test(s) && !/!==|!=|!important|<!--|<!doctype/i.test(s)); } },
  { id: 'casual', why: 'Casual wording. Use plain, courteous language.',
    test: (line, f) => { if (isCode(line)) return false; const t = isMarkup(f) ? [line] : prose(line).concat(line.includes('>') ? [line] : []); return t.some(s => /\b(oops|whoops|yay|awesome|gonna|wanna|kinda|uh-oh|sorry|lol|ok then|no worries|super easy|a ton of|stuff)\b/i.test(s)); } },
  { id: 'retired-term', why: 'A term the product no longer uses.',
    test: (line, f) => !isCode(line) && (
      /\b(?<!fact-)check a draft\b/i.test(line)
      || (/\bgetting started\b/i.test(line) && !/\bno\b.{0,30}getting started/i.test(line))
      || /\bthree rebindable\b/i.test(line)
      || (/\bnineteen looks|\b19 looks\b/i.test(line) && /(^|[\\/])mockup[\\/]/.test(f))) },   // the simulation and the lab now show 22 looks
  { id: 'us-spelling', why: 'Use US spelling (labeled, color, centered, gray, behavior, recognize).',
    test: (line) => !isCode(line) && /\b(labelled|labelling|colou?r(?:ed|ing|s)?(?<!color|colors|colored|coloring)|colour\w*|centred|centre|greyed?|grey|behaviour\w*|organis(?:e|ed|ing|ation)|recognis\w+|summaris\w+|finalis\w+|licence|catalogue|favour\w*|neighbour\w*|cancelled|cancelling)\b/i.test(line)
      && !/CancellationToken|Colour\(/.test(line) },
  { id: 'double-space', why: 'Two spaces between words.',
    test: (line, f) => isMarkup(f) && !/^\s*(\||```|    )/.test(line) && /[A-Za-z,.;:]\s{2,}[A-Za-z]/.test(line.trim()) },
  { id: 'product-name', why: 'The product is ANDA Companion (Notula): spell it that way.',
    test: (line) => /\b(Anda Companion|ANDA companion|ANDA-Companion|Notulla|Notula Companion)\b/.test(line) },
  { id: 'period-spacing', why: 'No space before a comma, full stop, colon or semicolon.',
    test: (line, f) => isMarkup(f) && !/^\s*(\||```)/.test(line) && /[A-Za-z] [,.;:](?=\s|$)/.test(line) && !/\s\.\.\./.test(line) },
];

let findings = 0;
for (const file of files) {
  const lines = readFileSync(file, 'utf8').split(/\r?\n/);
  let fence = false;
  lines.forEach((line, i) => {
    if (/^\s*```/.test(line)) { fence = !fence; return; }
    if (fence || /wording-ok/.test(line)) return;
    for (const rule of RULES) {
      if (rule.test(line, file)) { findings++; console.log(`${relative(root, file)}:${i + 1}  [${rule.id}] ${line.trim().slice(0, 150)}`); }
    }
  });
}
console.log(`\n${files.length} files read, ${findings} finding${findings === 1 ? '' : 's'}.`);
process.exit(findings ? 1 : 0);
