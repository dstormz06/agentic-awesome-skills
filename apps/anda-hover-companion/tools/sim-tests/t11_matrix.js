// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
const LAB = SIMURL + '/mockup/appearance-lab.html';
const OPT = process.env.OPTIONS_PAGE || '';      // an older proposals page that is not in the repository: checked only when its path is given
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; if (!c) { fails++; console.log('FAIL ' + m); } };
const LOOKS = 'xvutkplo'.split('');   // the eight looks offered since round 23
const VIEWPORTS = [[1920, 1080], [1366, 768], [1280, 720], [1024, 768], [768, 1024], [390, 844], [360, 640]];
(async () => {
  const browser = await chromium.launch();
  for (const [w, h] of VIEWPORTS) {
    const ctx = await browser.newContext({ viewport: { width: w, height: h } });
    const page = await ctx.newPage();
    const errs = []; page.on('pageerror', e => errs.push(e.message)); page.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
    await page.goto(URL); await page.waitForTimeout(250);
    const over = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
    ok(!over, `${w}x${h}: no horizontal page scroll`);
    for (const look of LOOKS) {
      for (const id of ['216482', '214990']) {
        await page.evaluate(([l, n]) => { applyLook(l); const compact = !(LOOKS.find(x => x[0] === l)[3] === 'full'); openCard(n, { anchor: { x: Math.round(innerWidth * .45), y: 60 }, forceFull: !compact }); }, [look, id]);
        await page.waitForTimeout(650);
        const info = await page.evaluate(() => {
          const c = document.getElementById('card'), k = document.getElementById('compact');
          const el = c && getComputedStyle(c).display !== 'none' ? c : (k && getComputedStyle(k).display !== 'none' ? k : null);
          if (!el) return null;
          const r = el.getBoundingClientRect();
          return { l: r.left, t: r.top, r: r.right, b: r.bottom, w: r.width, h: r.height, vw: innerWidth, vh: innerHeight, over: el.scrollWidth > el.clientWidth + 1 };
        });
        ok(info, `${w}x${h} look ${look} ${id}: a card or box is showing`);
        if (info) {
          ok(info.l >= -1 && info.r <= info.vw + 1, `${w}x${h} look ${look} ${id}: inside the window horizontally (${Math.round(info.l)}..${Math.round(info.r)} of ${info.vw})`);
          ok(info.t >= -1 && info.b <= info.vh + 1, `${w}x${h} look ${look} ${id}: inside the window vertically (${Math.round(info.t)}..${Math.round(info.b)} of ${info.vh})`);
          ok(!info.over, `${w}x${h} look ${look} ${id}: no inner horizontal scroll`);
        }
        await page.evaluate(() => { try { closeCard(); } catch (e) {} try { closeCompact(); } catch (e) {} });
      }
    }
    ok(errs.length === 0, `${w}x${h}: no page errors: ${errs.slice(0, 2).join(' | ')}`);
    await ctx.close();
  }
  // the lab and the options page at three widths
  for (const [name, url] of [['lab', LAB], ...(OPT ? [['options', require('url').pathToFileURL(OPT).href]] : [])]) {
    for (const w of [1440, 768, 360]) {
      const p = await browser.newPage({ viewport: { width: w, height: 900 } });
      const errs = []; p.on('pageerror', e => errs.push(e.message));
      await p.goto(url);
      ok(!(await p.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)), `${name} at ${w}: no horizontal scroll`);
      ok(errs.length === 0, `${name} at ${w}: no errors ${errs.join('|')}`);
      await p.close();
    }
  }
  await browser.close();
  console.log(fails ? `\n${fails} FAILED of ${checks}` : `\nALL ${checks} PASSED`);
  process.exit(fails ? 1 : 0);
})();
