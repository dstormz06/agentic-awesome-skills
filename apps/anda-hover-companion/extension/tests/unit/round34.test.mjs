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
