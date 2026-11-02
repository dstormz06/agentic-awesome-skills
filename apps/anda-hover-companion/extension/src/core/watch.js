// The "watch your back" rules: deterministic flags with their evidence and source. They state what the data shows and where to look;
// they never decide what a goal-date rule means. Mirrors the app's Services/WatchRules.cs and Services/UrgencyPolicy.cs.
import { DAY, parseIso, fmtShort, fmtMonthDay, fmtLong } from './calendar.js';

export const SEVERITY = { info: 0, warn: 1, bad: 2 };
const RANK = { 'goal-passed': 0, 'goal-soon': 1, 'goal-date-calendar': 2, 'forecast-date-calendar': 3, 'psg-after-submission': 4, 'stale-data': 5, 'no-goal-date': 6 };
export const UNKNOWN = '—';
export const hasForecast = (r) => r.forecastDate !== UNKNOWN;
export const hasProgress = (r) => r.progressPct >= 0;

/** @param realToday UTC midnight ms. @param cal BusinessCalendar. @param settings {lead, staleDays, goalSoonDays} */
export function evaluateWatch(r, realToday, cal, settings = {}) {
  const lead = settings.lead ?? 3, staleDays = settings.staleDays ?? 7, goalSoonDays = settings.goalSoonDays ?? 5;
  const flags = [];
  // fictional records are frozen at their extract date; judging them against the real calendar would call them "overdue"
  const asOf = parseIso(r.asOfDate);
  const today = r.demo && asOf !== null ? asOf : realToday;
  const source = r.sourceLabel || 'the record';
  const CAL = 'U.S. federal calendar (OPM)';

  const dateRule = (code, what, t) => {
    if (t === null) return;
    const f = cal.evaluate(t, lead);
    if (f.kind === 'weekend')
      flags.push({ code, severity: 'warn', text: `${what} date falls on a weekend: ${fmtShort(t)}. Nearest business days: ${fmtShort(f.prev)} and ${fmtShort(f.next)}.`,
        why: 'A date on a weekend may be handled differently from a weekday. Check how it applies to this action; this tool does not decide.', source: CAL });
    else if (f.kind === 'holiday')
      flags.push({ code, severity: 'warn', text: `${what} date falls on a federal holiday: ${fmtShort(t)} (${f.name}). Nearest business days: ${fmtShort(f.prev)} and ${fmtShort(f.next)}.`,
        why: 'A date on a holiday may be handled differently from a workday. Check how it applies to this action; this tool does not decide.', source: CAL });
    else if (f.near !== null)
      flags.push({ code, severity: 'info', text: `${what} date ${fmtShort(t)} is ${f.near} business day${f.near === 1 ? '' : 's'} ${f.before ? 'before' : 'after'} ${f.name}.`,
        why: 'Staffing and turnaround around a federal holiday can differ. Worth a look if the date is tight.', source: CAL });
  };

  const goal = parseIso(r.goalDateValue);
  dateRule('goal-date-calendar', 'Goal', goal);
  dateRule('forecast-date-calendar', 'Forecast', hasForecast(r) ? parseIso(r.forecastDateValue) : null);

  if (goal !== null) {
    const days = Math.round((goal - today) / DAY);
    if (days < 0)
      flags.push({ code: 'goal-passed', severity: 'bad', text: `The goal date (${fmtShort(goal)}) passed ${-days} day${days === -1 ? '' : 's'} ago.`,
        why: 'The record still lists this goal date. Confirm whether it moved or the data is out of date.', source });
    else if (days <= goalSoonDays)
      flags.push({ code: 'goal-soon', severity: 'warn', text: days === 0 ? `The goal date is today (${fmtShort(goal)}).` : `The goal date (${fmtShort(goal)}) is in ${days} day${days === 1 ? '' : 's'}.`,
        why: 'A near goal date is easy to miss in a long list.', source });
  } else {
    flags.push({ code: 'no-goal-date', severity: 'info', text: 'No goal date in the data for this ANDA.',
      why: 'Without a goal date nothing can be checked against the calendar.', source });
  }

  if (!r.demo && asOf !== null) {
    const age = Math.round((today - asOf) / DAY);
    if (age > staleDays)
      flags.push({ code: 'stale-data', severity: 'warn', text: `This data is ${age} days old (as of ${fmtMonthDay(asOf)}).`,
        why: 'A stale fact shown confidently is the riskiest kind. Refresh your source before relying on it.', source });
  }

  const submitted = parseIso(r.submittedDate), psg = parseIso(r.psgDateFromSheet);
  if (submitted !== null && psg !== null && psg > submitted)
    flags.push({ code: 'psg-after-submission', severity: 'warn',
      text: `A PSG date in your sheet (${fmtLong(psg)}) is after this ANDA was submitted (${fmtLong(submitted)}).`,
      why: 'A product-specific guidance change after submission may matter. Possible impact: check; this tool does not judge it.', source: 'Your sheet' });

  return flags
    .map((f, i) => ({ f, i }))
    .sort((a, b) => SEVERITY[b.f.severity] - SEVERITY[a.f.severity] || (RANK[a.f.code] ?? 9) - (RANK[b.f.code] ?? 9) || (a.f.code < b.f.code ? -1 : a.f.code > b.f.code ? 1 : 0) || a.i - b.i)
    .map((x) => x.f);
}

export const LATE_DAY_CHOICES = [3, 7, 14];
export const normalizeLateDays = (n) => (LATE_DAY_CHOICES.includes(n) ? n : 7);

/** An urgent ANDA: a bad attention item, a forecast late by `lateDays` or more, or any "bad" watch flag (a passed goal date). */
export function isUrgent(r, realToday, cal, lateDays = 7) {
  if (r.attention && r.attention.level === 'bad') return true;
  if (hasForecast(r) && r.forecastDeltaDays !== null && r.forecastDeltaDays !== undefined && r.forecastDeltaDays >= lateDays) return true;
  return evaluateWatch(r, realToday, cal).some((f) => f.severity === 'bad');
}

export function urgentReason(r, realToday, cal, lateDays = 7) {
  if (r.attention && r.attention.level === 'bad') return r.attention.text;
  if (hasForecast(r) && r.forecastDeltaDays !== null && r.forecastDeltaDays !== undefined && r.forecastDeltaDays >= lateDays) return `Forecast is ${r.forecastDeltaDays} days after the goal`;
  const f = evaluateWatch(r, realToday, cal).find((x) => x.severity === 'bad');
  return f ? f.text : null;
}

/** What a capture does first: 'full' opens the card, 'peek' the small one, 'invite' a small chip you click. */
export function firstStep(consent, escalateUrgent, urgent) {
  if (consent === 'whisper') return escalateUrgent && urgent ? 'peek' : 'invite';
  if (consent === 'peek') return 'peek';
  return 'full';
}
