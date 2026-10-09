// Real Blazor rendering and serialization, with fake API replies so no database is changed.
const { chromium } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.RUNNINGHILL_WEB_URL || 'http://localhost:5282';
const codes = ['en-ZA', 'af-ZA', 'xh-ZA', 'zu-ZA', 'tn-ZA'];
const resourceRoot = path.resolve(__dirname, '../../src/Runninghill.Contracts/Resources');
const decode = text => text.replaceAll('&amp;', '&').replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&quot;', '"').replaceAll('&apos;', "'");
function resources(code) {
    const suffix = code === 'en-ZA' ? '' : '_' + code.replace('-', '_');
    return Object.fromEntries([...fs.readFileSync(path.join(resourceRoot, `Text${suffix}.resx`), 'utf8')
        .matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)].map(match => [match[1], decode(match[2])]));
}
const english = resources('en-ZA');
const translate = (code, message) => resources(code)[Object.keys(english).find(key => english[key] === message)];

async function check(browser, width) {
    const context = await browser.newContext({ viewport: { width, height: 900 } });
    const page = await context.newPage();
    const errors = [], sent = [];
    let deny = false;
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/appsettings.Development.json', route => route.fulfill({ json: { ServiceUrl: base + '/' } }));
    await page.route('**/api/**', route => {
        const request = route.request(), url = new URL(request.url());
        sent.push({ language: request.headers()['accept-language'], body: request.postDataJSON(), url: url.pathname });
        if (deny) return route.fulfill({ status: 401, headers: { 'X-Request-ID': 'language-test' } });
        if (request.method() === 'POST') return route.fulfill({ json: { id: 3, ...request.postDataJSON() }, status: 201 });
        return route.fulfill({ json: { items: url.pathname.endsWith('/words')
            ? [{ id: 1, word: 'hello', type: 'Noun' }, { id: 2, word: 'umhlaba', type: 'Verb' }] : [], nextAfter: null } });
    });
    await page.goto(base);
    await page.locator('.language-selector select').waitFor();
    for (const code of codes) {
        await page.locator('.language-selector select').selectOption(code);
        assert.equal(await page.locator('.empty h3').textContent(), translate(code, 'Your collection starts here'));
        assert.equal(await page.locator('.connection-badge').textContent(), translate(code, 'Connect to get started'));
        assert.equal(await page.locator('.empty p').textContent(), translate(code, 'Enter an access token and connect to load your saved words.'));
        await page.locator('.connection-form button').first().click();
        await page.locator('.feedback').filter({ hasText: translate(code, 'Copy a valid access token and connect again.') }).waitFor();
    }
    await page.locator('.language-selector select').selectOption('en-ZA');
    await page.locator('#token').fill('test-token');
    await page.locator('.connection-form button').first().click();
    await page.locator('.word-list li').first().waitFor();
    await page.locator('.row-actions button').first().click();
    for (const code of codes) {
        await page.locator('#word').fill('draft');
        await page.locator('#word').blur();
        await page.locator('.language-selector select').selectOption(code);
        await page.waitForFunction(expected => document.documentElement.lang === expected, code);
        assert.equal(await page.locator('h1').textContent(), translate(code, 'Your words. New possibilities.'));
        assert.equal(await page.locator('#word').inputValue(), 'draft');
        assert.equal(await page.locator('#token').inputValue(), 'test-token');
        assert.equal(await page.locator('.sentence-text').textContent(), 'hello');
        assert.equal(await page.locator('#word-type option[value="Noun"]').textContent(), translate(code, 'Noun'));
        // Labels translate; the saved data and selected API type do not.
        await page.locator('.word-form button').first().click();
        await page.locator('.feedback').filter({ hasText: translate(code, 'Word saved.') }).waitFor();
        const post = sent.findLast(request => request.body);
        assert.equal(post.language, code);
        assert.equal(post.body.type, 'Noun');
        assert.equal(post.body.word, 'draft');
        await page.locator('.type-filter summary').click();
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false,
            `Dropdown overflow: ${code} at ${width}px`);
        await page.locator('[data-close-dropdown]').click();
        assert.equal(await page.locator('.type-filter').getAttribute('open'), null);
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
        assert.equal(overflow, false, `Horizontal overflow: ${code} at ${width}px`);
        await page.locator('.topbar button').click();
        assert.equal(await page.locator('h1').textContent(), translate(code, 'Event logs'));
        assert.equal(await page.locator('#log-source option[value="service"]').textContent(), translate(code, 'Service'));
        assert.equal(await page.locator('#log-level option[value="Error"]').textContent(), translate(code, 'Error'));
        await page.locator('.topbar button').click();
        deny = true;
        await page.locator('.connection summary').click();
        await page.locator('.connection-form button').first().click();
        await page.locator('.feedback').filter({ hasText: translate(code, 'Your access token is missing, expired, or invalid. Please enter a new access token and try again.') }).waitFor();
        deny = false;
        await page.locator('.connection-form button').first().click();
        await page.locator('.word-list li').first().waitFor();
    }
    await page.reload();
    await page.locator('.language-selector select').waitFor();
    assert.equal(await page.locator('.language-selector select').inputValue(), 'tn-ZA');
    assert.equal(await page.locator('#token').inputValue(), '');
    assert.deepEqual(errors, []);
    await context.close();
    console.log(`Five languages passed at ${width}px.`);
}

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.RUNNINGHILL_CHROME_BINARY, headless: true, args: ['--no-sandbox'] });
    try { for (const width of [320, 768, 1440]) await check(browser, width); }
    finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
