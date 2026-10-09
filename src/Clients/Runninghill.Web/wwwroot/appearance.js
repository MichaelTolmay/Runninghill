// Run before Blazor starts so the loading screen follows the saved brightness too.
(() => {
    const media = matchMedia('(prefers-color-scheme: dark)');
    let mode = 'System', company = 'Runninghill';
    try {
        const saved = JSON.parse(localStorage.getItem('runninghill.appearance') || '{}');
        mode = ['Light', 'Dark', 'System'].includes(saved.mode) ? saved.mode : 'System';
        company = /^[A-Za-z0-9_-]+$/.test(saved.company || '') ? saved.company : 'Runninghill';
    } catch { /* Damaged or restricted storage must not prevent startup. */ }
    const brightness = () => mode === 'Dark' || mode === 'System' && media.matches ? 'dark' : 'light';
    const applyBrightness = () => {
        document.documentElement.dataset.brightness = brightness();
        document.documentElement.style.colorScheme = brightness();
    };
    applyBrightness();
    window.runninghillTheme = {
        state: () => JSON.stringify({ mode, company, systemDark: media.matches }),
        apply: (selectedMode, selectedCompany, id, paletteJson) => {
            mode = selectedMode; company = selectedCompany;
            const palette = JSON.parse(paletteJson);
            for (const [role, color] of Object.entries(palette)) {
                // Only trusted, plain colour roles enter inline styles, even if a caller is changed later.
                if (/^[A-Za-z]+$/.test(role) && /^#[a-f\d]{6}$/i.test(color))
                    document.documentElement.style.setProperty('--' + role, color);
            }
            document.documentElement.dataset.appTheme = id;
            applyBrightness();
            try { localStorage.setItem('runninghill.appearance', JSON.stringify({ mode, company })); }
            catch { /* The current session still keeps its choice. */ }
        }
    };
    media.addEventListener('change', () => {
        if (mode !== 'System') return;
        const selector = document.querySelector('.system-theme');
        // Let the component select the same company's matching palette as well as repainting the page.
        if (selector) selector.dispatchEvent(new Event('change', { bubbles: true }));
        else applyBrightness();
    });
})();
