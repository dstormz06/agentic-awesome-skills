import { test } from 'node:test';
import assert from 'node:assert/strict';
import { findNumbers } from '../../src/core/recognizer.js';
import { parseXml } from '../../src/core/xml.js';
import { readXlsx, parseCsv, readTable, PortfolioError, MAX_ROWS } from '../../src/core/portfolio.js';
import { normalizeSettings, DEFAULT_SETTINGS } from '../../src/core/settings.js';
import { firstStep } from '../../src/core/watch.js';

// a minimal zip writer for crafting hostile files in tests: entries are {name, data(Uint8Array), declared?: number, deflate?: boolean}
async function zip(entries) {
  const enc = new TextEncoder(), parts = [], central = [];
  let offset = 0;
  const u16 = (n) => [n & 255, (n >> 8) & 255], u32 = (n) => [n & 255, (n >> 8) & 255, (n >> 16) & 255, (n >>> 24) & 255];
  for (const e of entries) {
    let body = e.data, method = 0;
    if (e.deflate) {
      const cs = new Blob([e.data]).stream().pipeThrough(new CompressionStream('deflate-raw'));
      body = new Uint8Array(await new Response(cs).arrayBuffer()); method = 8;
    }
    const name = enc.encode(e.name), usize = e.declared ?? e.data.length;
    const local = Uint8Array.from([...u32(0x04034b50), ...u16(20), ...u16(0), ...u16(method), ...u16(0), ...u16(0), ...u32(0), ...u32(body.length), ...u32(usize), ...u16(name.length), ...u16(0), ...name]);
    parts.push(local, body);
    central.push(Uint8Array.from([...u32(0x02014b50), ...u16(20), ...u16(20), ...u16(0), ...u16(method), ...u16(0), ...u16(0), ...u32(0), ...u32(body.length), ...u32(usize), ...u16(name.length), ...u16(0), ...u16(0), ...u16(0), ...u16(0), ...u32(0), ...u32(offset), ...name]));
    offset += local.length + body.length;
  }
  const dirSize = central.reduce((n, c) => n + c.length, 0);
  const end = Uint8Array.from([...u32(0x06054b50), ...u16(0), ...u16(0), ...u16(entries.length), ...u16(entries.length), ...u32(dirSize), ...u32(offset), ...u16(0)]);
  const all = [...parts, ...central, end];
  const out = new Uint8Array(all.reduce((n, p) => n + p.length, 0));
  let o = 0; for (const p of all) { out.set(p, o); o += p.length; }
  return out;
}
const T = (s) => new TextEncoder().encode(s);
const WB = '<workbook xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="S" sheetId="1" r:id="rId1"/></sheets></workbook>';
const RELS = '<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>';
const SHEET = '<worksheet><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>ANDA</t></is></c></row><row r="2"><c r="A2" t="inlineStr"><is><t>123456</t></is></c></row></sheetData></worksheet>';
const good = () => [{ name: 'xl/workbook.xml', data: T(WB) }, { name: 'xl/_rels/workbook.xml.rels', data: T(RELS) }, { name: 'xl/worksheets/sheet1.xml', data: T(SHEET) }];

test('a small hand-built workbook reads (the test helper itself works)', async () => {
  const t = await readXlsx(await zip(good()));
  assert.deepEqual(t.headers, ['ANDA']);
  assert.deepEqual(t.rows, [['123456']]);
});

test('a document type declaration is refused, so no entity or external file is ever loaded', async () => {
  const bad = good(); bad[2] = { name: 'xl/worksheets/sheet1.xml', data: T('<!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]>' + SHEET) };
  await assert.rejects(readXlsx(await zip(bad)), (e) => e instanceof PortfolioError && /could not be read/.test(e.message));
});

test('a zip bomb is stopped: an entry that expands beyond what it declares is refused', async () => {
  const bomb = good(); bomb[2] = { name: 'xl/worksheets/sheet1.xml', data: new Uint8Array(5 * 1024 * 1024).fill(32), declared: 1000, deflate: true };
  await assert.rejects(readXlsx(await zip(bomb)), PortfolioError);
});

