(function (root, factory) {
    'use strict';
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else api.init(root.document, root);
})(typeof window === 'undefined' ? globalThis : window, function () {
    'use strict';
    const initialized = new WeakSet();

    function init(doc, win) {
        const card = doc.querySelector('[data-dashboard-slideshow]');
        const button = card?.querySelector('[data-slideshow-toggle]');
        if (!card || !button || initialized.has(card)) return;
        initialized.add(card);
        const label = button.querySelector('[data-slideshow-label]');
        const icon = button.querySelector('[data-slideshow-icon]');
        const motion = typeof win.matchMedia === 'function'
            ? win.matchMedia('(prefers-reduced-motion: reduce)') : null;
        let userPaused = false;

        function update() {
            card.setAttribute('data-slideshow-paused', String(userPaused || doc.hidden || motion?.matches === true));
            button.hidden = motion?.matches === true;
            const action = userPaused ? 'Play' : 'Pause';
            if (label) label.textContent = action;
            if (icon) icon.className = userPaused ? 'bi bi-play-fill' : 'bi bi-pause-fill';
            button.setAttribute('aria-label', action + ' background slideshow');
        }

        button.addEventListener('click', function () {
            if (motion?.matches) return;
            userPaused = !userPaused;
            update();
        });
        doc.addEventListener('visibilitychange', update);
        if (typeof motion?.addEventListener === 'function') motion.addEventListener('change', update);
        else if (typeof motion?.addListener === 'function') motion.addListener(update);
        update();
    }

    return { init };
});
