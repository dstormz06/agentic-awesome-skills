// Time, as the simulation and the Windows app write it: the person's time zone, a moment as it is written, how long ago, and how fresh a
// piece of data is. The rules below are the simulation's own (mockup/windows-simulation.html, "Round 31: time"), copied without change;
// tests/unit/time.test.mjs holds them to the shared answers in mockup/round31-golden.json and mockup/round32-golden.json.
/* The zones offered. "" is the computer's own zone. */
export const TZ_CHOICES = [["", "Automatic: this computer's time zone"], ["America/New_York", "Eastern"], ["America/Chicago", "Central"], ["America/Denver", "Mountain"],
  ["America/Phoenix", "Arizona (no daylight time)"], ["America/Los_Angeles", "Pacific"], ["America/Anchorage", "Alaska"], ["Pacific/Honolulu", "Hawaii"], ["America/Puerto_Rico", "Atlantic (Puerto Rico)"], ["UTC", "UTC"]];
/* The abbreviations the tool knows: for each zone its winter and summer abbreviation and how far its standard time is from UTC, in minutes.
   The zones offered, and the other zones of the United States a computer set to "Automatic" may be in. */
export const TZ_ABBR = (() => {
  const t = {}, add = (std, dst, offset, ids) => ids.forEach(id => { t[id] = [std, dst, offset]; });
  add("EST", "EDT", -300, ["America/New_York", "America/Detroit", "America/Indiana/Indianapolis", "America/Indiana/Marengo", "America/Indiana/Petersburg", "America/Indiana/Vevay", "America/Indiana/Vincennes", "America/Indiana/Winamac", "America/Kentucky/Louisville", "America/Kentucky/Monticello"]);
  add("CST", "CDT", -360, ["America/Chicago", "America/Indiana/Knox", "America/Indiana/Tell_City", "America/Menominee", "America/North_Dakota/Center", "America/North_Dakota/New_Salem", "America/North_Dakota/Beulah"]);
  add("MST", "MDT", -420, ["America/Denver", "America/Boise"]); add("MST", "MST", -420, ["America/Phoenix"]);
  add("PST", "PDT", -480, ["America/Los_Angeles"]);
  add("AKST", "AKDT", -540, ["America/Anchorage", "America/Juneau", "America/Sitka", "America/Yakutat", "America/Nome"]);
  add("HST", "HST", -600, ["Pacific/Honolulu"]); add("HST", "HDT", -600, ["America/Adak"]);
  add("AST", "AST", -240, ["America/Puerto_Rico", "America/St_Thomas"]);
  add("ChST", "ChST", 600, ["Pacific/Guam", "Pacific/Saipan"]); add("SST", "SST", -660, ["Pacific/Pago_Pago"]);
  add("UTC", "UTC", 0, ["UTC", "Etc/UTC"]);
  return t;
})();
export const STALE_CHOICES = [1, 3, 7, 14, 30], STALE_DEFAULT = 7, TIME_MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
/* A moment the tool can write: a number, from the year 1900 to the year 9999 (a day inside the far end, so that any zone's wall clock is too). Anything else is "time not known". */
export const TIME_MIN = -2208988800000, TIME_MAX = 253402214399999;
export const timeKnown = ms => Number.isFinite(ms) && ms >= TIME_MIN && ms <= TIME_MAX;
/* Whether this computer knows a zone by that name. */
export function zoneKnown(zone){ try{ new Intl.DateTimeFormat("en-US", { timeZone:zone }); return !!zone; }catch(e){ return false; } }
/* The wall clock of a zone at a moment, as numbers, and how far that zone is from UTC then, in minutes. A zone that is not known is read as UTC. */
export function zoneClock(ms, zone){
  const read = z => { const o = {}; new Intl.DateTimeFormat("en-US", { timeZone:z, hourCycle:"h23", year:"numeric", month:"numeric", day:"numeric", hour:"numeric", minute:"numeric", second:"numeric" })
    .formatToParts(new Date(ms)).forEach(x => { if(x.type !== "literal") o[x.type] = +x.value; }); return o; };
  let p; try{ p = read(zone || "UTC"); }catch(e){ p = read("UTC"); }
  p.offset = Math.round((Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second) - Math.floor(ms / 1000) * 1000) / 60000);
  return p;
}
/* "EST" or "EDT" where the tool knows the zone, the moment is in 2007 or later (the years these zones have kept the hours they keep
   now), and the zone is then at its standard time or an hour ahead of it. In every other case (a zone the tool has no abbreviation for,
   an earlier year, a zone this computer does not know) the difference from UTC ("UTC+5:30"), which is always right. */
