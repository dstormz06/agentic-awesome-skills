// The same rule as the app's Data/EntityKinds.cs: six digits, optionally prefixed "ANDA", but never a number that is labelled as
// another kind of application (NDA, BLA, IND, DMF, MF, NDC). Showing the wrong application's card is worse than showing none.
const ANDA = /(?:ANDA[-\s#]?)?(?<!\d)(?<!\b(?:NDA|BLA|IND|DMF|NDC|MF)(?:[-\s#:.]|No\b|Number\b)*)(\d{6})(?!\d)/gi;

export function extractNumber(text) {
  if (!text || !String(text).trim()) return null;
  ANDA.lastIndex = 0;
  const m = ANDA.exec(String(text));
  return m ? m[1] : null;
}

/** Every distinct number in a text, in order of appearance, capped. Used by "scan this page". */
export function findNumbers(text, cap = 200) {
  const out = [];
  if (!text) return out;
  ANDA.lastIndex = 0;
  let m;
  while ((m = ANDA.exec(String(text))) && out.length < cap) if (!out.includes(m[1])) out.push(m[1]);
  return out;
}
