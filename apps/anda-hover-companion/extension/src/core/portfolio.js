// The person's own spreadsheet as the data source, read in the browser and kept in the extension's own storage. It shows only what the
// sheet contains: a forecast or progress that is not there is shown as unknown, never estimated. Mirrors the app's Services/Portfolio/.
import { parseXml } from './xml.js';
import { listEntries, readEntry, ZipError } from './zip.js';
import { extractNumber } from './recognizer.js';
import { DAY, parseIso, toIso, fmtLong, monthName } from './calendar.js';

export class PortfolioError extends Error {}

export const MAX_FILE_BYTES = 25 * 1024 * 1024;
export const MAX_UNZIPPED_BYTES = 150 * 1024 * 1024;
export const MAX_ROWS = 20000, MAX_COLUMNS = 200;
export const UNKNOWN = '—';

// ---------------- columns ----------------

export const FIELDS = [
  ['number', 'ANDA number'], ['drug', 'Drug / product'], ['applicant', 'Applicant'], ['goal', 'Goal date'], ['forecast', 'Forecast date'],
  ['status', 'Status'], ['notes', 'My notes'], ['cycle', 'Cycle'], ['submitted', 'Date submitted'], ['ingredient', 'Active ingredient'],
  ['form', 'Dosage form'], ['strength', 'Strength'], ['psg', 'PSG date (yours)'],
];
const SYNONYMS = {
  number: ['anda', 'anda number', 'anda no', 'anda #', 'application', 'application number', 'application no', 'appl no', 'app no', 'app #'],
  drug: ['drug', 'drug name', 'product', 'product name', 'proprietary name', 'established name', 'description of product'],
  applicant: ['applicant', 'company', 'firm', 'sponsor', 'applicant name'],
  goal: ['goal', 'goal date', 'gdufa goal', 'gdufa goal date', 'gdufa date', 'due date', 'target date', 'action date'],
  forecast: ['forecast', 'forecast date', 'projected', 'projected date', 'projected action date', 'my forecast'],
  status: ['status', 'current status', 'review status', 'stage', 'latest', 'latest status'],
  notes: ['notes', 'rpm notes', 'my notes', 'note', 'comments', 'project description', 'project descriptions', 'description'],
  cycle: ['cycle', 'review cycle', 'cycle number'],
  submitted: ['submission date', 'submitted', 'date submitted', 'received', 'receipt date', 'date received', 'received date'],
  ingredient: ['active ingredient', 'ingredient', 'api', 'established name'],
  form: ['dosage form', 'form'],
  strength: ['strength', 'strengths'],
  psg: ['psg', 'psg date', 'psg updated', 'psg revised', 'psg date updated'],
};

