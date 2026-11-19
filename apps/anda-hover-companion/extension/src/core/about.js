// What the extension is linked to, said in one line wherever its windows open, and what changed in each version.
// The extension makes no network requests of its own. The one link it can have is to the desktop app on the same computer.

/**
 * @param dataSource 'demo' | 'imported' | 'app'  @param status the answer of the service's status() when the source is the app, else null
 * @returns {short, full, state}: state is 'local', 'linked' or 'silent' (the app is chosen and does not answer).
 */
export function connectionLine(dataSource, status) {
  if (dataSource !== 'app')
    return { state: 'local', short: 'Offline · Local only', full: 'Offline · Local only. The extension makes no network requests and is not linked to any other program. Your data stays in this browser profile on this PC.' };
  if (status && status.ok)
    return { state: 'linked', short: 'Linked: the desktop app on this PC · no network use',
      full: 'Linked: ANDA Companion (Notula), the desktop app on this PC. Sent to it: the ANDA number or the draft text you ask about. Where it goes: the app on this computer only. Kept by the extension: nothing. The extension makes no network requests; what the app has switched on is shown in the app.' };
  return { state: 'silent', short: 'Desktop app chosen, not answering · nothing is sent',
    full: 'The desktop app on this PC is chosen as the source, and it is not answering, so nothing is sent to it and no card can be shown. The extension makes no network requests.' };
}

/** What changed, newest first. The version itself is read from the manifest, so the two cannot disagree about which one is installed. */
export const CHANGES = [
  { version: '0.2.0', title: 'Round 33', items: [
    'Every card says where its data is from, as of when and how long ago, and marks it STALE after the number of days you choose. The line is written again as each minute begins.',
    'Times carry their time zone (for example "Nov 18, 2026, 3:12 PM EST"). The zone is this computer\'s own unless you choose another under Time.',
    'The time your spreadsheet was saved is kept as one moment and written in the zone in use.',
    'The Settings page and the toolbar window say what is switched on: "Offline · Local only", or the link to the desktop app by name.',
    'First steps: a guide of five steps, shown once after installing and again from "Help: first steps".',
    'Settings carry the number of their form, so a later version can read them.',
  ] },
  { version: '0.1.0', title: 'First version', items: [
    'The card for a selected ANDA number, the draft check, the watch list and the page scan, from fictional demo records, your own spreadsheet or the desktop app.',
  ] },
];
