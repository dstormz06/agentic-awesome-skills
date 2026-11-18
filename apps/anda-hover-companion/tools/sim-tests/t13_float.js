// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
(async () => {
  const b = await chromium.launch();
  for (const [w, h, coarse] of [[1366,768,false],[1040,700,false],[1000,700,false],[820,1180,true],[768,1024,false],[700,800,false],[601,800,false],[600,800,false],[390,844,true],[360,640,true]]) {
    const ctx = await b.newContext({ viewport: { width: w, height: h }, hasTouch: coarse, isMobile: coarse && w < 500 });
    const p = await ctx.newPage();
    const errs = []; p.on('pageerror', e => errs.push(e.message));
    await p.goto(URL); await p.waitForTimeout(250);
    const phone = w <= 600;
    for (const look of ['k', 'x', 'u', 'p']) {
      await p.evaluate(l => { applyLook(l); openCard('216482', { anchor: { x: Math.round(innerWidth * .4), y: 90 }, forceFull: true }); }, look);
      await p.waitForTimeout(650);
      const i = await p.evaluate(() => { const c = document.getElementById('card'), r = c.getBoundingClientRect(); return { w: r.width, l: r.left, r: r.right, t: r.top, b: r.bottom, sheet: c.classList.contains('sheet'), vw: innerWidth, vh: innerHeight, scrim: getComputedStyle(document.getElementById('scrim')).display }; });
      ok(i.sheet === phone, `${w}px look ${look}: sheet=${i.sheet} (expected ${phone})`);
      if (!phone) { ok(i.w <= 380, `${w}px look ${look}: floating card is ${Math.round(i.w)} px wide, not full width`); ok(i.scrim === 'none', `${w}px look ${look}: no dark scrim`); }
      ok(i.l >= -1 && i.r <= i.vw + 1 && i.t >= -1 && i.b <= i.vh + 1, `${w}px look ${look}: card inside the window (${Math.round(i.l)}..${Math.round(i.r)} x ${Math.round(i.t)}..${Math.round(i.b)} of ${i.vw}x${i.vh})`);
      await p.evaluate(() => closeCard());
    }
    // in-app windows
    for (const id of ['settings', 'lookup', 'draft', 'wlist', 'plib', 'replies']) {
      await p.evaluate(i => { document.querySelectorAll('.app-win').forEach(e => e.style.display = 'none'); const e = document.getElementById(i); e.style.display = 'block'; }, id);
      await p.waitForTimeout(80);
      const r = await p.evaluate(i => { const e = document.getElementById(i), r = e.getBoundingClientRect(); return { w: r.width, l: r.left, r: r.right, t: r.top, b: r.bottom, vw: innerWidth, vh: innerHeight }; }, id);
      ok(r.l >= -1 && r.r <= r.vw + 1, `${w}px #${id}: inside horizontally (${Math.round(r.l)}..${Math.round(r.r)} of ${r.vw}, ${Math.round(r.w)} wide)`);
      if (!phone && w > 700) ok(r.w <= 540, `${w}px #${id}: ${Math.round(r.w)} px wide, not full width`);
      if (phone) ok(r.w >= w - 30, `${w}px #${id}: spans the phone screen`);
      await p.evaluate(i => { document.getElementById(i).style.display = 'none'; }, id);
    }
    ok(errs.length === 0, `${w}px: no page errors ${errs.join('|')}`);
    await ctx.close();
  }
  await b.close();
  console.log(fails ? `\n${fails} FAILED of ${checks}` : `\nALL ${checks} PASSED`);
  process.exit(fails ? 1 : 0);
})();