export const normalizeHeader = (h) => h.toLowerCase().replace(/_/g, ' ').replace(/[^a-z0-9# ]+/g, ' ').trim().replace(/\s+/g, ' ');
export const looks = (header, key) => (SYNONYMS[key] || []).includes(normalizeHeader(header));

/** The header chosen for each field: the person's override if it exists, otherwise the first header that matches. */
export function detectColumns(headers, overrides = null) {
  const map = {};
  const used = new Set();
  for (const [key] of FIELDS) {
    const chosen = overrides && overrides[key];
    if (chosen && chosen.trim() && headers.includes(chosen)) { map[key] = chosen; used.add(chosen); continue; }
    const hit = headers.find((h) => !used.has(h) && looks(h, key));
    if (hit !== undefined) { map[key] = hit; used.add(hit); }
  }
  return map;
}

// ---------------- dates ----------------

const MONTHS_FULL = ['january', 'february', 'march', 'april', 'may', 'june', 'july', 'august', 'september', 'october', 'november', 'december'];
const MONTHS_ABBR = MONTHS_FULL.map((m) => m.slice(0, 3));

function validDate(y, m, d) {
  if (y < 1990 || y > 2100 || m < 1 || m > 12 || d < 1) return null;
  const t = Date.UTC(y, m - 1, d);
  return new Date(t).getUTCMonth() === m - 1 ? toIso(t) : null;
}
const monthIndex = (name, list) => list.indexOf(name.toLowerCase()) + 1;
const twoDigitYear = (yy) => (yy <= 49 ? 2000 + yy : 1900 + yy);   // the invariant culture's pivot (2049): 00 to 49 is the 2000s, 50 to 99 the 1900s

/** US month-first dates and ISO dates. A date that cannot be read is not guessed. Returns 'YYYY-MM-DD' or null. */
export function parseDate(text) {
  const s = (text || '').trim();
  if (!s) return null;
  let m;
  if ((m = /^(\d{4})-(\d{2})-(\d{2})(?:[ T](\d{2}):(\d{2}):(\d{2}))?$/.exec(s))) return validDate(+m[1], +m[2], +m[3]);
  if ((m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})(?: (\d{1,2}):(\d{2}):(\d{2}) (AM|PM))?$/i.exec(s))) return validDate(+m[3], +m[1], +m[2]);
  if ((m = /^(\d{1,2})\/(\d{1,2})\/(\d{2})$/.exec(s))) return validDate(twoDigitYear(+m[3]), +m[1], +m[2]);
  if ((m = /^([A-Za-z]{3}) (\d{1,2}),? (\d{4})$/.exec(s)) && monthIndex(m[1], MONTHS_ABBR)) return validDate(+m[3], monthIndex(m[1], MONTHS_ABBR), +m[2]);
  if ((m = /^([A-Za-z]{3,9}) (\d{1,2}),? (\d{4})$/.exec(s)) && monthIndex(m[1], MONTHS_FULL)) return validDate(+m[3], monthIndex(m[1], MONTHS_FULL), +m[2]);
  if ((m = /^(\d{1,2})-([A-Za-z]{3})-(\d{4})$/.exec(s)) && monthIndex(m[2], MONTHS_ABBR)) return validDate(+m[3], monthIndex(m[2], MONTHS_ABBR), +m[1]);
  if ((m = /^(\d{1,2})-([A-Za-z]{3})-(\d{2})$/.exec(s)) && monthIndex(m[2], MONTHS_ABBR)) return validDate(twoDigitYear(+m[3]), monthIndex(m[2], MONTHS_ABBR), +m[1]);
  if ((m = /^(\d{1,2}) ([A-Za-z]{3}) (\d{4})$/.exec(s)) && monthIndex(m[2], MONTHS_ABBR)) return validDate(+m[3], monthIndex(m[2], MONTHS_ABBR), +m[1]);
  if ((m = /^(\d{4})\/(\d{2})\/(\d{2})$/.exec(s))) return validDate(+m[1], +m[2], +m[3]);
  return null;
}

// ---------------- CSV ----------------

function detectDelimiter(text) {
  const first = text.split('\n')[0];
  const count = (c) => first.split(c).length - 1;
  const comma = count(','), semi = count(';'), tab = count('\t');
  return tab > comma && tab > semi ? '\t' : semi > comma ? ';' : ',';
}

export function parseCsv(text, name = 'CSV') {
  if (text.charCodeAt(0) === 0xfeff) text = text.slice(1);
  const delimiter = detectDelimiter(text);
  const rows = [];
  let row = [], cell = '', quoted = false;
  const cap = (r) => (r.length > MAX_COLUMNS ? r.slice(0, MAX_COLUMNS) : r);
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quoted) {
      if (c === '"') { if (text[i + 1] === '"') { cell += '"'; i++; } else quoted = false; } else cell += c;
    } else if (c === '"') quoted = true;
    else if (c === delimiter) { row.push(cell); cell = ''; }
    else if (c === '\n' || c === '\r') {
      if (c === '\r' && text[i + 1] === '\n') i++;
      row.push(cell); cell = '';
      if (row.some((x) => x.length > 0)) rows.push(cap(row));
      row = [];
      if (rows.length > MAX_ROWS) throw new PortfolioError('The file has more than 20,000 rows, which is more than this tool will read.');
    } else cell += c;
  }
  row.push(cell);
  if (row.some((x) => x.length > 0)) rows.push(cap(row));
  return shape(rows, name);
}

