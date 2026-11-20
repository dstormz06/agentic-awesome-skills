// Writes (WRITE=1) or checks mockup/round40-golden.json: the answers of the browser simulation for round 40 (MCP servers and My API
// presets): the sentences, the arguments offered for a tool, the arguments a person writes, the addresses, the list of servers as kept,
// the presets, and what the Connections window and the event log say with MCP servers on. The Windows app's tests (Round40Tests) must
// give the same answers.
// Run from this folder:  NODE_PATH=$(npm root -g) node round40-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round40-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => {
    const tools = [
      { name: 'search', inputSchema: { type: 'object', properties: { query: { type: 'string' }, limit: { type: 'integer' }, exact: { type: 'boolean' } }, required: ['query'] } },
      { name: 'second_text_required', inputSchema: { type: 'object', properties: { note: { type: 'string' }, number: { type: 'string' } }, required: ['number'] } },
      { name: 'every_type', inputSchema: { type: 'object', properties: { a: { type: 'number' }, b: { type: 'array' }, c: { type: 'object' }, d: { type: ['string', 'null'] }, e: {}, f: { type: 'null' } } } },
      { name: 'no_schema', inputSchema: {} },
      { name: 'no_text', inputSchema: { type: 'object', properties: { n: { type: 'integer' } } } },
    ];
    const selections = ['', '  Metformin ER, cycle 2  ', 'é "quoted" \\ back\nline'];
    const argumentsFor = [];
    for (const t of tools) for (const sel of selections) argumentsFor.push({ tool: t, selection: sel, result: mcpArgumentsFor(t, sel) });
    const parse = ['', '   ', '{}', '{"query":"x"}', '[1,2]', '"text"', '42', '{bad json', ' {"a":{"b":[1,2,3]}} ', 'x'.repeat(20001)]
      .map(text => { const r = mcpParseArgs(text); return { text: text.length > 100 ? `x*${text.length}` : text, args: r.args, problem: r.problem }; });
    const addresses = ['', '   ', 'https://mcp.example.test/mcp', 'http://mcp.example.test/mcp', 'http://localhost:8080/mcp', 'http://127.0.0.1/mcp', 'https://user:pw@mcp.example.test/', 'ftp://mcp.example.test', 'not an address', 'https://xn--exmple-4nf.test/mcp']
      .map(endpoint => ({ endpoint, problem: mcpAddressProblem(endpoint) }));
    const long = 'A'.repeat(39);
    const normalize = [
      { name: 'empty', input: [], output: mcpNormalize([]) },
      { name: 'two of one name, a long name twice, an address missing, an odd time limit', input: [
        { name: 'Division search', endpoint: 'https://mcp.example.test/a', header: 'Authorization', scheme: 'Bearer', env: 'MCP_TOKEN', enabled: true, timeout: 60 },
        { name: 'Division search', endpoint: 'https://mcp.example.test/b', header: 'x-api-key', scheme: '', env: '', enabled: true, timeout: 45 },
        { name: long + 'BC', endpoint: '', header: 'Authorization', scheme: 'Bearer', env: '', enabled: true, timeout: 30 },
        { name: long + 'BC', endpoint: ' https://mcp.example.test/c ', header: ' Authorization ', scheme: ' Bearer ', env: ' T ', enabled: false, timeout: 10 },
        { name: '  tab\there ​hidden spaced  ', endpoint: 'https://mcp.example.test/d', header: 'Authorization', scheme: 'Bearer', env: '', enabled: true, timeout: 120 },
        { name: '', endpoint: 'https://mcp.example.test/e', header: 'Authorization', scheme: 'Bearer', env: '', enabled: true, timeout: 30 }] },
      { name: 'nine: eight kept', input: Array.from({ length: 9 }, (_, i) => ({ name: 'S' + i, endpoint: 'https://mcp.example.test/' + i, header: 'Authorization', scheme: 'Bearer', env: '', enabled: true, timeout: 30 })) },
    ].map(c => ({ ...c, output: mcpNormalize(c.input) }));
    const states = [
      { ai: null, agents: [], outlook: null, mcp: [] },
      { ai: null, agents: [], outlook: null, mcp: [{ name: 'Division search', where: 'mcp.example.test' }] },
      { ai: { name: 'Claude', where: 'api.anthropic.com' }, agents: [{ name: 'Alert Hub', where: 'alerts.example.test', auto: false }], outlook: { where: 'login.microsoftonline.com and graph.microsoft.com' },
        mcp: [{ name: 'Division search', where: 'mcp.example.test' }, { name: 'Fictional demo server', where: 'mcp.example.invalid' }] },
      { ai: null, agents: [], outlook: null, mcp: [{ name: 'Division search', where: '' }] },
    ];
    const pairs = [[0, 1], [1, 2], [2, 1], [1, 0], [1, 3], [2, 2]];
    return {
      about: 'Shared cases for round 40, written by the browser simulation. The Windows app must give the same answers.',
      said: { sends: MCP_SAID.sends, kept: MCP_SAID.kept, hint: MCP_SAID.hint, noServer: MCP_SAID.noServer, notJson: MCP_SAID.notJson, tooLong: MCP_SAID.tooLong,
        nothingYet: MCP_SAID.nothingYet, chooseFirst: MCP_SAID.chooseFirst, listed: [1, 3].map(n => ({ n, text: MCP_SAID.listed(n) })) },
      argumentsFor, parse, addresses, normalize,
      characters: [{}, { query: 'goal date', max_results: 2 }, { q: 'é "quoted" \\ <b>&</b>\n' }, { emoji: '\u{1F600}', n: [1.5, true, null] }, { t: 'a\\\u{1F600}\\\\\u{1F4C4}' }, { typed: '\\uD83D\\uDE00 is text' }].map(args => ({ args, n: JSON.stringify(args).length })),
      preview: mcpPreview({ name: 'Division search' }, 'search', { query: 'Metformin', limit: 3 }, 'mcp.example.test'),
      presets: API_PRESETS,
      connections: states.map(c => ({ state: c, result: connections(c), mcpStart: connMcpStart(c) })),
      changes40: pairs.map(([i, j]) => ({ before: i, now: j, lines: connChanges(states[i], states[j]) })),
      phrases: EV_PHRASES.filter(x => x.startsWith('MCP') || x.includes('MCP')),
      changes: CHANGES.find(c => c.round === 40),
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.argumentsFor.length} sets of arguments offered, ${doc.parse.length} texts, ${doc.addresses.length} addresses, ${doc.normalize.length} lists of servers, ${doc.presets.length} presets, ${doc.connections.length} connection states, ${doc.changes40.length} changes`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round40-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
