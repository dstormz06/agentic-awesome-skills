// Draws the Loupe icon at every size the app needs, from loupe.svg and loupe-small.svg, and writes app.ico, tray-32.png, tray-32-alert.png (a dot) and tray-32-2.png to tray-32-9plus.png (a number).
// Run from this folder:  node make-icons.mjs   (needs Playwright to be installed where Node can find it, for example NODE_PATH=$(npm root -g); it is only used here, not by the app).
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
const { chromium } = createRequire(import.meta.url)('playwright');

const here = dirname(fileURLToPath(import.meta.url));
const master = readFileSync(join(here, 'loupe.svg'), 'utf8').replace(/<title>.*<\/title>/, '');
const small = readFileSync(join(here, 'loupe-small.svg'), 'utf8').replace(/<title>.*<\/title>/, '');
// Round 23: the Loupe carries no amber at any size. Amber means "something needs you today" and appears only on the tray picture:
// a dot for one thing, the number for two to nine, "9+" beyond. Windows draws tray pictures at 16 to 24 px, so the number badge is large.
const DOT = '<circle cx="51" cy="13" r="11" fill="#fff"/><circle cx="51" cy="13" r="8" fill="#f2a93b"/>';
const numberBadge = t => `<circle cx="44" cy="20" r="20" fill="#fff"/><circle cx="44" cy="20" r="17.5" fill="#f2a93b"/><text x="44" y="${t.length > 1 ? 28 : 30}" text-anchor="middle" font-family="Segoe UI,Arial,Liberation Sans,DejaVu Sans,sans-serif" font-weight="800" font-size="${t.length > 1 ? 22 : 28}" fill="#1d2329">${t}</text>`;
const withBadge = (svg, badge) => svg.replace('</svg>', badge + '</svg>');
const SIZES = [16, 24, 32, 48, 64, 128, 256];
const pick = px => (px <= 24 ? small : master);

const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 1 });
async function png(svg, px) {
  await page.setViewportSize({ width: px, height: px });
  await page.setContent(`<style>html,body{margin:0;background:transparent}svg{display:block;width:${px}px;height:${px}px}</style>${svg}`);
  return await page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: px, height: px } });
}
const frames = [];
for (const px of SIZES) frames.push([px, await png(pick(px), px)]);

// An .ico file is a small header, one directory entry per picture, then the pictures. Windows reads PNG pictures.
const header = Buffer.alloc(6); header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(frames.length, 4);
let offset = 6 + 16 * frames.length;
const entries = frames.map(([px, data]) => {
  const e = Buffer.alloc(16);
  e.writeUInt8(px >= 256 ? 0 : px, 0); e.writeUInt8(px >= 256 ? 0 : px, 1); e.writeUInt8(0, 2); e.writeUInt8(0, 3);
  e.writeUInt16LE(1, 4); e.writeUInt16LE(32, 6); e.writeUInt32LE(data.length, 8); e.writeUInt32LE(offset, 12);
  offset += data.length; return e;
});
writeFileSync(join(here, 'app.ico'), Buffer.concat([header, ...entries, ...frames.map(f => f[1])]));
writeFileSync(join(here, 'tray-32.png'), await png(pick(32), 32));
writeFileSync(join(here, 'tray-32-alert.png'), await png(withBadge(pick(32), DOT), 32));
for (const t of ['2', '3', '4', '5', '6', '7', '8', '9', '9+']) writeFileSync(join(here, `tray-32-${t === '9+' ? '9plus' : t}.png`), await png(withBadge(pick(32), numberBadge(t)), 32));
// the browser extension's four sizes
const ext = join(here, '..', '..', '..', 'extension', 'icons');
for (const px of [16, 32, 48, 128]) writeFileSync(join(ext, `icon${px}.png`), await png(pick(px), px));
await browser.close();
console.log('wrote app.ico (' + SIZES.join(', ') + '), tray-32.png, tray-32-alert.png, tray-32-2 to 9 and 9plus .png and extension/icons/icon{16,32,48,128}.png');