export const ABBR_FROM = Date.UTC(2007, 0, 1);
export function zoneAbbr(ms, zone){
  const now = zoneClock(ms, zone), pair = TZ_ABBR[zone];
  if(pair && ms >= ABBR_FROM){
    if(now.offset === pair[2]) return pair[0];
    if(now.offset === pair[2] + 60 && pair[1] !== pair[0]) return pair[1];
  }
  const m = Math.abs(now.offset);
  return now.offset === 0 ? "UTC" : "UTC" + (now.offset < 0 ? "-" : "+") + Math.floor(m / 60) + (m % 60 ? ":" + String(m % 60).padStart(2, "0") : "");
}
/* A moment as it is written everywhere in the tool: "Nov 18, 2026, 3:12 PM EST". */
export function timeLabel(ms, zone, seconds){
  if(!timeKnown(ms)) return "time not known";
  const p = zoneClock(ms, zone), two = n => String(n).padStart(2, "0");
  return `${TIME_MONTHS[p.month - 1]} ${p.day}, ${p.year}, ${(p.hour + 11) % 12 + 1}:${two(p.minute)}${seconds ? ":" + two(p.second) : ""} ${p.hour >= 12 ? "PM" : "AM"} ${zoneAbbr(ms, zone)}`;
}
/* A time of day alone, for a moment of today: "3:12 PM EST". */
export function timeOfDay(ms, zone, seconds){
  if(!timeKnown(ms)) return "time not known";
  const p = zoneClock(ms, zone), two = n => String(n).padStart(2, "0");
  return `${(p.hour + 11) % 12 + 1}:${two(p.minute)}${seconds ? ":" + two(p.second) : ""} ${p.hour >= 12 ? "PM" : "AM"} ${zoneAbbr(ms, zone)}`;
}
/* A reference file (Drugs@FDA) changes about monthly, so it has its own number of days: when it was saved, how long ago, and STALE once it
   is older than those days. The day is the day in the zone in use. */
export const REF_STALE_CHOICES = [14, 30, 45, 60, 90], REF_STALE_DEFAULT = 45;
export function refSaved(savedMs, nowMs, days, zone){
  const n = REF_STALE_CHOICES.includes(days) ? days : REF_STALE_DEFAULT;
  if(!timeKnown(savedMs) || !timeKnown(nowMs)) return { text:"saved: time not known", stale:false };
  const c = zoneClock(savedMs, zone), stale = nowMs - savedMs > n * 86400000;
  return { text:`saved ${TIME_MONTHS[c.month - 1]} ${c.day}, ${c.year} (${relTime(savedMs, nowMs)})` + (stale ? ` · STALE: older than ${n} days` : ""), stale };
}
/* What Settings says after the zone was chosen. A zone this computer does not know is said plainly: its own is used instead. */
export function zoneSaid(chosen, computer, nowMs){
  const own = `this computer's time zone (${String(computer).replace(/_/g, " ")}, ${zoneAbbr(nowMs, computer)})`, choice = TZ_CHOICES.find(c => c[0] === chosen && c[0]);
  if(!choice) return `Times are now written in ${own}. No stored time was changed.`;
  if(!zoneKnown(chosen)) return `This computer has no data for ${choice[1]} time, so times are written in ${own}. No stored time was changed.`;
  return `Times are now written in ${choice[1]} time (${zoneAbbr(nowMs, chosen)}). No stored time was changed.`;
}
/* How long ago, in whole units, never rounded up: 59 seconds is "just now", 119 minutes is "1 hour ago". */
export function relTime(thenMs, nowMs){
  if(!timeKnown(thenMs) || !timeKnown(nowMs)) return "time not known";
  const d = nowMs - thenMs, n = (count, unit) => `${count} ${unit}${count === 1 ? "" : "s"} ago`;
  if(d < -60000) return "later than now: please check this computer's clock";
  if(d < 60000) return "just now";
  const minutes = Math.floor(d / 60000); if(minutes < 60) return n(minutes, "minute");
  const hours = Math.floor(minutes / 60); if(hours < 24) return n(hours, "hour");
  return n(Math.floor(hours / 24), "day");
}
/* One line for a piece of data: where it is from, as of when, how long ago, and STALE once it is older than the days the person chose. */
export function freshLine(source, asOfMs, nowMs, staleDays, zone){
  const days = STALE_CHOICES.includes(staleDays) ? staleDays : STALE_DEFAULT;
  if(!timeKnown(asOfMs) || !timeKnown(nowMs)) return { text:`${source} · time not known`, stale:false };
  const stale = nowMs - asOfMs > days * 86400000;
  return { text:`${source} · as of ${timeLabel(asOfMs, zone, false)} (${relTime(asOfMs, nowMs)})` + (stale ? ` · STALE: older than ${days} ${days === 1 ? "day" : "days"}` : ""), stale };
}

/* The name of this computer's own zone, as the browser gives it. */
export function computerZone(){ try{ return new Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC"; }catch(e){ return "UTC"; } }
/* The zone in use: the one chosen where this computer knows it, the computer's own in every other case. */
export function zoneInUse(chosen, computer = computerZone()){ return TZ_CHOICES.some(c => c[0] === chosen && c[0]) && zoneKnown(chosen) ? chosen : computer; }
/* One line for a record's data, as the app's Clock.DataLine: sample data says that it does not change; a record without a moment keeps its old line. */
export function dataLine(r, nowMs, staleDays, zone){
  const asOf = typeof r.asOfUtc === "string" ? Date.parse(r.asOfUtc) : NaN;
  if(!r.dataSource || !Number.isFinite(asOf)) return { text: r.dataAsOf ?? "", stale: false };
  if(r.demo) return { text: `${r.dataSource} · as of ${timeLabel(asOf, zone, false)} (sample data, which does not change)`, stale: false };
  return freshLine(r.dataSource, asOf, nowMs, staleDays, zone);
}
