// This runs before Blazor so a failed download or runtime can still explain how to recover.
(() => {
    const messages = {
        'RH-WEB-START': 'Runninghill could not finish loading. Reload the page. If this continues, try a private browser window and share the diagnostic details with support.',
        'RH-WEB-ASSET': 'A file needed by this page could not be loaded. Check your connection and reload. If it continues, share the file name and diagnostic details with support.',
        'RH-WEB-UNSUPPORTED': 'This browser cannot run the app. Update your browser and make sure JavaScript and WebAssembly are allowed, then reload.',
        'RH-WEB-RENDER': 'This page could not be displayed.',
        'RH-WEB-RUNTIME': 'This page could not be displayed.'
    };
    let failure;
    const translate = text => window.runninghillLanguage?.text(text) || text;
    const safeName = value => /^[A-Za-z][A-Za-z0-9_.-]{0,120}$/.test(value || '') ? value : '';
    const fileName = value => {
        try { return safeName(new URL(value, location.href).pathname.split('/').pop()); }
        catch { return ''; }
    };

    function refresh() {
        const banner = document.getElementById('blazor-error-ui');
        if (!banner || !failure) return;
        let advice = translate(messages[failure.code]);
        if (failure.code === 'RH-WEB-RENDER' || failure.code === 'RH-WEB-RUNTIME')
            advice += ' ' + translate('Reload the page. Unsaved changes may be lost. Before repeating a save, check whether it already succeeded.');
        document.getElementById('startup-error').textContent = advice;
        // Codes and .NET/JavaScript type names stay stable across languages and can be searched verbatim.
        document.getElementById('startup-code').textContent = failure.code + ' · ' + failure.reference;
        document.getElementById('startup-details').textContent = JSON.stringify(failure, null, 2);
        document.getElementById('startup-copy').textContent = translate('Copy diagnostic details');
        document.getElementById('startup-details-label').textContent = translate('Diagnostic details');
        document.getElementById('startup-share').textContent = translate('Share these details with support. Do not include your access token.');
        banner.style.display = 'block';
    }

    function report(code, errorType = '', reference = '', asset = '') {
        // Keep the first failure: a subsequent startup rejection often only repeats a failed download.
        if (failure) return;
        failure = {
            code: Object.hasOwn(messages, code) ? code : 'RH-WEB-RUNTIME',
            reference: /^[a-zA-Z0-9-]{1,64}$/.test(reference) ? reference : (globalThis.crypto?.randomUUID?.() || Date.now().toString(36) + '-' + Math.random().toString(36).slice(2)),
            timeUtc: new Date().toISOString(),
            errorType: safeName(errorType),
            asset: fileName(asset),
            startupVersion: fileName(document.getElementById('startup-loader')?.src || ''),
            browser: navigator.userAgent.slice(0, 300),
            language: document.documentElement.lang,
            online: navigator.onLine,
            secureContext: window.isSecureContext
        };
        // No form values, URLs, raw exception messages, response bodies, or credentials enter this report.
        console.error('[Runninghill.Error]', failure);
        refresh();
    }

    window.runninghillErrors = { report, refresh };
    window.addEventListener('error', event => {
        if (event.target instanceof HTMLScriptElement || event.target instanceof HTMLLinkElement)
            report('RH-WEB-ASSET', '', '', event.target.src || event.target.href);
        else if (event instanceof ErrorEvent) report('RH-WEB-RUNTIME', event.error?.name);
    }, true);
    window.addEventListener('unhandledrejection', event => report('RH-WEB-RUNTIME', event.reason?.name));

    document.addEventListener('DOMContentLoaded', async () => {
        refresh();
        document.getElementById('startup-copy').addEventListener('click', async () => {
            try {
                await navigator.clipboard.writeText(JSON.stringify(failure, null, 2));
                document.getElementById('startup-copy-status').textContent = translate('Diagnostic details copied.');
            } catch {
                document.getElementById('startup-copy-status').textContent = translate('Could not copy automatically. Select and copy the diagnostic details below.');
                document.querySelector('#blazor-error-ui details').open = true;
            }
        });
        // Blazor can show its fatal banner without throwing a JavaScript error. Cover that path too.
        const banner = document.getElementById('blazor-error-ui');
        new MutationObserver(() => {
            if (!failure && banner.style.display !== 'none') report('RH-WEB-RUNTIME');
        }).observe(banner, { attributes: true, attributeFilter: ['style'] });
        if (typeof WebAssembly === 'undefined') { report('RH-WEB-UNSUPPORTED'); return; }
        if (!window.Blazor) { report('RH-WEB-ASSET', '', '', '_framework/blazor.webassembly.js'); return; }
        try { await Blazor.start(); }
        catch (error) { report('RH-WEB-START', error?.name); }
    });
})();
