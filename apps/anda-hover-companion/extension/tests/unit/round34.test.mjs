// Round 34 in the extension: the person's own shortcut (or the menu item) opens the card directly. Whisper stays a choice. The rule is held
// to the answers the browser simulation wrote in mockup/round34-golden.json, the same file the Windows app is tested against. The extension
// has no middle-click, so only the cases started by a shortcut apply to it.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { normalizeSettings, DEFAULT_SETTINGS, SETTINGS_SCHEMA } from '../../src/core/settings.js';
import { firstStep } from '../../src/core/watch.js';

const here = dirname(fileURLToPath(import.meta.url));
const GOLD = JSON.parse(readFileSync(join(here, '..', '..', '..', 'mockup', 'round34-golden.json'), 'utf8'));

test('the usual way to start is the one the simulation and the app use: Full', () => {
  assert.equal(GOLD.defaultConsent, 'full');
  assert.equal(DEFAULT_SETTINGS.consent, GOLD.defaultConsent);
  assert.equal(normalizeSettings({}).consent, GOLD.defaultConsent);
});

test('what a shortcut does first matches the shared answers, for every way to start, urgent or not', () => {
  const cases = GOLD.steps.filter((s) => s.via === 'shortcut');
  assert.equal(cases.length, 12);
  for (const s of cases) assert.equal(firstStep(s.consent, s.escalate, s.urgent), s.step, JSON.stringify(s));
});

test('settings stored before round 34 move from Whisper to Full once; a Whisper chosen since is kept', () => {
  assert.equal(SETTINGS_SCHEMA, 3);
  assert.equal(normalizeSettings({ consent: 'whisper' }).consent, 'full', 'no form number: stored before round 33');
  assert.equal(normalizeSettings({ schema: 2, consent: 'whisper' }).consent, 'full', 'form 2: stored in round 33');
  const chosen = normalizeSettings({ schema: 3, consent: 'whisper' });
  assert.equal(chosen.consent, 'whisper', 'form 3: chosen after the change, so it is the person\'s choice');
  assert.deepEqual(normalizeSettings(chosen), chosen, 'reading it again changes nothing');
  for (const kept of ['peek', 'full']) assert.equal(normalizeSettings({ schema: 2, consent: kept }).consent, kept, `${kept} is never changed`);
  assert.equal(normalizeSettings({ schema: 2, consent: 'whisper' }).schema, 3, 'the moved settings carry the new form, so the move happens once');
});

test('the extension says once that a stored Whisper was moved, and saves the move so it is not said again', async () => {
  const { createService } = await import('../../src/service.js');
  const { consentMoves, CONSENT_MOVED } = await import('../../src/core/settings.js');
  const m = new Map([['settings', { schema: 2, consent: 'whisper', autoHideSeconds: 40 }]]);
  const storage = { get: async (k) => m.get(k), set: async (k, v) => { m.set(k, v); } };
  const svc = createService({ storage });
  assert.equal(await svc.consentMoved(), true);
  assert.deepEqual([m.get('settings').consent, m.get('settings').schema, m.get('settings').autoHideSeconds], ['full', 3, 40]);
  assert.equal(await svc.consentMoved(), false, 'said once');
  await svc.saveSettings({ consent: 'whisper' });
  assert.equal((await svc.getSettings()).consent, 'whisper', 'a Whisper chosen after the move is kept');
  assert.equal(await svc.consentMoved(), false);
  for (const [stored, want] of [[undefined, false], [{}, false], [{ consent: 'whisper' }, true], [{ schema: 3, consent: 'whisper' }, false], [{ schema: 2, consent: 'peek' }, false]]) assert.equal(consentMoves(stored), want, JSON.stringify(stored));
  assert.match(CONSENT_MOVED, /^Your shortcut now opens the card directly\. To see the small invite first again, choose Whisper invite below\.$/);
});
