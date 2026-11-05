// Usage: SEED=1 N=5000 node gen.js notes.json   then run the app side (see README) and: node compare.js notes.json app-output.json
// Builds seeded random notes from realistic fragments (and some hostile ones), for the app-versus-simulation differential check.
const fs = require('fs');
let seed = +(process.env.SEED || 20261104); const rnd = () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296;
const pick = a => a[Math.floor(rnd() * a.length)];
const lead = ['', '', 'Don\'t forget to ', 'Need to ', 'remember to ', 'please ', 'Also ', 'and then ', 'TODO: ', '- ', '* ', '1. ', '2) ', '[ ] ', 'a) ', 'A) ', '• ', 'Thanks, ', 'Hi Sam, ', 'FYI ', 'Action item: '];
const verb = ['call', 'email', 'send', 'review', 'submit', 'pay', 'follow-up', 'fix', 'schedule', 'book', 'clean', 'route', 'order', 'check', 'update', 'file', 'renew', 'pick up', 'ask', 'confirm', 'approve', 'triage', 'assess', 'the inspection report was', 'it is', 'meeting with', 'Dr. Lee said'];
const obj = ['the applicant', 'the team lead', 'the IR response', 'the memo', 'the expense report', 'the annual training', 'the dentist', 'the vehicle registration', 'the status report', 'ANDA 216482', 'NDA 021234', '1/2 inch of margin', 'the huddle', 'the facility inspection date', 'the information request', 'Pat and Lee', 'the tracker, the memo, and the labeling', 'a very long thing '.repeat(12), 'ANDA 214990 and ANDA 216482'];
const when = ['', '', ' tomorrow', ' today', ' by Friday', ' on Monday', ' next week', ' this weekend', ' in 3 days', ' in 2 weeks', ' on Nov 12', ' 11/20', ' 12/3/27', ' 2026-12-24', ' the 1st', ' on the 15th', ' every day', ' every Friday', ' every weekday', ' weekly', ' monthly on the 1st', ' every month', ' by eom', ' eow', ' end of next month', ' by Sat', ' due Tues', ' on Feb 30', ' in 99 days', ' at 5pm', ' Thursdays', ' Mon-Fri', ' by Dec 31, 2026', ' next Friday', ' day after tomorrow', ' 31st of Jan', ' on 13/45'];
const tail = ['', '', '.', '!', '!!', '?', ' asap', ' urgent!', ' (!)', ' ...', ';', ', thanks', '. Thanks.', ' - important'];
const junk = ["٣", "１２", "𝟐", "٢٠٢٦-١١-٠٤", '', '', '', '\n', '\n\n', ' ', '\t', '\u0000', ' ‮', '😀', '<b>x</b>', '"quoted"', '(paren)', 'ß', 'é', '—'];
const item = () => pick(junk) + pick(lead) + pick(verb) + ' ' + pick(obj) + pick(when) + pick(tail);
const out = [];
for (let i = 0; i < +(process.env.N || 2500); i++) {
  const n = 1 + Math.floor(rnd() * 5), parts = [];
  for (let k = 0; k < n; k++) parts.push(item());
  out.push(parts.join(pick([' ', ' ', '\n', ' Also ', '. ', '; ', ' and '])));
}
// plus inputs longer than the reading limit, so the cut-off rule is compared too
out.push('Submit the report. '.repeat(400), 'call Sam '.repeat(1000), '- email the team lead ' + 'x'.repeat(10), Array.from({ length: 400 }, (_, i) => '- email the team lead ' + i).join('\n'), 'word'.repeat(2500), ('Call Sam today' + '\r\n').repeat(500), ('a. b! c? d; ').repeat(700) + 'call Sam');
// plus short edge inputs
out.push('', ' ', '.', '!!', '1/1', 'call', 'a b', 'The 5th', 'in 0 days call Sam', '2026-13-45 call Sam', 'Feb 29 call Sam', 'Feb 29 2028 call Sam', '12/31 call Sam', '1/1 call Sam');
fs.writeFileSync(process.argv[2], JSON.stringify(out));
console.log(out.length);