function shape(rows, name) {
  if (rows.length === 0) throw new PortfolioError('The file is empty.');
  // the header row is the first of the first ten that names an ANDA column; otherwise the first row
  let header = 0;
  for (let i = 0; i < Math.min(10, rows.length); i++) if (rows[i].some((h) => looks(h, 'number'))) { header = i; break; }
  return { headers: rows[header].map((h) => h.trim()), rows: rows.slice(header + 1), sheetName: name };
}

// ---------------- XLSX ----------------

const isBuiltInDate = (id) => (id >= 14 && id <= 22) || (id >= 27 && id <= 36) || (id >= 45 && id <= 47) || (id >= 50 && id <= 58);
export function isDateFormat(code) {
  let s = '', quote = false, bracket = false;
  for (const c of code) {
    if (c === '"') { quote = !quote; continue; }
    if (quote) continue;
    if (c === '[') { bracket = true; continue; }
    if (c === ']') { bracket = false; continue; }
    if (!bracket) s += c.toLowerCase();
  }
  return /[dmyhs]/.test(s) && !s.includes('general');
}

export function fromSerial(serial, date1904) {
  if (serial < 1 || serial > 2958465) return String(serial);
  const base = date1904 ? Date.UTC(1904, 0, 1) : Date.UTC(1899, 11, 30);
  return toIso(base + Math.floor(serial) * DAY);
}

export function columnIndex(ref) {
  let n = 0;
  for (const ch of ref) { const u = ch.toUpperCase(); if (u < 'A' || u > 'Z') break; n = n * 26 + (u.charCodeAt(0) - 64); }
  return Math.max(0, n - 1);
}

async function text(bytes, entry, cap) { return new TextDecoder('utf-8').decode(await readEntry(bytes, entry, cap)); }

