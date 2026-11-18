// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const OUT = process.argv[2] || TMP;
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (['error','warning'].includes(m.type())) errors.push(m.type()+': '+m.text()); });
  page.on('pageerror', e => errors.push('pageerror: '+e.message));
  await page.goto(URL);
  await page.waitForTimeout(300);

  // 1. recognizer equivalence with the C# xUnit table
  const table = [
    ["216482","216482"],["ANDA 216482","216482"],["anda-216482","216482"],["ANDA#216482","216482"],
    ["see ANDA 214990, then the labeling","214990"],["(216482)","216482"],["Ref: 216482.","216482"],["a\n216482\nb","216482"],
    ["12345",null],["1234567",null],["21648a",null],["abc",null],["",null],["   ",null],["9216482",null],["2164829",null],["x216482y","216482"],
    ["NDA 021234",null],["NDA021234",null],["NDA: 021234",null],["NDA No. 021234",null],["nda #021234",null],["DMF 012345",null],["IND 123456",null],["BLA 125000",null],["MF-012345",null],["ANDA/NDA 216482",null],
    ["NDA 021234 and ANDA 216482","216482"],["ANDA 021234","021234"],["IND 123456; 214990","214990"]
  ];
  const bad = await page.evaluate(t => t.filter(([x,e]) => extractNumber(x) !== e).map(([x,e]) => [x, extractNumber(x), e]), table);
  console.log('recognizer mismatches vs C# table:', JSON.stringify(bad));

  // 2. status line + offline brief equal the C# test expectations
  const sl = await page.evaluate(() => [statusLine('216482'), statusLine('214990'), offlineBrief('216482'), offlineBrief('214990')]);
  console.log(sl.join('\n'));

  // 3. placement port vs the C# PlacementTests
  const pl = await page.evaluate(() => {
    const A = {x:0,y:0,r:1920,b:1040};
    return [placeNear(100,100,400,500,A), placeNear(1800,100,400,500,A), placeNear(100,900,400,500,A), placeNear(1919,1039,492,760,A)];
  });
  console.log('placement', JSON.stringify(pl));

  await page.screenshot({ path: OUT + '/sim-desktop-initial.png' });
  console.log('errors:', JSON.stringify(errors));
  await browser.close();
})();
