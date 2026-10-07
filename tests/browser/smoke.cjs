const { chromium } = require('playwright-core');
const { execFileSync } = require('node:child_process');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
(async () => {
  const token = execFileSync('python3', [path.join(root, 'scripts/dev-token.py')], {encoding:'utf8'}).trim();
  const browser = await chromium.launch({executablePath:process.env.RUNNINGHILL_CHROME_BINARY, headless:true});
  try {
    const page = await browser.newPage();
    const failures = [];
    let compressedNativeModule = false;
    page.on('response', response => {
      if (response.url().includes('/_framework/dotnet.native') && response.url().endsWith('.wasm')) {
        compressedNativeModule = response.headers()['content-encoding'] === 'gzip';
      }
    });
    page.on('pageerror', e => { failures.push(e.message); console.log('BROWSER ERROR:',e.message); });
    page.on('console', m => { console.log('BROWSER CONSOLE:',m.type(),m.text()); });
    page.on('requestfailed', r => console.log('REQUEST FAILED:',r.url(), r.failure()?.errorText));
    await page.goto('http://127.0.0.1:5082/');
    await page.getByRole('heading', {name:'Runninghill', exact:true}).waitFor({timeout:60000});
    await page.getByLabel('Access token', {exact:true}).fill(token);
    await page.getByLabel('Access token', {exact:true}).press('Tab');
    await page.getByRole('button', {name:'Check connection', exact:true}).click();
    await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('Database schema verified'), null, {timeout:15000});
    console.log('PASS AOT browser UI -> authenticated API -> PostgreSQL');
    if (!compressedNativeModule) throw Error('Native WebAssembly module was not served with gzip');
    console.log('PASS precompressed WebAssembly delivery');
    await page.getByLabel('Access token', {exact:true}).fill('invalid-token');
    await page.getByLabel('Access token', {exact:true}).press('Tab');
    await page.getByRole('button', {name:'Check connection', exact:true}).click();
    await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('new access token'));
    console.log('PASS browser displays authentication failure');
    // Simulate replies that real outages or a mismatched service version might produce.
    // Expected errors must leave the page usable and must never echo private response bodies.
    for (const body of ['not-json', 'null', '{}', '{"message":""}']) {
      await page.route('**/api/status', route => route.fulfill({
        status: 200, contentType: 'application/json', body,
        headers: {'X-Request-ID': 'browser-invalid-reply'}
      }));
      const received = page.waitForResponse(response => response.url().endsWith('/api/status'));
      await page.getByRole('button', {name:'Check connection', exact:true}).click();
      await received;
      await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('cannot read'));
      if (!(await page.getByRole('status').textContent()).includes('browser-invalid-reply')) throw Error('Missing request reference');
      await page.unroute('**/api/status');
    }
    await page.route('**/api/status', route => route.fulfill({
      status: 429, body: 'private-server-detail', headers: {'X-Request-ID': 'browser-busy'}
    }));
    await page.getByRole('button', {name:'Check connection', exact:true}).click();
    await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('wait a few seconds'));
    const busyMessage = await page.getByRole('status').textContent();
    if (!busyMessage.includes('browser-busy') || busyMessage.includes('private-server-detail')) throw Error('Unsafe or missing error details');
    await page.unroute('**/api/status');
    console.log('PASS malformed replies and busy service show safe, actionable errors');
    await page.reload();
    await page.getByLabel('Access token', {exact:true}).waitFor();
    if (await page.getByLabel('Access token', {exact:true}).inputValue() !== '') throw Error('Token persisted across reload');
    if (failures.length) throw Error(failures.join('\n'));
    console.log('PASS no browser runtime errors; token cleared on reload');
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exit(1); });
