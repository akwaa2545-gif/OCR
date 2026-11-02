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
        if (!card || initialized.has(card)) return;
        initialized.add(card);
        const motion = typeof win.matchMedia === 'function'
            ? win.matchMedia('(prefers-reduced-motion: reduce)') : null;

        function update() {
            card.setAttribute('data-slideshow-paused', String(doc.hidden || motion?.matches === true));
        }

        doc.addEventListener('visibilitychange', update);
        if (typeof motion?.addEventListener === 'function') motion.addEventListener('change', update);
        else if (typeof motion?.addListener === 'function') motion.addListener(update);
        update();
    }

    return { init };
});
