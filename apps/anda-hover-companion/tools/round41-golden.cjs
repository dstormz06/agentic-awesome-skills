// Writes (WRITE=1) or checks mockup/round41-golden.json: the answers of the browser simulation for round 41: Elsa's request body and reply
// path typed in Settings, the token header check, and MCP servers that are programs on this PC (by full path and SHA-256). The Windows
// app's tests (Round41Tests) must give the same answers.
// Run from this folder:  NODE_PATH=$(npm root -g) node round41-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round41-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => {
    const T = '{"model":"{{model}}","agent":"{{agent}}","max_tokens":{{max_tokens}},"messages":[{"role":"system","content":"{{system}}"},{"role":"user","content":"{{user}}"}]}';
    const elsa = [
      { header: 'Authorization', scheme: 'Bearer', template: '', path: '' },
      { header: 'Authorization', scheme: 'Bearer', template: T, path: '' },
      { header: 'Authorization', scheme: 'Bearer', template: '', path: 'output.text' },
      { header: 'Authorization', scheme: 'Bearer', template: T, path: 'output.text' },
      { header: 'Authorization', scheme: 'Bearer', template: '{"q": {{user}}', path: 'output.text' },
      { header: 'Authorization', scheme: 'Bearer', template: T, path: 'output..text' },
      { header: 'Authorization', scheme: 'Bearer', template: T, path: 'choices[0].message.content' },
      { header: 'api-key', scheme: '', template: T, path: 'output.text' },
      { header: 'Host', scheme: 'Bearer', template: T, path: 'output.text' },
      { header: 'Authorization', scheme: 'Bear er', template: T, path: 'output.text' },
      { header: 'Content-Type', scheme: '', template: T, path: 'output.text' },
      { header: '', scheme: '', template: T, path: 'output.text' },
    ].map(e => ({ elsa: e, problem: elsaProblem(e), filled: elsaFilled(e) }));
    const headers = [['Authorization', 'Bearer'], ['x-api-key', ''], ['', 'Bearer'], ['Author ization', 'Bearer'], ['Authorization:', 'Bearer'], ['HOST', 'Bearer'],
      ['proxy-authorization', ''], ['Mcp-Session-Id', ''], ['Content-Length', ''], ['Cookie', ''], ['Authorization', 'Bearer x'], ['Authorization', 'Bearér']]
      .map(([header, scheme]) => ({ header, scheme, problem: authHeaderProblem(header, scheme) }));
    const sha = 'a'.repeat(64);
    const programs = [['', ''], ['C:\\Tools\\mcp.exe', ''], ['C:\\Tools\\mcp.exe', sha], ['"C:\\Program Files\\Example\\mcp-server.exe"', sha.toUpperCase()],
      ['C:\\Tools\\mcp.bat', sha], ['C:\\Tools\\run.ps1', sha], ['\\\\server\\share\\mcp.exe', sha], ['mcp.exe', sha], ['D:\\a\\b\\MCP.EXE', sha], ['C:\\Tools\\mcp.exe', sha.slice(1)],
      ['C:\\Tools\\mc|p.exe', sha], ['C:\\Tools\\mcp.exe', 'g'.repeat(64)],
      // round 41 review: an alternate stream, "." and "..", folders ending in a dot or a space, an empty folder, a forward slash
      ['C:\\Tools\\notes.txt:mcp.exe', sha], ['C:\\Tools:stream\\mcp.exe', sha], ['C:\\Tools\\..\\Windows\\mcp.exe', sha], ['C:\\.\\Tools\\mcp.exe', sha], ['C:\\Tools.\\mcp.exe', sha],
      ['C:\\Tools \\mcp.exe', sha], ['C:\\\\Tools\\mcp.exe', sha], ['C:/Tools/mcp.exe', sha], ['C:\\Tools\\v1.2\\mcp.exe', sha], ['C:\\mcp.exe', sha]]
      .map(([program, s]) => ({ program, sha: s, problem: mcpProgramProblem(program, s), name: mcpProgramName(program), path: mcpProgramPath(program) }));
    const input = [
      { name: 'Fictional local search', isProgram: true, program: ' "C:\\Program Files\\Example\\mcp-server.exe" ', sha: sha.toUpperCase(), args: ['--stdio', '  ', ' --quiet ', '', 'x'.repeat(600)], enabled: true, timeout: 60 },
      { name: 'Not yet approved', isProgram: true, program: '', sha: '', args: [], enabled: true, timeout: 30 },
      { name: 'A script', isProgram: true, program: 'C:\\Tools\\run.bat', sha, args: [], enabled: true, timeout: 30 },
      { name: 'Many arguments', isProgram: true, program: 'C:\\Tools\\mcp.exe', sha, args: Array.from({ length: 25 }, (_, i) => 'a' + i), enabled: false, timeout: 30 },
      { name: 'An https server', endpoint: 'https://mcp.example.test/mcp', header: 'Authorization', scheme: 'Bearer', env: '', enabled: true, timeout: 30 },
      { name: 'An address that cannot be used', endpoint: 'http://mcp.example.test/mcp', header: 'Authorization', scheme: 'Bearer', env: '', enabled: true, timeout: 30 },
    ];
    const normalized = mcpNormalize(input);
    return {
      about: 'Shared cases for round 41, written by the browser simulation. The Windows app must give the same answers.',
      elsaSaid: ELSA_SAID, elsa, headers,
      programSaid: { hint: MCP_SAID.programHint, starts: MCP_SAID.startsProgram('mcp-server.exe'), mcpHint: MCP_SAID.hint },
      programs, normalize: { input, output: normalized },
      where: normalized.map(m => mcpWhere(m)),
      phrases: EV_PHRASES.filter(x => x.startsWith('MCP program')),
      changes: CHANGES.find(c => c.round === 41), version: APP_VERSION,
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.elsa.length} Elsa settings, ${doc.headers.length} token headers, ${doc.programs.length} programs, ${doc.normalize.input.length} servers kept`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round41-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
