// Everything a card shows, already decided. The shape is the same one the app's native host sends, so the card drawn in
// "standalone" mode and the card drawn in "together" mode are the same card.
import { parseIso, fmtLong } from './calendar.js';
import { evaluateWatch, hasForecast, hasProgress, isUrgent, urgentReason, SEVERITY } from './watch.js';

export function statusLine(r) {
  const cycle = r.meta.includes('·') ? r.meta.slice(r.meta.lastIndexOf('·') + 1).trim() : r.meta.trim();
  let s = `${r.kind} ${r.number} (${r.drug}): ${cycle}`;
  if (hasProgress(r)) s += `, ${r.progressPct}% through review`;
  s += `; goal ${r.goalDate}`;
  const clean = (d) => d.replace('▲', '').replace('▼', '').trim();
  if (hasForecast(r)) s += `; forecast ${r.forecastDate} (${clean(r.forecastDelta)}, ${r.forecastConfidence})`;
  if (r.attention) s += `; needs action: ${r.attention.text}`;
  return s + '.';
}

export function offlineBrief(r) {
  let t = '';
  if (r.attention) t += `Open now: ${r.attention.text}. `;
  if (r.sisters.length > 0) t += `Related precedent: ANDA ${r.sisters[0].number} — ${r.sisters[0].description}. `;
  if (t.length === 0) t += 'No open items need action. ';
  t += 'Forecast drivers and assumptions: expand → Forecast & assumptions.';
  return t.trimEnd();
}

/** Same wording as the app (ReferenceResolver): always two lines, a date with its source or a brief reason there is none. */
export const NO_PSG = 'not available: no PSG date in your data, and no official PSG source is connected';
export const NO_RLD = 'not available: no Drugs@FDA file chosen (app: Settings, My data)';

/** The fictional demo records carry made-up reference dates so the layout can be shown. They say so. */
const DEMO_DATES = { '216482': ['2026-01-14', '2026-03-03'], '214990': ['2025-11-20', '2026-03-03'] };
const demoText = (r, i) => (r.demo && DEMO_DATES[r.number] ? `${fmtLong(parseIso(DEMO_DATES[r.number][i]))} · fictional demo date` : null);

/** The PSG line: a date the person keeps in their own sheet, labeled as theirs, or the fictional demo date. No official PSG source is connected. */
export function psgText(r) {
  const p = parseIso(r.psgDateFromSheet);
  return p === null ? null : `${fmtLong(p)} · from your sheet, not an official source`;
}

/** What the card's PSG and RLD lines say. The extension has no Drugs@FDA file of its own: with the app connected, the app answers. */
export function referenceFor(r) {
  return { psg: psgText(r) ?? demoText(r, 0) ?? NO_PSG, rld: demoText(r, 1) ?? NO_RLD };
}

/** @param ctx {today (UTC ms), calendar, lead, lateDays} */
export function buildCard(r, ctx) {
  const lead = ctx.lead ?? 3, lateDays = ctx.lateDays ?? 7;
  const flags = evaluateWatch(r, ctx.today, ctx.calendar, { lead });
  const urgent = isUrgent(r, ctx.today, ctx.calendar, lateDays);
  return {
    number: r.number, kind: r.kind, drug: r.drug, meta: r.meta, freshness: r.dataAsOf ?? null, source: r.sourceLabel ?? null, demo: !!r.demo,
    goalDate: r.goalDate, goalDays: r.goalDays, daysToGoal: r.daysToGoal ?? null, progress: hasProgress(r) ? r.progressPct : null,
    forecastDate: r.forecastDate, forecastDelta: r.forecastDelta, forecastLate: r.forecastLate, hasForecast: hasForecast(r),
    forecastDeltaDays: r.forecastDeltaDays ?? null, forecastConfidence: r.forecastConfidence,
    attention: r.attention ? { level: r.attention.level === 'bad' ? 'bad' : 'warn', text: r.attention.text } : null,
    latest: r.latest, ahead: r.ahead ?? null,
    watch: flags, watchMore: Math.max(0, flags.length - 1),
    reference: referenceFor(r),
    pending: r.pending, notes: r.notes, forecastDetail: r.forecastDetail, sisters: r.sisters,
    brief: { tag: 'OFFLINE BRIEF', text: offlineBrief(r), generated: false },
    statusLine: statusLine(r),
    urgency: { isUrgent: urgent, reason: urgent ? urgentReason(r, ctx.today, ctx.calendar, lateDays) : null },
  };
}

/** The portfolio-wide watch list: every record with something worth a second look, worst first. */
export function buildWatchList(records, ctx, limit = 500) {
  const items = [];
  let total = 0;
  for (const r of records) {
    total++;
    const flags = evaluateWatch(r, ctx.today, ctx.calendar, { lead: ctx.lead ?? 3 });
    const urgent = isUrgent(r, ctx.today, ctx.calendar, ctx.lateDays ?? 7);
    if (flags.length === 0 && !urgent) continue;
    const severity = flags.length === 0 ? 'info' : flags.reduce((m, f) => (SEVERITY[f.severity] > SEVERITY[m] ? f.severity : m), 'info');
    items.push({
      goal: parseIso(r.goalDateValue),
      item: { number: r.number, drug: r.drug, goalDate: r.goalDate, severity, topFlag: flags.length > 0 ? flags[0].text : null, flagCount: flags.length, urgent,
              urgentReason: urgent ? urgentReason(r, ctx.today, ctx.calendar, ctx.lateDays ?? 7) : null },
    });
  }
  items.sort((a, b) => SEVERITY[b.item.severity] - SEVERITY[a.item.severity]
    || (b.item.urgent ? 1 : 0) - (a.item.urgent ? 1 : 0)
    || (a.goal === null ? 1 : 0) - (b.goal === null ? 1 : 0)
    || (a.goal ?? Infinity) - (b.goal ?? Infinity)
    || (a.item.number < b.item.number ? -1 : a.item.number > b.item.number ? 1 : 0));
  return { items: items.slice(0, limit).map((x) => x.item), total, shown: items.length };
}
