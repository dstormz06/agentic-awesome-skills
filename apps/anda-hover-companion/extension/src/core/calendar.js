// U.S. federal holidays from the statutory rules, and business days. A holiday on a Saturday is observed on the Friday, on a Sunday
// on the Monday (OPM's rule for a Monday-to-Friday schedule). Dates are UTC midnights in milliseconds, so day arithmetic is exact.
// Mirrors the app's Services/BusinessCalendar.cs; the committed golden fixture keeps the two identical.
export const DAY = 864e5;
const DOW = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

export const ymd = (y, m, d) => Date.UTC(y, m - 1, d);
export const dowOf = (t) => new Date(t).getUTCDay();   // 0 = Sunday
export function parseIso(s) {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s || '');
  return m ? Date.UTC(+m[1], +m[2] - 1, +m[3]) : null;
}
export const toIso = (t) => new Date(t).toISOString().slice(0, 10);
export const fmtShort = (t) => { const d = new Date(t); return `${DOW[d.getUTCDay()]} ${MON[d.getUTCMonth()]} ${d.getUTCDate()}`; };
export const fmtMonthDay = (t) => { const d = new Date(t); return `${MON[d.getUTCMonth()]} ${d.getUTCDate()}`; };
export const fmtLong = (t) => { const d = new Date(t); return `${MON[d.getUTCMonth()]} ${d.getUTCDate()}, ${d.getUTCFullYear()}`; };
export const monthName = (i) => MON[i];

export function holidaysOf(year) {
  const list = [];
  const fixed = (m, d, name) => {
    const actual = ymd(year, m, d), w = dowOf(actual);
    list.push({ observed: w === 6 ? actual - DAY : w === 0 ? actual + DAY : actual, actual, name, regional: false });
  };
  const nth = (m, wd, n, name) => {
    let t = ymd(year, m, 1);
    while (dowOf(t) !== wd) t += DAY;
    t += 7 * (n - 1) * DAY;
    list.push({ observed: t, actual: t, name, regional: false });
  };
  const last = (m, wd, name) => {
    let t = Date.UTC(year, m, 0);   // day 0 of the next month is the last day of this one
    while (dowOf(t) !== wd) t -= DAY;
    list.push({ observed: t, actual: t, name, regional: false });
  };
  fixed(1, 1, "New Year's Day");
  nth(1, 1, 3, 'Birthday of Martin Luther King, Jr.');
  nth(2, 1, 3, "Washington's Birthday");
  last(5, 1, 'Memorial Day');
  fixed(6, 19, 'Juneteenth National Independence Day');
  fixed(7, 4, 'Independence Day');
  nth(9, 1, 1, 'Labor Day');
  nth(10, 1, 2, 'Columbus Day');
  fixed(11, 11, 'Veterans Day');
  nth(11, 4, 4, 'Thanksgiving Day');
  fixed(12, 25, 'Christmas Day');
  if ((year - 1) % 4 === 0) {   // Inauguration Day: Jan 20, the Monday if it falls on a Sunday; Washington, DC area only
    const actual = ymd(year, 1, 20);
    list.push({ observed: dowOf(actual) === 0 ? actual + DAY : actual, actual, name: 'Inauguration Day (Washington, DC area)', regional: true });
  }
  return list;
}

export class BusinessCalendar {
  /** @param extra closures declared at short notice, [{date: 'YYYY-MM-DD', name}] */
  constructor(extra = []) {
    this.extra = (extra || []).map((e) => ({ t: parseIso(e.date), name: e.name || 'Closure' })).filter((e) => e.t !== null);
  }

  holidayOn(t) {
    for (const e of this.extra) if (e.t === t) return { observed: t, actual: t, name: e.name };
    const year = new Date(t).getUTCFullYear();
    // a New Year's Day on a Saturday is observed on Dec 31 of the year before, so look one year ahead in December
    const all = holidaysOf(year).concat(new Date(t).getUTCMonth() === 11 ? holidaysOf(year + 1) : []);
    return all.find((h) => h.observed === t) || null;
  }

  kindOf(t) {
    const w = dowOf(t);
    if (w === 0 || w === 6) return 'weekend';
    return this.holidayOn(t) ? 'holiday' : 'business';
  }

  holidayName(t) { const h = this.holidayOn(t); return h ? h.name : null; }
  isBusinessDay(t) { return this.kindOf(t) === 'business'; }
  nextBusinessDay(t) { do t += DAY; while (!this.isBusinessDay(t)); return t; }
  previousBusinessDay(t) { do t -= DAY; while (!this.isBusinessDay(t)); return t; }
  /** Business days strictly between two dates (neither end counted). */
  businessDaysBetween(a, b) {
    if (a > b) [a, b] = [b, a];
    let n = 0;
    for (let d = a + DAY; d < b; d += DAY) if (this.isBusinessDay(d)) n++;
    return n;
  }

  /** Weekend, holiday, or a business day within `lead` business days before or after a federal holiday. */
  evaluate(t, lead = 3) {
    const kind = this.kindOf(t);
    let name = kind === 'holiday' ? this.holidayName(t) : null, near = null, before = false;
    if (kind === 'business' && lead > 0) {
      const window = lead * 2 + 4;
      for (let k = 1; k <= window && near === null; k++) {
        const ahead = t + k * DAY, behind = t - k * DAY;
        if (this.kindOf(ahead) === 'holiday') {
          const n = this.businessDaysBetween(t, ahead) + 1;
          if (n <= lead) { near = n; before = true; name = this.holidayName(ahead); }
        }
        if (near === null && this.kindOf(behind) === 'holiday') {
          const n = this.businessDaysBetween(behind, t) + 1;
          if (n <= lead) { near = n; before = false; name = this.holidayName(behind); }
        }
      }
    }
    return { kind, name, near, before, prev: this.previousBusinessDay(t), next: this.nextBusinessDay(t) };
  }
}
