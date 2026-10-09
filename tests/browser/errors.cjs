// Exercise real startup and .NET rendering failures without changing the user's database.
const { chromium } = require('playwright-core');
const assert = require('node:assert/strict');
const base = process.env.RUNNINGHILL_WEB_URL || 'http://localhost:5282';

async function check(browser, configure, expected) {
    const context = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await context.newPage();
    try {
        await configure(page);
        await page.goto(base);
        if (!expected) {
            await page.locator('.system-theme').waitFor();
            assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
            await page.reload();
            await page.locator('.system-theme').waitFor();
            assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
            const icon = await page.locator('.theme-icon').boundingBox();
            assert(icon.width <= 256 && icon.height <= 128, 'Published stylesheets must load as well as startup scripts');
        } else {
            await page.locator('#blazor-error-ui').waitFor({state:'visible'});
            await page.waitForFunction(() => document.getElementById('startup-details').textContent.includes('reference'));
            const details = JSON.parse(await page.locator('#startup-details').textContent());
            assert.equal(details.code, expected);
            assert(details.reference && details.timeUtc && details.startupVersion);
            assert(!JSON.stringify(details).includes('private-token'));
            assert(!(await page.locator('#startup-error').textContent()).includes('An unexpected error occurred'));
            assert((await page.locator('#startup-error').textContent()).length > 50);
            if (await page.locator('html').getAttribute('lang') !== 'en-ZA')
                assert(!(await page.locator('#startup-error').textContent()).startsWith('A file needed'), 'Startup recovery advice must be translated');
            if (expected === 'RH-WEB-RENDER') assert.equal(details.errorType, 'JSException');
            // Clipboard failures leave a selectable report instead of raising another error.
            await page.evaluate(() => Object.defineProperty(navigator, 'clipboard', {value:{writeText:()=>Promise.reject(new Error('blocked'))}}));
            await page.locator('#startup-copy').click();
            await page.waitForFunction(() => document.querySelector('#blazor-error-ui details').open);
            assert((await page.locator('#startup-copy-status').textContent()).length > 20);
        }
    } finally { await context.close(); }
}

(async () => {
    const browser = await chromium.launch({executablePath:process.env.RUNNINGHILL_CHROME_BINARY,headless:true,args:['--no-sandbox']});
    try {
        await check(browser, async page => {
            // Model an old browser cache: a new release must never request these unversioned helpers.
            await page.route(/\/(appearance|localization|dropdown)\.js$/, route => route.fulfill({
                contentType:'application/javascript', body:'throw new TypeError("stale cached helper");'
            }));
            await page.addInitScript(() => {
                localStorage.setItem('runninghill.appearance', '{broken');
                localStorage.setItem('runninghill.language', 'unknown-language');
            });
        });
        for (const language of ['en-ZA', 'af-ZA', 'xh-ZA', 'zu-ZA', 'tn-ZA']) {
            await check(browser, async page => {
                await page.addInitScript(language => localStorage.setItem('runninghill.language', language), language);
                await page.route('**/appearance*.js', route => route.abort());
            }, 'RH-WEB-ASSET');
        }
        await check(browser, async page => page.route('**/appearance*.js', route => route.fulfill({
            contentType:'application/javascript', body:'window.runninghillTheme = {state(){throw new TypeError("private-token");}};'
        })), 'RH-WEB-RENDER');
        await check(browser, async page => page.route('**/_framework/blazor.webassembly*.js', route => route.abort()), 'RH-WEB-ASSET');
        console.log('PASS startup: warm reload, corrupt preferences, blocked asset, rendering failure, runtime download failure, private-data exclusion, clipboard fallback');
    } finally { await browser.close(); }
})().catch(error => {console.error(error);process.exit(1);});
