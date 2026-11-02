// The draft check: compares what an email or chat draft says about an ANDA (the number, a goal or forecast date, an on-track or late
// statement) with the record. Deterministic: no model, no network, the same draft always gives the same answer. It says exactly what it
// checked and what it did not. Mirrors the app's Services/DraftCheck.cs.
import { extractNumber, findNumbers } from './recognizer.js';
import { parseIso, monthName } from './calendar.js';
import { hasForecast } from './watch.js';

const DATE_RE = new RegExp(
  '(?<![\\d/])(?:(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\\.?\\s+(\\d{1,2})(?:st|nd|rd|th)?(?:,?\\s+(\\d{4}))?(?!\\d)' +
  '|(\\d{1,2})/(\\d{1,2})(?:/(\\d{4}|\\d{2}))?(?![\\d/])' +
  '|(\\d{4})-(\\d{2})-(\\d{2}))', 'gi');
const ON_TRACK = /\b(on track|on schedule|ahead of (?:the )?(?:goal|schedule)|ahead of goal)\b/i;
const LATE = /\b(running late|behind schedule|behind|delayed|at risk|slipping|slipped|past (?:the )?goal|will miss|going to miss)\b/i;
const MONTHS = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];

function trimSentence(s) { s = s.trim(); return s.length <= 90 ? s : s.slice(0, 87) + '...'; }

function toMention(m) {
  if (m[1]) {
    const mon = MONTHS.indexOf(m[1].slice(0, 3).toLowerCase()) + 1, day = +m[2], year = m[3] ? +m[3] : null;
    return mon >= 1 && mon <= 12 && day >= 1 && day <= 31 ? { mon, day, year, index: m.index, length: m[0].length } : null;
  }
  if (m[4]) {
    const mon = +m[4], day = +m[5];
    let year = null;
    if (m[6]) { year = +m[6]; if (year < 100) year += 2000; }
    return mon >= 1 && mon <= 12 && day >= 1 && day <= 31 ? { mon, day, year, index: m.index, length: m[0].length } : null;
  }
  if (m[7]) return { mon: +m[8], day: +m[9], year: +m[7], index: m.index, length: m[0].length };
  return null;
}

function keyword(sentence, mt) {
  const start = Math.max(0, mt.index - 50);
  const before = sentence.slice(start, mt.index).toLowerCase();
  const g = before.lastIndexOf('goal');
  const f = Math.max(before.lastIndexOf('forecast'), before.lastIndexOf('projected'));
  if (g >= 0 || f >= 0) return g > f ? 'goal' : 'forecast';
  const after = sentence.slice(mt.index + mt.length, Math.min(sentence.length, mt.index + mt.length + 30)).toLowerCase();
  const ag = after.indexOf('goal'), af = after.indexOf('forecast');
  if (ag >= 0 && (af < 0 || ag < af)) return 'goal';
  if (af >= 0) return 'forecast';
  return null;
}

const matches = (mt, iso) => {
  const t = parseIso(iso);
  if (t === null) return false;
  const d = new Date(t);
  return mt.mon === d.getUTCMonth() + 1 && mt.day === d.getUTCDate() && (mt.year === null || mt.year === d.getUTCFullYear());
};
const showMention = (mt) => `${monthName(Math.min(12, Math.max(1, mt.mon)) - 1)} ${mt.day}` + (mt.year !== null ? `, ${mt.year}` : '');
const longIso = (iso) => { const d = new Date(parseIso(iso)); return `${monthName(d.getUTCMonth())} ${d.getUTCDate()}, ${d.getUTCFullYear()}`; };

/** @param lookup (number) => record | null */
export function checkDraft(draft, lookup) {
  const findings = [];
  if (!draft || !draft.trim())
    return { findings: [{ kind: 'note', text: 'Nothing to check yet. Paste or type a draft.' }], numbersChecked: 0, datesChecked: 0, statusChecked: 0 };

  let numbersChecked = 0, datesChecked = 0, statusChecked = 0, multi = 0;
  const seenNumbers = new Set(), reportedUnknown = new Set(), seen = new Set();
  const add = (kind, text) => { if (!seen.has(kind + text)) { seen.add(kind + text); findings.push({ kind, text }); } };

  for (const paragraph of draft.split(/\r?\n\s*\r?\n/)) {
    let context = null;
    for (const sentence of paragraph.split(/(?<=[.!?])\s+|\r?\n/).filter((s) => s.trim().length > 0)) {
      const numbers = findNumbers(sentence);
      for (const n of numbers) {
        if (!seenNumbers.has(n)) { seenNumbers.add(n); numbersChecked++; }
        if (lookup(n) === null && !reportedUnknown.has(n)) {
          reportedUnknown.add(n);
          add('unknown', `ANDA ${n} is not in your data, so nothing about it could be checked. Check the number.`);
        }
      }

      let subject;
      if (numbers.length === 1) { subject = numbers[0]; context = subject; }
      else if (numbers.length > 1) {
        subject = null; context = null;
        if (++multi <= 3) add('note', `One sentence names several ANDAs, so its dates and wording were not checked: "${trimSentence(sentence)}"`);
      } else subject = context;

      const record = subject === null ? null : lookup(subject);
      if (!record) continue;

      DATE_RE.lastIndex = 0;
      let m;
      while ((m = DATE_RE.exec(sentence))) {
        const mention = toMention(m);
        if (!mention) continue;
        const which = keyword(sentence, mention);
        if (!which) continue;
        const actual = which === 'goal' ? record.goalDateValue : hasForecast(record) ? record.forecastDateValue : null;
        if (!actual) continue;
        datesChecked++;
        if (!matches(mention, actual))
          add('diff', `ANDA ${subject}: the draft gives the ${which} date as ${showMention(mention)}, but the record says ${longIso(actual)}.`);
      }

      if (hasForecast(record)) {
        const claimsOnTrack = ON_TRACK.test(sentence), claimsLate = LATE.test(sentence);
        const delta = record.forecastDelta.replace('▲', '').replace('▼', '').trim();
        if (claimsOnTrack) statusChecked++;
        if (claimsLate) statusChecked++;
        if (claimsOnTrack && record.forecastLate) add('diff', `ANDA ${subject}: the draft sounds on track, but the record's forecast is ${delta}.`);
        if (claimsLate && !record.forecastLate) add('diff', `ANDA ${subject}: the draft sounds late or at risk, but the record's forecast is ${delta}.`);
      }
    }
  }

  const ordered = findings.map((f, i) => ({ f, i })).sort((a, b) => (a.f.kind === 'note' ? 1 : 0) - (b.f.kind === 'note' ? 1 : 0) || a.i - b.i).map((x) => x.f);
  if (numbersChecked === 0) ordered.unshift({ kind: 'note', text: 'No ANDA numbers found in the draft, so nothing was checked against a record.' });
  else if (!ordered.some((f) => f.kind === 'diff' || f.kind === 'unknown'))
    ordered.unshift({ kind: 'ok', text: `Checked ${numbersChecked} ANDA number${numbersChecked === 1 ? '' : 's'}, ${datesChecked} goal or forecast date${datesChecked === 1 ? '' : 's'} and ${statusChecked} on-track or late statement${statusChecked === 1 ? '' : 's'}: no differences from the record.` });
  ordered.push({ kind: 'note', text: 'This compares the draft only with the data this tool has, and only numbers, goal and forecast dates, and on-track or late wording. It does not check other wording and is not the system of record.' });
  return { findings: ordered, numbersChecked, datesChecked, statusChecked };
}

export { extractNumber };
