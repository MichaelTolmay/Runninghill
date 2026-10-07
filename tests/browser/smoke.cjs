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
    await page.getByLabel('Access token', {exact:true}).fill('invalid-token');
    await page.getByLabel('Access token', {exact:true}).press('Tab');
    await page.getByRole('button', {name:'Check connection', exact:true}).click();
    await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('valid access token'));
    console.log('PASS browser displays authentication failure');
    await page.reload();
    await page.getByLabel('Access token', {exact:true}).waitFor();
    if (await page.getByLabel('Access token', {exact:true}).inputValue() !== '') throw Error('Token persisted across reload');
    if (failures.length) throw Error(failures.join('\n'));
    console.log('PASS no browser runtime errors; token cleared on reload');
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exit(1); });
