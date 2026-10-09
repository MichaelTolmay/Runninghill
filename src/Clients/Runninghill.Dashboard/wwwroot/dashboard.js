// Store appearance only. Credentials never enter web storage or JavaScript.
window.dashboard = {
    language() {
        let language = 'en-ZA';
        try { language = localStorage.getItem('runninghill.dashboard.language') || language; } catch { }
        if (!['en-ZA', 'af-ZA', 'xh-ZA', 'zu-ZA', 'tn-ZA'].includes(language)) language = 'en-ZA';
        document.documentElement.lang = language;
        return language;
    },
    setLanguage(language) {
        document.documentElement.lang = language;
        try { localStorage.setItem('runninghill.dashboard.language', language); } catch { }
    }
};