export async function readXlsx(bytes) {
  if (bytes.length > MAX_FILE_BYTES) throw new PortfolioError('The file is larger than 25 MB, which is more than this tool will read.');
  let entries;
  try { entries = listEntries(bytes); } catch (e) { if (e instanceof ZipError) throw new PortfolioError('The file could not be read. It may be damaged, password-protected or not a real workbook.'); throw e; }
  let total = 0;
  for (const e of entries.values()) total += e.usize;
  if (total > MAX_UNZIPPED_BYTES) throw new PortfolioError('The workbook expands to an unreasonable size, so it was not opened.');
  const damaged = new PortfolioError('The file could not be read. It may be damaged, password-protected or not a real workbook.');
  try {
    const rels = {};
    if (entries.has('xl/_rels/workbook.xml.rels'))
      parseXml(await text(bytes, entries.get('xl/_rels/workbook.xml.rels'), 5e6), { open: (n, a) => { if (n === 'Relationship') rels[a.Id ?? ''] = a.Target ?? ''; } });
    if (!entries.has('xl/workbook.xml')) throw new PortfolioError('That does not look like an Excel workbook.');
    let date1904 = false;
    const sheets = [];
    parseXml(await text(bytes, entries.get('xl/workbook.xml'), 5e6), {
      open: (n, a) => {
        if (n === 'workbookPr') date1904 = a.date1904 === '1' || a.date1904 === 'true';
        else if (n === 'sheet' && a.state !== 'hidden' && a.state !== 'veryHidden') {
          const target = rels[a.id ?? ''];
          if (target !== undefined) sheets.push({ name: a.name ?? 'Sheet', target: target.startsWith('/') ? target.slice(1) : 'xl/' + target });
        }
      },
    });
    if (sheets.length === 0) throw new PortfolioError('The workbook has no visible sheets.');

    const shared = [];
    if (entries.has('xl/sharedStrings.xml')) {
      let inSi = false, rph = 0, inT = false, cur = '';
      parseXml(await text(bytes, entries.get('xl/sharedStrings.xml'), 100e6), {
        open: (n) => { if (n === 'si') { inSi = true; cur = ''; } else if (n === 'rPh') rph++; else if (n === 't' && inSi) inT = true; },
        text: (t) => { if (inSi && inT && rph === 0) cur += t; },
        close: (n) => { if (n === 'si') { shared.push(cur); inSi = false; } else if (n === 'rPh') rph--; else if (n === 't') inT = false; },
      });
    }

    const dateXf = new Set();
    if (entries.has('xl/styles.xml')) {
      const custom = {}, xfs = [];
      let inCellXfs = false;
      parseXml(await text(bytes, entries.get('xl/styles.xml'), 20e6), {
        open: (n, a) => {
          if (n === 'numFmt' && a.numFmtId !== undefined) custom[+a.numFmtId] = a.formatCode ?? '';
          else if (n === 'cellXfs') inCellXfs = true;
          else if (n === 'xf' && inCellXfs) xfs.push(Number.isInteger(+a.numFmtId) ? +a.numFmtId : 0);
        },
        close: (n) => { if (n === 'cellXfs') inCellXfs = false; },
      });
      xfs.forEach((id, i) => { if (id in custom ? isDateFormat(custom[id]) : isBuiltInDate(id)) dateXf.add(i); });
    }

    let first = null;
    for (const { name, target } of sheets) {
      const entry = entries.get(target);
      if (!entry) continue;
      const table = readSheet(await text(bytes, entry, 120e6), name, shared, dateXf, date1904);
      first = first ?? table;
      if (table.headers.some((h) => looks(h, 'number'))) return table;   // the first sheet that has an ANDA column
    }
    if (first) return first;
    throw new PortfolioError('No readable sheet was found in the workbook.');
  } catch (e) {
    if (e instanceof PortfolioError) throw e;
    throw damaged;
  }
}

function readSheet(xml, name, shared, dateXf, date1904) {
  const rows = [];
  let row = null, cell = null, inV = false, inIs = false, inT = false;
  parseXml(xml, {
    open: (n, a) => {
      if (n === 'row') row = [];
      else if (n === 'c' && row) cell = { col: columnIndex(a.r ?? ''), type: a.t ?? '', style: Number.isInteger(+a.s) ? +a.s : 0, value: '' };
      else if (n === 'v' && cell) inV = true;
      else if (n === 'is' && cell) inIs = true;
      else if (n === 't' && cell && inIs) inT = true;
    },
    text: (t) => { if (cell && (inV || (inIs && inT))) cell.value += t; },
    close: (n) => {
      if (n === 'v') inV = false;
      else if (n === 't') inT = false;
      else if (n === 'is') inIs = false;
      else if (n === 'c' && cell && row) {
        let v = cell.value;
        switch (cell.type) {
          case 's': { const idx = Number.isInteger(+v) && v !== '' ? +v : -1; v = idx >= 0 && idx < shared.length ? shared[idx] : ''; break; }
          case 'b': v = v === '1' ? 'TRUE' : 'FALSE'; break;
          case 'e': v = ''; break;
          case 'str': case 'inlineStr': break;
          default: { const serial = Number(v); if (dateXf.has(cell.style) && v.trim() !== '' && Number.isFinite(serial)) v = fromSerial(serial, date1904); }
        }
        if (cell.col < MAX_COLUMNS) {
          while (row.length < cell.col) row.push('');
          if (row.length === cell.col) row.push(v.trim()); else row[cell.col] = v.trim();
        }
        cell = null;
      } else if (n === 'row') {
        if (row && row.some((x) => x.length > 0)) rows.push(row);
        row = null;
        if (rows.length > MAX_ROWS) throw new PortfolioError('The sheet has more than 20,000 rows, which is more than this tool will read.');
      }
    },
  });
  return shape(rows, name);
}

