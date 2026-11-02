(function () {
    'use strict';

    function init(document) {
        const dialog = document.getElementById('activity-dialog');
        if (!dialog) return;
        const view = document.defaultView;
        Array.from(dialog.querySelectorAll('[data-activity-close]')).forEach(function (button) {
            button.addEventListener('click', function () { dialog.close(); });
        });
        dialog.addEventListener('close', function () {
            document.documentElement.classList.remove('activity-modal-open');
            if (view?.history && view.location) {
                const url = new URL(view.location.href);
                Array.from(url.searchParams.keys()).forEach(function (key) {
                    if (key.toLowerCase() === 'eventid') url.searchParams.delete(key);
                });
                view.history.replaceState(view.history.state, '', url.href);
            }
            const origin = document.getElementById(dialog.dataset.returnFocus) || document.getElementById('activity-heading');
            if (origin) origin.focus();
        });
        function open() {
            if (typeof dialog.showModal === 'function') {
                if (dialog.open) dialog.removeAttribute('open');
                dialog.showModal();
            }
            document.documentElement.classList.add('activity-modal-open');
            const title = document.getElementById('activity-dialog-title');
            if (title) title.focus();
        }
        if (dialog.open) open();
        view?.addEventListener('pageshow', function () {
            if (dialog.open) {
                document.documentElement.classList.add('activity-modal-open');
                if (typeof dialog.matches === 'function' && !dialog.matches(':modal')) open();
            } else document.documentElement.classList.remove('activity-modal-open');
        });
    }

    if (typeof module !== 'undefined' && module.exports) module.exports = { init: init };
    if (typeof document !== 'undefined') {
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { init(document); });
        else init(document);
    }
})();
