(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else { root.ModalLayer = api; api.init(root.document); }
})(typeof window === 'object' ? window : this, function () {
    'use strict';
    const initialized = new WeakSet();
    function init(document) {
        if (!document || initialized.has(document)) return;
        initialized.add(document);
        // Establish the final containing block before Bootstrap starts its transition.
        document.addEventListener('show.bs.modal', function (event) {
            const modal = event.target;
            if (!modal || !modal.classList || !modal.classList.contains('modal')) return;
            if (modal.parentElement !== document.body) document.body.appendChild(modal);
            modal.style.zIndex = '10050';
        });
        document.addEventListener('hidden.bs.modal', function () {
            if (document.body.classList.contains('modal-open') || document.querySelector('.modal.show')) return;
            document.querySelectorAll('.modal-backdrop').forEach(backdrop => backdrop.remove());
        });
    }
    return { init };
});
