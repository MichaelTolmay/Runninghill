// Loaded only into build output by dashboard.py, never packaged with the application.
(async () => {
    const base = __TEST_BASE__;
    const control = __TEST_CONTROL__; // Test receipts use HTTP; production requests use the selected transport.
    const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
    const wait = async predicate => {
        for (let attempt = 0; attempt < 200; attempt++) {
            if (predicate()) return;
            await pause(100);
        }
        throw new Error('Timed out: ' + predicate.toString());
    };
    const set = async (selector, value) => {
        const input = document.querySelector(selector);
        if (!input) throw new Error('Missing ' + selector);
        input.value = value;
        input.dispatchEvent(new Event('change', { bubbles: true }));
        await pause(100);
    };
    const button = text => [...document.querySelectorAll('button')].find(node => node.textContent.trim() === text);
    try {
        await wait(() => document.querySelector('#start'));
        await pause(500);
        const titles = new Set();
        for (const language of ['af-ZA', 'xh-ZA', 'zu-ZA', 'tn-ZA', 'en-ZA']) {
            const previousTitle = document.querySelector('h1').textContent;
            await set('.appearance label:last-child select', language);
            await wait(() => document.documentElement.lang === language);
            await wait(() => document.querySelector('h1').textContent !== previousTitle);
            titles.add(document.querySelector('h1').textContent);
        }
        if (titles.size !== 5) throw new Error('Each language must render its own dashboard title');
        for (const mode of ['Dark', 'Light']) {
            await set('.system-theme', mode);
            await wait(() => document.documentElement.dataset.appTheme === 'Runninghill_' + mode.toLowerCase());
        }
        await set('#interval', '0');
        document.querySelector('#start').click();
        await wait(() => document.querySelector('[role="alert"]')?.textContent.includes('3,600'));
        await set('#interval', '1');
        await set('#service-url', base);
        await set('#website-url', base);
        await set('#token', 'dashboard-test-token');
        document.querySelector('#start').click();
        await wait(() => document.querySelector('#word-count').textContent === '37');
        if (document.querySelector('#sentence-count').textContent !== '8') throw new Error('Wrong sentence count');
        await wait(() => Number(document.querySelector('#check-count').textContent) >= 2);
        document.querySelector('#load-test').click();
        await wait(() => document.querySelector('.load').textContent.includes('24 requests; 0 failures'));
        if (document.querySelector('.gauge strong').textContent === '—') throw new Error('No measured gauge value');
        await set('.log-controls select', 'service');
        button('Refresh logs').click();
        await wait(() => document.querySelector('.log-list').textContent.includes('Latest service event'));
        button('Older events').click();
        await wait(() => document.querySelector('.log-list').textContent.includes('Older service event'));
        await fetch(control + '/control/fail');
        await wait(() => Number(document.querySelector('#failure-count').textContent) > 0);
        await wait(() => document.querySelector('#word-count').textContent === '—');
        button('Stop monitoring').click();
        await wait(() => !document.querySelector('#start').disabled);
        const count = document.querySelector('#check-count').textContent;
        await pause(1400);
        if (document.querySelector('#check-count').textContent !== count) throw new Error('Monitoring did not stop');
        if (document.documentElement.scrollWidth > innerWidth + 1) throw new Error('Horizontal overflow');
        await fetch(control + '/result', { method: 'POST', body: JSON.stringify({ passed: true }) });
    } catch (error) {
        await fetch(control + '/result', { method: 'POST', body: JSON.stringify({ passed: false, error: String(error) }) });
    }
})();
