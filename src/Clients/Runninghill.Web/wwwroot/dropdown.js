// <details> owns whether a dropdown is open. Blazor owns the selected filters.
// Keep dismissal in the browser: it stays immediate and never starts an API request.
(() => {
    const openDropdowns = 'details[data-dismiss-dropdown][open]';

    // Record browser-owned interactions without reading input values or visible word text.
    document.addEventListener('toggle', event => {
        if (event.target instanceof HTMLDetailsElement) {
            console.info('[Runninghill.UI]', {
                event: event.target.hasAttribute('data-dismiss-dropdown') ? 'TypeFilterToggled' : 'ConnectionPanelToggled',
                open: event.target.open,
                timestamp: new Date().toISOString()
            });
        }
    }, true);
    window.addEventListener('pagehide', () => console.info('[Runninghill.UI]', { event: 'PageHidden' }));

    function close(dropdown, returnFocus = false) {
        dropdown.open = false;
        if (returnFocus) dropdown.querySelector('summary')?.focus();
    }

    // Wait for the click, not pointerdown: collapsing the inline phone menu on pointerdown
    // would move the button before release and swallow the user's click.
    // Do not cancel the event: that same click must still perform the outside action.
    document.addEventListener('click', event => {
        for (const dropdown of document.querySelectorAll(openDropdowns)) {
            if (!dropdown.contains(event.target)) close(dropdown);
        }
    }, true);

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        for (const dropdown of document.querySelectorAll(openDropdowns)) {
            close(dropdown, true);
            event.preventDefault();
        }
    });

    document.addEventListener('click', event => {
        const button = event.target.closest('[data-close-dropdown]');
        const dropdown = button?.closest('details[data-dismiss-dropdown]');
        if (dropdown) close(dropdown, true);
    });

    // Both Apply buttons submit the same filter form. Closing does not clear the checkboxes.
    document.addEventListener('submit', event => {
        for (const dropdown of event.target.querySelectorAll(openDropdowns)) close(dropdown);
    }, true);
})();