test('a workbook whose parts add up to an unreasonable size is not opened', async () => {
  const big = good(); big.push({ name: 'xl/junk.bin', data: T('x'), declared: 200 * 1024 * 1024 });
  await assert.rejects(readXlsx(await zip(big)), (e) => /unreasonable size/.test(e.message));
});

test('files that are not workbooks, are empty, or are the wrong type fail with a plain message', async () => {
  await assert.rejects(readXlsx(T('this is not a zip file at all, just text')), (e) => e instanceof PortfolioError && /could not be read/.test(e.message));
  await assert.rejects(readXlsx(await zip([{ name: 'readme.txt', data: T('x') }])), (e) => /does not look like an Excel workbook/.test(e.message));
  await assert.rejects(readTable(T('x'), 'notes.docx'), (e) => /not supported/.test(e.message));
  assert.throws(() => parseCsv('', 'a.csv'), (e) => /empty/.test(e.message));
});

test('a hidden sheet is never read', async () => {
  const wb = '<workbook xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Secret" sheetId="1" state="hidden" r:id="rId1"/></sheets></workbook>';
  await assert.rejects(readXlsx(await zip([{ name: 'xl/workbook.xml', data: T(wb) }, { name: 'xl/_rels/workbook.xml.rels', data: T(RELS) }, { name: 'xl/worksheets/sheet1.xml', data: T(SHEET) }])), (e) => /no visible sheets/.test(e.message));
});

test('more than 20,000 rows is refused rather than loaded', () => {
  const csv = 'ANDA,Goal\n' + Array.from({ length: MAX_ROWS + 5 }, (_, i) => `${100000 + i},2026-12-01`).join('\n');
  assert.throws(() => parseCsv(csv, 'big.csv'), (e) => e instanceof PortfolioError && /20,000 rows/.test(e.message));
});

test('the XML reader decodes entities and CDATA and ignores comments and processing instructions', () => {
  const seen = [];
  parseXml('<?xml version="1.0"?><a x="1 &amp; 2"><!-- c --><b>&lt;t&gt; &#65; &#x42;</b><c><![CDATA[<raw>]]></c><d/></a>', {
    open: (n, a, sc) => seen.push(['open', n, a.x ?? null, sc]), text: (t) => seen.push(['text', t]), close: (n) => seen.push(['close', n]),
  });
  assert.deepEqual(seen.filter((x) => x[0] === 'text').map((x) => x[1]), ['<t> A B', '<raw>']);
  assert.equal(seen.find((x) => x[1] === 'a')[2], '1 & 2');
});

test('scanning a page: numbers are distinct, in order, never an NDA, and capped', () => {
  assert.deepEqual(findNumbers('ANDA 216482 and again 216482, then 214990; NDA 021234 is another application; 12345 is too short'), ['216482', '214990']);
  assert.equal(findNumbers(Array.from({ length: 500 }, (_, i) => String(100000 + i)).join(' '), 50).length, 50);
});

test('settings fall back safely and keep their meaning', () => {
  assert.deepEqual(normalizeSettings({}), DEFAULT_SETTINGS);
  const s = normalizeSettings({ consent: 'nonsense', urgentLateDays: 9, holidayLeadDays: 4, autoHideSeconds: 15, dataSource: 'cloud', extraClosures: [{ date: '2026-12-24', name: 'EO' }, { date: 'bad' }, null] });
  assert.equal(s.consent, 'whisper'); assert.equal(s.urgentLateDays, 7); assert.equal(s.holidayLeadDays, 3); assert.equal(s.autoHideSeconds, 20); assert.equal(s.dataSource, 'demo');
  assert.deepEqual(s.extraClosures, [{ date: '2026-12-24', name: 'EO' }]);
});

test('what a request does first follows the consent model and the urgent rule', () => {
  assert.equal(firstStep('whisper', true, true), 'peek');
  assert.equal(firstStep('whisper', true, false), 'invite');
  assert.equal(firstStep('whisper', false, true), 'invite');
  assert.equal(firstStep('peek', true, false), 'peek');
  assert.equal(firstStep('full', true, true), 'full');
});
