// Exercise the real REST API through the UI at desktop, tablet and phone widths.
const { chromium } = require('playwright-core');
const { execFileSync } = require('node:child_process');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const debug = process.env.RUNNINGHILL_DEBUG === '1';
const base = process.env.RUNNINGHILL_WEB_URL || (debug ? 'http://localhost:5182' : 'http://localhost:5082');
const token = execFileSync('python3', debug ? ['scripts/dev.py', 'token'] : ['scripts/dev-token.py'], { cwd: root, encoding: 'utf8' }).trim();
const suffix = Date.now().toString().replace(/\d/g, c => String.fromCharCode(97 + Number(c)));
const word = 'wonder' + suffix;
(async () => {
    const browser = await chromium.launch({ executablePath: process.env.RUNNINGHILL_CHROME_BINARY, headless: true, args: ['--no-sandbox'] });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    try {
        await page.goto(base);
        await page.getByLabel('Access token', { exact: true }).fill(token);
        await page.getByRole('button', { name: 'Connect / refresh' }).click();
        await page.getByRole('status').filter({ hasText: 'up to date' }).waitFor();
        await page.getByLabel('Add a word', { exact: true }).fill(word);
        await page.getByLabel('Word type', { exact: true }).selectOption('Noun');
        await page.getByRole('button', { name: 'Add word', exact: true }).click();
        await page.getByRole('status').filter({ hasText: 'Word saved' }).waitFor();
        // Search is required when the collection already contains more than one page.
        await page.getByLabel('Search collection').fill(word);
        await page.getByRole('button', { name: 'Apply filters' }).click();
        const row = page.locator('.word-list > li').filter({ hasText: word });
        await row.waitFor();
        await row.getByRole('button', { name: `Edit ${word}`, exact: true }).click();
        await page.getByLabel('Word type', { exact: true }).selectOption('Verb');
        await page.getByRole('button', { name: 'Save changes', exact: true }).click();
        await page.getByRole('status').filter({ hasText: 'Word saved' }).waitFor();
        await page.getByLabel('Search collection').fill(word);
        await page.getByRole('button', { name: 'Apply filters' }).click();
        await row.getByText('Verb', { exact: true }).waitFor();
        await row.getByRole('button', { name: `Add ${word} to sentence`, exact: true }).click();
        await row.getByRole('button', { name: `Add ${word} to sentence`, exact: true }).click();
        assert.equal(await page.locator('.sentence-words > li').count(), 2);
        await page.getByRole('button', { name: 'Remove word 2', exact: true }).click();
        await page.getByRole('button', { name: 'Save sentence', exact: true }).click();
        await page.getByRole('status').filter({ hasText: 'Sentence saved' }).waitFor();
        // Check responsive layout and a usable type selector at intermediate widths too.
        fs.mkdirSync(path.join(root, 'artifacts/ui'), { recursive: true });
        for (const width of [1440, 820, 390, 320]) {
            await page.setViewportSize({ width, height: 1050 });
            await page.locator('.type-filter summary').click();
            await page.getByLabel('Noun', { exact: true }).check();
            await page.getByLabel('Noun', { exact: true }).uncheck();
            await page.locator('.type-filter summary').click();
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Horizontal overflow at ${width}px`);
            await page.screenshot({ path: path.join(root, `artifacts/ui/web-${width}.png`), fullPage: true });
        }
        await row.getByRole('button', { name: `Delete ${word}`, exact: true }).click();
        await row.getByRole('button', { name: 'Keep word', exact: true }).click();
        assert.equal(await row.count(), 1);
        await row.getByRole('button', { name: `Delete ${word}`, exact: true }).click();
        await row.getByRole('button', { name: 'Confirm delete', exact: true }).click();
        await page.getByRole('status').filter({ hasText: 'Word deleted' }).waitFor();
        assert.equal(await row.count(), 0);
        // Invalid tokens must explain recovery; no app crash or blank screen.
        await page.locator('.connection summary').click();
        await page.getByLabel('Access token', { exact: true }).fill('invalid');
        await page.getByRole('button', { name: 'Connect / refresh' }).click();
        await page.getByRole('status').filter({ hasText: 'expired, or invalid' }).waitFor();
        assert.deepEqual(errors, []);
        console.log('Collection browser checks passed: CRUD, sentence builder, delete cancellation, auth failure, 1440/820/390/320px layouts.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
