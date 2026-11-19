import { TZ_CHOICES, STALE_CHOICES, STALE_DEFAULT } from './time.js';

/** The form of the stored settings. 1: before round 33 (no number was stored). 2: adds the time zone and the days before data is marked STALE. */
export const SETTINGS_SCHEMA = 2;

export const DEFAULT_SETTINGS = Object.freeze({
  schema: SETTINGS_SCHEMA,
  consent: 'whisper',          // 'full' | 'peek' | 'whisper': the same three consent models as the app; whisper is the default
  escalateUrgent: true,        // in whisper mode an urgent ANDA opens a peek by itself
  urgentLateDays: 7,           // forecast this many days past goal (3, 7 or 14) counts as urgent
  holidayLeadDays: 3,          // flag dates within this many business days of a federal holiday (0, 2, 3 or 5)
  autoHideSeconds: 10,         // 0 = never; never hides while hovered, focused or expanded
  dataSource: 'demo',          // 'demo' | 'imported' | 'app'
  followApp: true,             // when connected, use the desktop app's settings
  extraClosures: [],           // [{date: 'YYYY-MM-DD', name}] closures declared at short notice
  timeZone: '',                // '' = this computer's own zone, or one of the zones offered (core/time.js)
  staleAfterDays: STALE_DEFAULT, // data older than this many days is marked STALE (1, 3, 7, 14 or 30)
});
export const HOLIDAY_LEAD_CHOICES = [0, 2, 3, 5];
export const AUTO_HIDE_CHOICES = [0, 10, 20, 40];

/**
 * Settings as stored, brought to the current form. Settings stored by an earlier version are carried over and gain the new values at
 * their usual setting; a value that is not one of the choices goes back to its usual setting; anything this version does not know is dropped.
 */
export function normalizeSettings(s = {}) {
  const stored = s && typeof s === 'object' && !Array.isArray(s) ? s : {};
  const out = {};
  for (const k of Object.keys(DEFAULT_SETTINGS)) out[k] = Object.prototype.hasOwnProperty.call(stored, k) ? stored[k] : DEFAULT_SETTINGS[k];
  out.schema = SETTINGS_SCHEMA;
  if (!TZ_CHOICES.some((c) => c[0] === out.timeZone)) out.timeZone = '';
  if (!STALE_CHOICES.includes(out.staleAfterDays)) out.staleAfterDays = STALE_DEFAULT;
  if (!['full', 'peek', 'whisper'].includes(out.consent)) out.consent = 'whisper';
  out.escalateUrgent = out.escalateUrgent !== false;
  if (![3, 7, 14].includes(out.urgentLateDays)) out.urgentLateDays = 7;
  if (!HOLIDAY_LEAD_CHOICES.includes(out.holidayLeadDays)) out.holidayLeadDays = 3;
  if (!AUTO_HIDE_CHOICES.includes(out.autoHideSeconds)) out.autoHideSeconds = 20;
  if (!['demo', 'imported', 'app'].includes(out.dataSource)) out.dataSource = 'demo';
  out.followApp = out.followApp !== false;
  out.extraClosures = Array.isArray(out.extraClosures)
    ? out.extraClosures.filter((e) => e && /^\d{4}-\d{2}-\d{2}$/.test(e.date)).slice(0, 50).map((e) => ({ date: e.date, name: String(e.name || 'Closure').slice(0, 80) }))
    : [];
  return out;
}