// ---------------- reading a file ----------------

/** @param bytes Uint8Array @param name the file's name */
export async function readTable(bytes, name) {
  const ext = (name.includes('.') ? name.slice(name.lastIndexOf('.')) : '').toLowerCase();
  if (ext === '.xlsx' || ext === '.xlsm') return readXlsx(bytes);
  if (ext === '.csv' || ext === '.txt' || ext === '.tsv') {
    if (bytes.length > MAX_FILE_BYTES) throw new PortfolioError('The file is larger than 25 MB, which is more than this tool will read.');
    return parseCsv(new TextDecoder('utf-8').decode(bytes), name);
  }
  throw new PortfolioError(`Files of type ${ext || '(none)'} are not supported. Use .xlsx or .csv (in Excel: File, Save As, CSV).`);
}

// ---------------- the repository ----------------

const MON_ABBR_TITLE = MONTHS_ABBR.map((m) => m[0].toUpperCase() + m.slice(1));
const pad2 = (n) => String(n).padStart(2, '0');
const first = (s) => (s.split(/[ ,]+/).filter((x) => x.length > 0)[0] ?? '');

/**
 * @param table {headers, rows}  @param opts {name, overrides, savedAt: {y, m, d, hh, mm} (local), today: 'YYYY-MM-DD'}
 * Returns {status, headers, mapping, numbers(), find(number, todayIso), records(todayIso)}.
 */
export function openPortfolio(table, opts) {
  const { name, overrides = null, savedAt } = opts;
  const mapping = detectColumns(table.headers, overrides);
  const savedIso = toIso(Date.UTC(savedAt.y, savedAt.m - 1, savedAt.d));
  const result = { headers: table.headers, mapping, rowsByNumber: new Map(), status: { ok: false, count: 0, message: 'Not loaded.' } };
  if (!mapping.number) {
    result.status = { ok: false, count: 0, message: 'No column for the ANDA number was found. Choose which column holds it in Settings.' };
    return finish(result, name, savedIso);
  }
  const col = (key) => (mapping[key] !== undefined ? table.headers.indexOf(mapping[key]) : -1);
  const cell = (r, i) => (i >= 0 && i < r.length ? r[i].trim() : '');
  const date = (r, i) => parseDate(cell(r, i));
  const c = Object.fromEntries(FIELDS.map(([k]) => [k, col(k)]));
  let skipped = 0;
  for (const r of table.rows) {
    const number = extractNumber(cell(r, c.number));
    if (number === null) { skipped++; continue; }
    result.rowsByNumber.set(number, {
      number, drug: cell(r, c.drug), applicant: cell(r, c.applicant), goal: date(r, c.goal), forecast: date(r, c.forecast), status: cell(r, c.status),
      notes: cell(r, c.notes), cycle: cell(r, c.cycle), submitted: date(r, c.submitted), ingredient: cell(r, c.ingredient), form: cell(r, c.form),
      strength: cell(r, c.strength), psg: date(r, c.psg),
    });
  }
  const n = result.rowsByNumber.size;
  const missing = [['goal', 'goal date'], ['forecast', 'forecast'], ['drug', 'drug']].filter(([k]) => !mapping[k]).map(([, label]) => label).join(', ');
  const saved = `${MON_ABBR_TITLE[savedAt.m - 1]} ${savedAt.d}, ${pad2(savedAt.hh)}:${pad2(savedAt.mm)}`;
  result.status = {
    ok: true, count: n,
    message: `Loaded ${n} ANDA${n === 1 ? '' : 's'} from ${name} (file saved ${saved}).` +
      (skipped > 0 ? ` ${skipped} row${skipped === 1 ? '' : 's'} without a readable ANDA number skipped.` : '') +
      (missing.length > 0 ? ` Not found in your sheet: ${missing}.` : ''),
  };
  return finish(result, name, savedIso);
}

