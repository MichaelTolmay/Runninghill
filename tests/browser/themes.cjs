// Exercise the real UI and CSS while keeping all service calls away from the user's database.
const { chromium } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.RUNNINGHILL_WEB_URL || 'http://localhost:5282';
const root = path.resolve(__dirname, '../..');
const rgb = value => `rgb(${value.slice(1).match(/../g).map(value => parseInt(value, 16)).join(', ')})`;
const colors = mode => JSON.parse(fs.readFileSync(path.join(root, `themes/Runninghill_${mode}.json`))).colors;

async function check(browser, width) {
    const context = await browser.newContext({ viewport: { width, height: 950 }, colorScheme: 'light' });
    const page = await context.newPage(), errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/appsettings.Development.json', route => route.fulfill({ json: { ServiceUrl: base + '/' } }));
    await page.route('**/api/**', route => route.fulfill({ json: {
        items: route.request().url().includes('/words') ? [{ id: 1, word: 'hello', type: 'Noun' }] : [], nextAfter: null
    } }));
    await page.goto(base);
    await page.locator('.system-theme').waitFor();
    await page.locator('#token').fill('test-token');
    await page.locator('.connection-form button').first().click();
    await page.locator('.row-actions button').first().click();
    await page.locator('#word').fill('draft');
    await page.locator('#word').blur();
    for (const code of ['en-ZA', 'af-ZA', 'xh-ZA', 'zu-ZA', 'tn-ZA']) {
        await page.locator('.language-selector select').selectOption(code);
        for (const mode of ['Dark', 'Light', 'System']) {
            await page.locator('.system-theme').selectOption(mode);
            const expected = mode === 'Dark' ? 'dark' : 'light';
            await page.waitForFunction(expected => document.documentElement.dataset.appTheme === `Runninghill_${expected}`, expected);
            assert.equal(await page.locator('.app-theme').inputValue(), `Runninghill_${expected}`);
            await page.waitForFunction(({ expected, source }) => {
                const image = document.querySelector('.theme-icon img');
                return image?.dataset.icon === `Runninghill_${expected}_icon` && image.getAttribute('src') === source && image.complete && image.naturalWidth > 0;
            }, { expected, source: 'data:image/webp;base64,' + fs.readFileSync(path.join(root, `themes/Runninghill_${expected}_icon.webp`)).toString('base64') });
            const iconContainer = page.locator('.theme-icon');
            const iconBox = await iconContainer.boundingBox();
            const headerBox = await page.locator('.topbar').boundingBox();
            const introBox = await page.locator('.intro .eyebrow').boundingBox();
            assert(iconBox.width <= 256 && iconBox.height <= 128, 'Theme image exceeds its size limit');
            assert(iconBox.y >= headerBox.y + headerBox.height && iconBox.y + iconBox.height <= introBox.y, 'Theme image must be between the header and introduction');
            if (width <= 560)
                assert(Math.abs(iconBox.x + iconBox.width / 2 - width / 2) <= 2, 'Phone logo is not centered');
            else
                assert(Math.abs(iconBox.x - introBox.x) <= 2, 'Desktop/tablet logo is not aligned with the introduction');
            const logoStyle = await iconContainer.evaluate(node => {
                const style = getComputedStyle(node);
                return { color: style.backgroundColor, mask: style.maskImage };
            });
            assert.equal(logoStyle.color, rgb(expected === 'dark' ? '#FFFFFF' : '#1F2937'));
            assert(logoStyle.mask.includes('data:image/webp;base64,'), 'Logo must use the transparent artwork as its mask');
            assert.equal(await page.locator('html').evaluate(node => getComputedStyle(node).backgroundColor), rgb(colors(expected).Background));
            assert.equal(await page.locator('.panel').first().evaluate(node => getComputedStyle(node).backgroundColor), rgb(colors(expected).Surface));
            assert.equal(await page.locator('#word').evaluate(node => getComputedStyle(node).backgroundColor), rgb(colors(expected).Surface));
            assert.equal(await page.locator('.tag.noun').first().evaluate(node => getComputedStyle(node).backgroundColor), rgb(colors(expected).NounBackground));
            assert.equal(await page.locator('#word').inputValue(), 'draft');
            assert.equal(await page.locator('#token').inputValue(), 'test-token');
            assert.equal(await page.locator('.sentence-text').textContent(), 'hello');
            const boxes = await Promise.all(['.system-theme', '.app-theme', '.appearance-toolbar button'].map(selector => page.locator(selector).boundingBox()));
            assert(boxes[0].x + boxes[0].width <= boxes[1].x + 1);
            assert(boxes[1].x + boxes[1].width <= boxes[2].x + 1);
            const toolbar = await page.locator('.appearance-toolbar').boundingBox();
            assert(Math.abs(toolbar.x + toolbar.width / 2 - width / 2) <= 2, `Toolbar not centered at ${width}px`);
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `Overflow: ${code}/${mode}/${width}`);
            if (code !== 'en-ZA') {
                assert.notEqual(await page.locator('.theme-field span').first().textContent(), 'System theme');
                assert.notEqual(await page.locator('.system-theme option[value="Dark"]').textContent(), 'Dark');
            }
        }
    }
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.waitForFunction(() => document.documentElement.dataset.appTheme === 'Runninghill_dark');
    assert.equal(await page.locator('.system-theme').inputValue(), 'System');
    await page.locator('.system-theme').selectOption('Light');
    await page.emulateMedia({ colorScheme: 'light' });
    await page.emulateMedia({ colorScheme: 'dark' });
    assert.equal(await page.locator('.app-theme').inputValue(), 'Runninghill_light');
    await page.locator('.system-theme').selectOption('Dark');
    await page.locator('.appearance-toolbar button').click();
    await page.locator('#log-source').waitFor();
    assert.equal(await page.locator('.app-theme').inputValue(), 'Runninghill_dark');
    assert.equal(await page.locator('.panel').first().evaluate(node => getComputedStyle(node).backgroundColor), rgb(colors('dark').Surface));
    await page.locator('.appearance-toolbar button').click();
    await page.reload();
    await page.waitForFunction(() => document.documentElement.dataset.appTheme === 'Runninghill_dark');
    assert.equal(await page.locator('.system-theme').inputValue(), 'Dark');
    assert.equal(await page.locator('#token').inputValue(), '');
    fs.mkdirSync(path.join(root, 'artifacts/ui'), { recursive: true });
    await page.screenshot({ path: path.join(root, `artifacts/ui/themes-dark-${width}.png`), fullPage: true });
    assert.deepEqual(errors, []);
    await context.close();
    console.log(`Themes passed at ${width}px: responsive logo alignment and colour, five languages, persistence, OS changes, draft preservation, Logs.`);
}

(async () => {
    const browser = await chromium.launch({ executablePath: process.env.RUNNINGHILL_CHROME_BINARY, headless: true, args: ['--no-sandbox'] });
    try { for (const width of [320, 560, 561, 768, 1440]) await check(browser, width); }
    finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
