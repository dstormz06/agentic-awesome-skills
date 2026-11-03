export const DEFAULT_SETTINGS = Object.freeze({
  consent: 'whisper',          // 'full' | 'peek' | 'whisper': the same three consent models as the app; whisper is the default
  escalateUrgent: true,        // in whisper mode an urgent ANDA opens a peek by itself
  urgentLateDays: 7,           // forecast this many days past goal (3, 7 or 14) counts as urgent
  holidayLeadDays: 3,          // flag dates within this many business days of a federal holiday (0, 2, 3 or 5)
  autoHideSeconds: 10,         // 0 = never; never hides while hovered, focused or expanded
  dataSource: 'demo',          // 'demo' | 'imported' | 'app'
  followApp: true,             // when connected, use the desktop app's settings
  extraClosures: [],           // [{date: 'YYYY-MM-DD', name}] closures declared at short notice
});
export const HOLIDAY_LEAD_CHOICES = [0, 2, 3, 5];
export const AUTO_HIDE_CHOICES = [0, 10, 20, 40];

export function normalizeSettings(s = {}) {
  const out = { ...DEFAULT_SETTINGS, ...s };
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