function finish(result, name, savedIso) {
  const rows = result.rowsByNumber;
  const sameProduct = (a, b) => {
    if (a.ingredient.length > 0 && b.ingredient.length > 0) return a.ingredient.toLowerCase() === b.ingredient.toLowerCase();
    return first(a.drug).length > 0 && first(a.drug).toLowerCase() === first(b.drug).toLowerCase();
  };
  const find = (number, todayIso) => {
    const row = rows.get(number);
    if (!row) return null;
    const today = parseIso(todayIso);
    const goalT = parseIso(row.goal), fcT = parseIso(row.forecast);
    const daysToGoal = goalT === null ? null : Math.round((goalT - today) / DAY);
    const goalDays = daysToGoal === null ? '' : daysToGoal === 0 ? '· today' : daysToGoal === 1 ? '· 1 day' : daysToGoal === -1 ? '· 1 day ago' : daysToGoal > 1 ? `· ${daysToGoal} days` : `· ${-daysToGoal} days ago`;
    let forecastText = UNKNOWN, delta = 'no forecast in your sheet', late = false, deltaDays = null;
    if (fcT !== null) {
      forecastText = fmtLong(fcT);
      if (goalT !== null) {
        const d = Math.round((fcT - goalT) / DAY);
        deltaDays = d; late = d > 0;
        delta = d > 0 ? `▲ +${d} d vs goal` : d < 0 ? `▼ ${-d} d ahead of goal` : 'on goal';
      } else delta = 'no goal date to compare';
    }
    const notes = row.notes.length > 0 ? [{ who: 'Your sheet', text: row.notes }] : [];
    const sisters = [...rows.values()]
      .filter((x) => x.number !== number && row.applicant.length > 0 && x.applicant.toLowerCase() === row.applicant.toLowerCase() && sameProduct(x, row))
      .slice(0, 3).map((x) => ({ number: x.number, description: `${x.drug.length > 0 ? x.drug : 'same product'} — same applicant` }));
    const meta = [row.applicant, row.cycle.length > 0 ? `Cycle ${row.cycle}` : ''].filter((x) => x.length > 0).join(' · ');
    const saved = parseIso(savedIso);
    return {
      number, kind: 'ANDA', drug: row.drug.length > 0 ? row.drug : UNKNOWN, meta: meta.length > 0 ? meta : 'From your portfolio',
      goalDate: goalT === null ? UNKNOWN : fmtLong(goalT), goalDays, daysToGoal, forecastDeltaDays: deltaDays, progressPct: -1,
      forecastDate: forecastText, forecastDelta: delta, forecastLate: late, forecastConfidence: row.forecast === null ? '' : 'as entered in your sheet',
      attention: null, latest: row.status.length > 0 ? row.status : UNKNOWN,
      dataAsOf: `your portfolio file, saved ${monthName(new Date(saved).getUTCMonth())} ${new Date(saved).getUTCDate()} · ${name}`,
      ahead: null, pending: [], notes,
      forecastDetail: 'Dates and notes come from your portfolio file, as you entered them. This tool does not compute a forecast for your data.',
      sisters, goalDateValue: row.goal, forecastDateValue: row.forecast, submittedDate: row.submitted, asOfDate: savedIso, demo: false,
      sourceLabel: `Your portfolio: ${name}`,
      activeIngredient: row.ingredient.length > 0 ? row.ingredient : null, dosageForm: row.form.length > 0 ? row.form : null,
      strength: row.strength.length > 0 ? row.strength : null, psgDateFromSheet: row.psg,
    };
  };
  return {
    status: result.status, headers: result.headers, mapping: result.mapping,
    numbers: () => [...rows.keys()], find,
    records: (todayIso) => [...rows.keys()].map((n) => find(n, todayIso)),
  };
}
