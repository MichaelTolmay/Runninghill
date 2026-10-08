// Test UI state independently of existing database contents. Only API replies are faked;
// the actual Blazor app, browser events, rendering, and request serialization run normally.
const { chromium } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.RUNNINGHILL_WEB_URL || (process.env.RUNNINGHILL_DEBUG === '1'
    ? 'http://localhost:5182' : 'http://localhost:5082');
const screenshotDirectory = path.resolve(__dirname, '../../artifacts/ui');

async function checkViewport(browser, width) {
    const context = await browser.newContext({ viewport: { width, height: width < 600 ? 740 : 1050 } });
    const page = await context.newPage();
    page.setDefaultTimeout(15000);
    const errors = [], requests = [], sentencePosts = [];
    const words = [], sentences = [];
    page.on('pageerror', error => errors.push(error.message));
    // Debug normally uses a separate API port. Keep all test traffic on this origin.
    await page.route('**/appsettings.Development.json', route => route.fulfill({
        json: { ServiceUrl: `${base}/` }
    }));
    await page.route('**/api/**', async route => {
        const request = route.request();
        const url = new URL(request.url());
        const method = request.method();
        requests.push({ method, url });
        if (url.pathname === '/api/words' && method === 'GET') {
            const search = url.searchParams.get('search') || '';
            const types = (url.searchParams.get('types') || '').split(',').filter(Boolean);
            return route.fulfill({ json: {
                items: words.filter(word => word.word.startsWith(search) && (!types.length || types.includes(word.type))),
                nextAfter: null
            } });
        }
        if (url.pathname === '/api/words' && method === 'POST') {
            const word = { id: words.length + 1, ...request.postDataJSON() };
            words.push(word);
            return route.fulfill({ status: 201, json: word });
        }
        if (url.pathname.startsWith('/api/words/') && method === 'PUT') {
            const word = words.find(word => word.id === Number(url.pathname.split('/').pop()));
            Object.assign(word, request.postDataJSON());
            return route.fulfill({ json: word });
        }
        if (url.pathname === '/api/sentences' && method === 'GET') {
            return route.fulfill({ json: { items: sentences, nextAfter: null } });
        }
        if (url.pathname === '/api/sentences' && method === 'POST') {
            const body = request.postDataJSON();
            sentencePosts.push(body);
            const sentence = { id: sentences.length + 1,
                text: body.wordIds.map(id => words.find(word => word.id === id).word).join(' '),
                createdAt: new Date().toISOString() };
            sentences.unshift(sentence);
            return route.fulfill({ status: 201, json: sentence });
        }
        errors.push(`Unexpected API request: ${method} ${url.pathname}`);
        await route.fulfill({ status: 500 });
    });
    const status = text => page.getByRole('status').filter({ hasText: text }).waitFor();
    const rows = page.locator('.word-list > li');
    const menu = page.locator('.type-filter');
    const summary = menu.locator('summary');
    const saveSentence = page.getByRole('button', { name: 'Save sentence', exact: true });
    const isOpen = () => menu.evaluate(element => element.open);
    const apply = () => page.getByRole('button', { name: 'Apply filters', exact: true }).click();
    try {
        await page.goto(base);
        await page.getByLabel('Access token', { exact: true }).fill('ui-test-token');
        await page.getByRole('button', { name: 'Connect / refresh' }).click();
        await status('up to date');
        assert.ok(await saveSentence.isDisabled(), 'An empty sentence cannot be saved');
        assert.match(await page.locator('#sentence-help').innerText(), /Add at least one word/);

        for (const [word, type] of [['birds', 'Noun'], ['fly', 'Verb'], ['swiftly', 'Adverb']]) {
            await page.getByLabel('Add a word', { exact: true }).fill(word);
            await page.getByLabel('Word type', { exact: true }).selectOption(type);
            await page.getByRole('button', { name: 'Add word', exact: true }).click();
            await status('Word saved');
            assert.equal(await rows.count(), words.length, 'Saving must retain the other visible words');
            assert.equal(await page.getByLabel('Search collection').inputValue(), '');
        }

        // Dismissing the menu must keep choices and never call the API on its own.
        await summary.click();
        await page.getByLabel('Noun', { exact: true }).check();
        const requestCount = requests.length;
        await page.keyboard.press('Escape');
        assert.equal(await isOpen(), false);
        assert.ok(await summary.evaluate(element => element === document.activeElement));
        await summary.click();
        assert.ok(await page.getByLabel('Noun', { exact: true }).isChecked());
        await page.getByRole('button', { name: 'Close word types', exact: true }).click();
        assert.equal(await isOpen(), false);
        await summary.click();
        await page.getByRole('heading', { name: 'Word collection', exact: true }).click();
        assert.equal(await isOpen(), false);
        assert.equal(requests.length, requestCount);

        // Test the Apply button inside the menu, then the external form button.
        await summary.click();
        await page.getByRole('button', { name: 'Apply selected types', exact: true }).click();
        await status('Filters applied');
        assert.equal(await isOpen(), false);
        assert.equal(await rows.count(), 1);
        assert.match(await rows.innerText(), /birds/);
        await summary.click();
        await page.getByLabel('Verb', { exact: true }).check();
        await apply();
        await status('Filters applied');
        assert.equal(await isOpen(), false);
        assert.equal(await rows.count(), 2);
        await page.getByRole('button', { name: 'Clear filters', exact: true }).click();
        await status('Filters applied');
        assert.equal(await rows.count(), 3);

        // Saving an edit from a filtered view also returns the complete first page.
        await page.getByLabel('Search collection').fill('birds');
        await apply();
        await status('Filters applied');
        assert.equal(await rows.count(), 1);
        await page.getByRole('button', { name: 'Edit birds', exact: true }).click();
        await page.getByLabel('Edit word', { exact: true }).fill('eagles');
        await page.getByRole('button', { name: 'Save changes', exact: true }).click();
        await status('Word saved');
        assert.equal(await rows.count(), 3);
        assert.equal(await page.getByLabel('Search collection').inputValue(), '');
        const lastWordsGet = requests.filter(r => r.method === 'GET' && r.url.pathname === '/api/words').at(-1);
        assert.equal(lastWordsGet.url.searchParams.get('search'), '');
        assert.equal(lastWordsGet.url.searchParams.get('types'), '');

        await page.getByRole('button', { name: 'Add eagles to sentence', exact: true }).click();
        await status('Added eagles');
        assert.ok(await saveSentence.isEnabled());
        await page.getByRole('button', { name: 'Add fly to sentence', exact: true }).click();
        await status('Added fly');
        assert.equal(await page.locator('.sentence-text').innerText(), 'eagles fly');
        await saveSentence.click();
        await status('Sentence saved');
        assert.equal(sentencePosts.length, 1);
        assert.deepEqual(sentencePosts[0].wordIds, [1, 2]);
        assert.match(sentencePosts[0].requestId, /^[0-9a-f-]{36}$/);
        assert.equal(await page.locator('.history li p').innerText(), 'eagles fly');
        assert.ok(await saveSentence.isDisabled());

        // Clicking an outside button closes the menu AND runs that button's action.
        await page.getByRole('button', { name: 'Add eagles to sentence', exact: true }).click();
        await status('Added eagles');
        await summary.click();
        await page.locator('#sentence').getByRole('button', { name: 'Clear', exact: true }).click();
        assert.equal(await isOpen(), false);
        assert.equal(await page.locator('.sentence-words > li').count(), 0);
        await summary.click();
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Menu overflow at ${width}px`);
        // Scroll to the last type; the sticky Close button must still be usable.
        await page.getByLabel('Determiner', { exact: true }).scrollIntoViewIfNeeded();
        await page.getByRole('button', { name: 'Close word types', exact: true }).click();
        assert.equal(await isOpen(), false);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Page overflow at ${width}px`);
        fs.mkdirSync(screenshotDirectory, { recursive: true });
        await page.screenshot({ path: path.join(screenshotDirectory, `regressions-${width}.png`), fullPage: true });
        assert.deepEqual(errors, []);
        console.log(`Collection UI regressions passed at ${width}px: multiple words, filter dismissal, edits, sentence POST.`);
    } finally { await context.close(); }
}

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.RUNNINGHILL_CHROME_BINARY,
        headless: true, args: ['--no-sandbox'] });
    try {
        for (const width of [1440, 820, 390, 320]) await checkViewport(browser, width);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
