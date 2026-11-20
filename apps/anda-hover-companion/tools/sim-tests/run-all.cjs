// Runs every browser suite of the simulation and prints, for each, its exit code and how many checks passed and failed; then the totals.
// A suite counts as failed when it exits other than 0 or prints a failed check.
// Usage, from any folder:  node run-all.cjs [tag] [groups]      (groups: how many suites run side by side; 3 by default)
// The full output of each suite is kept under the temporary folder, in anda-sim-tests/<tag>/. The exit code is 1 if any suite's is not 0.
const { spawn } = require('child_process'), fs = require('fs'), path = require('path'), os = require('os');
const suites = JSON.parse(fs.readFileSync(path.join(__dirname, 'suites.json'), 'utf8'));
const tag = process.argv[2] || 'run', groups = Math.max(1, Math.min(8, Number(process.argv[3]) || 3));
const out = path.join(os.tmpdir(), 'anda-sim-tests', tag); fs.mkdirSync(out, { recursive: true });
const queue = suites.slice(), results = [];
const one = file => new Promise(done => {
  const log = fs.createWriteStream(path.join(out, file.replace(/\.js$/, '.txt')));
  const child = spawn(process.execPath, [path.join(__dirname, file)], { stdio: ['ignore', 'pipe', 'pipe'], env: process.env });
  let text = ''; const take = d => { text += d; log.write(d); };
  child.stdout.on('data', take); child.stderr.on('data', take);
  const timer = setTimeout(() => child.kill('SIGKILL'), 15 * 60 * 1000);      // a suite that hangs is stopped and counted as failed
  child.on('close', code => {
    clearTimeout(timer); log.end();
    const lines = text.split('\n'), pass = lines.filter(l => /^PASS|^ok |\u2713/.test(l)).length, fail = lines.filter(l => /^FAIL|\u2717|^not ok/.test(l)).length;
    // A suite that printed a failed check is counted as failed even if it ended with 0: twelve older suites print FAIL without setting their exit code.
    const status = code === null ? 'killed' : code === 0 && fail > 0 ? 'FAIL lines' : code;
    results.push({ file, code: status, pass, fail });
    console.log(`${file} exit=${code === null ? 'killed' : code} pass=${pass} fail=${fail}${status === 'FAIL lines' ? ' (counted as failed: it printed a failed check)' : ''}`);
    done();
  });
});
const worker = async () => { while (queue.length) await one(queue.shift()); };
Promise.all(Array.from({ length: groups }, worker)).then(() => {
  const bad = results.filter(r => r.code !== 0);
  console.log(`\n${results.length - bad.length} of ${results.length} suites passed (exit 0 and no failed check); failed: ${bad.length}${bad.length ? ' (' + bad.map(r => r.file).join(', ') + ')' : ''}`);
  console.log(`checks passed: ${results.reduce((n, r) => n + r.pass, 0)}; failed: ${results.reduce((n, r) => n + r.fail, 0)}`);
  console.log(`output of each suite: ${out}`);
  process.exit(bad.length ? 1 : 0);
});
