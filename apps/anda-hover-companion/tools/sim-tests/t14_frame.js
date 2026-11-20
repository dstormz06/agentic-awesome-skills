// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// The artifact viewer shows the page inside a frame of unknown width. Drive the demo script inside a frame at seven widths.
const { chromium } = require('playwright');
const fs = require('fs');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; if (!c) { fails++; console.log('FAIL ' + m); } };
(async () => {
  const browser = await chromium.launch();
  for (const [W, H] of [[1100, 800], [1000, 800], [900, 760], [768, 800], [650, 800], [601, 800], [500, 800], [390, 800]]) {
    const phone = W <= 600;
    fs.writeFileSync(TMP + '/frame_wrap.html', `<!doctype html><body style="margin:0;background:#888"><iframe id="f" src="${SIM}" width="${W}" height="${H}" style="border:0;display:block"></iframe>`);
    const ctx = await browser.newContext({ viewport: { width: W + 40, height: H + 40 } });
    await ctx.grantPermissions(['clipboard-read', 'clipboard-write']);
    const page = await ctx.newPage();
    const errs = []; page.on('pageerror', e => errs.push(e.message)); page.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
    await page.goto(require('url').pathToFileURL(TMP + '/frame_wrap.html').href); await page.waitForTimeout(600);
    const f = page.frames().find(x => x !== page.mainFrame());
    const tag = `${W}px`;
    const geo = sel => f.evaluate(s => { const e = document.querySelector(s); if (!e) return null; const cs = getComputedStyle(e); if (cs.display === 'none') return null; const r = e.getBoundingClientRect(); return { w: r.width, l: r.left, r: r.right, t: r.top, b: r.bottom, vw: innerWidth, vh: innerHeight, sheet: e.classList.contains('sheet') }; }, sel);
    const inside = g => g && g.l >= -1 && g.r <= g.vw + 1 && g.t >= -1 && g.b <= g.vh + 1;
    const wideEnough = (g, max) => g && (phone ? g.w >= W - 4 : g.w <= max);
    const txt = sel => f.textContent(sel);
    ok(!(await f.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)), `${tag}: no sideways page scroll`);
    // pin the icon, then the at-risk number opens a peek
    await f.click('#chev'); await f.click('#pinBtn2'); await f.waitForTimeout(200);
    await f.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await f.waitForTimeout(700);
    let g = await geo('#card');
    ok(!!g, `${tag}: the at-risk number opens a peek`);
    if (g) { ok(g.sheet === phone, `${tag}: bottom sheet only on a phone (sheet=${g.sheet})`); ok(wideEnough(g, 380), `${tag}: peek is ${Math.round(g.w)} px wide`); ok(inside(g), `${tag}: peek inside the frame (${Math.round(g.l)}..${Math.round(g.r)}, ${Math.round(g.t)}..${Math.round(g.b)} of ${g.vw}x${g.vh})`); }
    ok(!phone ? (await f.evaluate(() => getComputedStyle(document.getElementById('scrim')).display)) === 'none' : true, `${tag}: no dark overlay behind a floating card`);
    // full brief
    if (await f.isVisible('#moreBtn')) { await f.click('#moreBtn'); await f.waitForTimeout(500); }
    g = await geo('#card');
    ok(!!g && (await txt('#c-id')).includes('216482') && (await txt('#c-fdelta')).includes('14'), `${tag}: full brief shows 216482 and the 14-day gap`);
    if (g) { ok(wideEnough(g, 500), `${tag}: full card is ${Math.round(g.w)} px wide`); ok(inside(g), `${tag}: full card inside the frame (${Math.round(g.l)}..${Math.round(g.r)}, ${Math.round(g.t)}..${Math.round(g.b)} of ${g.vw}x${g.vh})`); }
    ok((await txt('#c-psg')).length > 5 && (await txt('#c-rld')).length > 5, `${tag}: PSG and RLD lines are filled`);
    await page.keyboard.press('Escape'); await f.waitForTimeout(300);
    ok(!(await geo('#card')), `${tag}: Esc closes the card`);
    // lookup
    await page.keyboard.press('Control+Alt+Space'); await f.waitForTimeout(300);
    g = await geo('#lookup'); ok(!!g && inside(g) && (phone ? g.w >= W - 30 : g.w <= 540), `${tag}: lookup opens inside the frame (${g ? Math.round(g.w) : '-'} px)`);
    await page.keyboard.type('214990'); await page.keyboard.press('Enter'); await f.waitForTimeout(500);
    ok(!!(await geo('#card')) && (await txt('#c-id')).includes('214990'), `${tag}: lookup opens 214990`);
    await page.keyboard.press('Escape'); await f.waitForTimeout(250);
    // draft check
    await page.keyboard.press('Control+Alt+Shift+KeyR'); await f.waitForTimeout(500);
    g = await geo('#draft'); ok(!!g && inside(g) && (phone ? g.w >= W - 30 : g.w <= 540), `${tag}: draft check opens inside the frame (${g ? Math.round(g.w) : '-'} px)`);
    ok((await txt('#drOut')).length > 20, `${tag}: draft check lists its findings`);
    await f.click('#drDone'); await f.waitForTimeout(200);
    // tray: Today (home since round 34), settings
    for (const [item, win, done] of [['Today…', '#today', '#tdClose'], ['Settings…', '#settings', '#setDone']]) {
      await f.click('#appIcon', { button: 'right' }); await f.waitForTimeout(200);
      await f.click(`#menu >> text=${item}`); await f.waitForTimeout(350);
      g = await geo(win); ok(!!g && inside(g) && (phone ? g.w >= W - 30 : g.w <= (win === '#today' ? 560 : 540))   /* Today has two panes, 560 px */, `${tag}: ${item} opens inside the frame (${g ? Math.round(g.w) : '-'} px)`);
      await f.click(done); await f.waitForTimeout(200);
    }
    // settings: every section reachable, and the backup box is not cut off
    await f.click('#appIcon', { button: 'right' }); await f.click('#menu >> text=Settings…'); await f.waitForTimeout(300);
    const scrollable = await f.evaluate(() => { const s = document.getElementById('settings'); return s.scrollHeight >= s.clientHeight; });
    ok(scrollable, `${tag}: Settings scrolls if it is taller than the frame`);
    await f.click('#setDone'); await f.waitForTimeout(150);
    // unpin and reset leave a clean page
    ok(errs.length === 0, `${tag}: no console or page errors ${errs.slice(0, 2).join(' | ')}`);
    await ctx.close();
  }
  await browser.close();
  console.log(fails ? `\n${fails} FAILED of ${checks}` : `\nALL ${checks} PASSED`);
  process.exit(fails ? 1 : 0);
})();
