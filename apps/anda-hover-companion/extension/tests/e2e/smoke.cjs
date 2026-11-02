const h = require('./harness.cjs');
(async () => {
  const { context, worker, id } = await h.launch();
  console.log('id', id, 'expected', h.EXPECTED_ID);
  console.log('workers', context.serviceWorkers().length);
  await context.close();
})().catch((e) => { console.error(e); process.exit(1); });
