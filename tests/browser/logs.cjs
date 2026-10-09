// Exercise the real UI with controlled server replies, including permission failures.
const { chromium } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.RUNNINGHILL_WEB_URL || (process.env.RUNNINGHILL_DEBUG === '1'
    ? 'http://localhost:5182' : 'http://localhost:5082');

async function checkViewer(browser, width) {
    const context = await browser.newContext({ viewport: { width, height: 900 } });
    const page = await context.newPage();
    page.setDefaultTimeout(20000);
    const errors = [], logRequests = [];
    let denied = false, malformed = false;
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/appsettings.Development.json', route => route.fulfill({ json: { ServiceUrl: `${base}/` } }));
    await page.route('**/api/**', async route => {
        const url = new URL(route.request().url());
        if (url.pathname === '/api/words') return route.fulfill({ json: { items: [{ id: 1, word: 'birds', type: 'Noun' }], nextAfter: null } });
        if (url.pathname === '/api/sentences') return route.fulfill({ json: { items: [], nextAfter: null } });
        if (url.pathname === '/api/logs') {
            logRequests.push(url);
            assert.equal(route.request().headers().authorization, 'Bearer ui-test-token');
            if (denied) return route.fulfill({ status: 403, headers: { 'X-Request-ID': 'logs-permission-test' } });
            if (malformed) return route.fulfill({ json: { items: [null] } });
            const older = url.searchParams.get('before') !== '0';
            const items = Array.from({ length: older ? 1 : 20 }, (_, index) => ({ id: older ? 1 : 30 - index,
                timestamp: '2026-10-09T10:00:00Z', level: 'Warning', category: 'Runninghill.Test', eventId: 1001,
                message: index === 0 ? '<script>window.logInjection = true</script> ' + 'long-reference-'.repeat(80) : 'Completed test operation' }));
            return route.fulfill({ json: { items, nextBefore: older ? null : 11, retainedCount: 21 } });
        }
        throw new Error(`Unexpected request: ${url.pathname}`);
    });
    try {
        await page.goto(base);
        await page.getByLabel('Access token', { exact: true }).fill('ui-test-token');
        await page.getByRole('button', { name: 'Connect / refresh' }).click();
        await page.getByRole('status').filter({ hasText: 'up to date' }).waitFor();
        await page.getByRole('button', { name: 'Add birds to sentence', exact: true }).click();
        await page.getByLabel('Add a word', { exact: true }).fill('unfinished');
        await page.getByRole('button', { name: 'Logs', exact: true }).click();
        await page.getByRole('heading', { name: 'Event logs', exact: true }).waitFor();
        assert.ok(await page.locator('.log-entry').count() > 0, 'App actions appear immediately');
        assert.doesNotMatch(await page.locator('.log-list').innerText(), /ui-test-token|unfinished/);
        await page.getByLabel('Search message or category').fill('no-such-event');
        await page.getByRole('button', { name: 'Refresh logs', exact: true }).click();
        await page.getByRole('heading', { name: 'No matching events' }).waitFor();
        await page.getByRole('button', { name: 'Reset filters' }).click();
        await page.getByLabel('Source', { exact: true }).selectOption('service');
        assert.equal(await page.getByLabel('Service access token with logs.read permission').inputValue(), 'ui-test-token');
        await page.getByLabel('Severity', { exact: true }).selectOption('Warning');
        await page.getByLabel('Search message or category').fill('request-reference');
        await page.getByRole('button', { name: 'Refresh logs', exact: true }).click();
        await page.getByRole('status').filter({ hasText: '20 events shown' }).waitFor();
        assert.equal(await page.locator('.log-entry').count(), 20);
        assert.equal(logRequests.at(-1).searchParams.get('level'), 'Warning');
        assert.equal(logRequests.at(-1).searchParams.get('search'), 'request-reference');
        assert.equal(await page.evaluate(() => window.logInjection), undefined, 'Log text must never execute as HTML');
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Long messages must wrap without horizontal scrolling');
        fs.mkdirSync(path.resolve(__dirname, '../../artifacts/ui'), { recursive: true });
        await page.screenshot({ path: path.resolve(__dirname, `../../artifacts/ui/logs-${width}.png`) });
        await page.getByRole('button', { name: 'Older events' }).click();
        await page.getByRole('status').filter({ hasText: '1 events shown' }).waitFor();
        assert.equal(logRequests.at(-1).searchParams.get('before'), '11');
        assert.ok(await page.getByRole('button', { name: 'Older events' }).isDisabled());
        denied = true;
        await page.getByRole('button', { name: 'Newest', exact: true }).click();
        await page.getByRole('status').filter({ hasText: 'logs.read permission' }).waitFor();
        assert.match(await page.getByRole('status').innerText(), /logs-permission-test/);
        assert.equal(await page.locator('.log-entry').count(), 0, 'Failed permission check clears stale results');
        denied = false; malformed = true;
        await page.getByRole('button', { name: 'Refresh logs', exact: true }).click();
        await page.getByRole('status').filter({ hasText: 'reply' }).waitFor();
        assert.equal(await page.locator('.log-entry').count(), 0);
        await page.getByRole('button', { name: 'Back to collection' }).click();
        assert.equal(await page.getByLabel('Add a word', { exact: true }).inputValue(), 'unfinished');
        assert.ok(await page.getByRole('button', { name: 'Save sentence', exact: true }).isEnabled());
        assert.match(await page.locator('.connection-badge').innerText(), /Connected/);
        assert.deepEqual(errors, []);
        console.log(`Log viewer passed at ${width}px: app events, filters, paging, permission errors, safe text, draft preservation.`);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({ executablePath: process.env.RUNNINGHILL_CHROME_BINARY, headless: true, args: ['--no-sandbox'] });
    try { for (const width of [1440, 820, 390, 320]) await checkViewer(browser, width); }
    finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
