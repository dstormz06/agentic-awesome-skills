// First steps: the extension's guide of five steps. It is shown once after the extension is installed and again from "Help: first steps".
// Each line is a plain statement about what the extension does; tests/unit/guide.test.mjs checks the ones that can be checked against the code.

export const SAMPLE_NUMBER = '216482';

export const GUIDE_STEPS = [
  { title: 'What this is', action: null, lines: [
    'ANDA Companion (Notula) for Edge shows a short card for an ANDA number you select on a web page: the goal date, the forecast, and what is worth a second look.',
    'It starts with two fictional ANDAs, 216482 and 214990. They are sample data, and every card made from them is marked FICTIONAL DEMO.',
    'The extension makes no network requests, and it reads a page only when you ask it to.',
  ] },
  { title: 'Try it on the sample number', action: { id: 'sample', label: 'Show the sample card here' }, lines: [
    'On a web page, select the number 216482, then {start}. Or press the button below to see the card of the sample ANDA 216482 on this page.',
    'On a card on a web page, E shows more, S copies the status line and Esc closes the card.',
  ] },
  { title: 'Your own data', action: { id: 'file', label: 'Go to “My spreadsheet”' }, lines: [
    'Under “My spreadsheet” on this page, choose your portfolio file (Excel or CSV).',
    'The file is read here in the browser and its rows are kept in this browser profile on this PC. It is never uploaded.',
    'If the desktop app is installed on this computer, you can use it as the source instead.',
  ] },
  { title: 'What is switched on', action: null, lines: [
    'The top of this page and the foot of the toolbar window say what is switched on. At the moment: {connections}',
    'The extension has no AI. The one link it can have is to the desktop app on this computer, and only after you allow it.',
  ] },
  { title: 'Where things are', action: null, lines: [
    'The toolbar button opens Look up, Draft, Watch list and This page.',
    'A right-click on selected text offers the same card. The shortcuts are listed under “Ways to start it” on this page.',
    'This guide opens again from “Help: first steps” on this page and in the toolbar window.',
  ] },
];

/** How a card is started on a page: the key Edge really gave the command, or the right-click menu when no key is set. */
export function guideStart(key) {
  const k = typeof key === 'string' ? key.trim() : '';
  return k.length > 0 ? `press ${k}` : 'right-click it and choose “ANDA Companion (Notula): peek at…” (no shortcut is set at the moment)';
}

/**
 * One step as it is shown. The place is kept inside the guide: a number that is not a step shows the first or the last one.
 * The two places a line can hold are filled in one pass, so that a filled-in text is never read again for a place.
 */
export function guideStep(i, start, connections) {
  const n = GUIDE_STEPS.length, at = Number.isFinite(i) ? Math.min(n - 1, Math.max(0, Math.trunc(i))) : 0, s = GUIDE_STEPS[at];
  const fill = (line) => line.replace(/\{(start|connections)\}/g, (_, name) => (name === 'start' ? String(start ?? '') : String(connections ?? '')));
  return { at, count: n, place: `Step ${at + 1} of ${n}`, title: s.title, lines: s.lines.map(fill), action: s.action,
    back: at > 0, next: at < n - 1 ? 'Next' : 'Done' };
}

/** The guide opens by itself only until it has been shown once. */
export const guideOpensAtStart = (seen) => seen !== true;

/** What the guide says when the browser would not keep that it was shown. */
export const GUIDE_NOT_REMEMBERED = 'The browser did not keep that this guide was shown, so it may open again the next time.';
