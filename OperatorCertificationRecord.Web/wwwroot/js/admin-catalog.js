(function () {
    'use strict';

    function updateMode(form) {
        const selected = form.querySelector('[data-catalog-mode]:checked');
        if (!selected) return;
        form.querySelectorAll('[data-catalog-mode-fields]').forEach(function (group) {
            const active = group.dataset.catalogModeFields === selected.value;
            group.hidden = !active;
            group.querySelectorAll('input:not([type="hidden"]), select').forEach(function (field) {
                field.disabled = !active;
                field.required = active;
            });
        });
    }

    function updateMoveSummary() {
        const process = document.getElementById('catalog-process-existing');
        const destination = document.getElementById('catalog-process-workshop');
        const summary = document.getElementById('catalog-move-summary');
        if (!process || !destination || !summary) return;
        const sourceOption = process.selectedOptions[0];
        const destinationOption = destination.selectedOptions[0];
        if (!process.value || !destination.value) {
            summary.textContent = 'Choose a process and destination to see the move.';
            return;
        }
        summary.textContent = sourceOption.dataset.processName + ': ' +
            sourceOption.dataset.sourceWorkshop + ' → ' + destinationOption.textContent.trim();
    }

    function init() {
        document.querySelectorAll('[data-catalog-form]').forEach(function (form) {
            form.querySelectorAll('[data-catalog-mode]').forEach(function (input) {
                input.addEventListener('change', function () {
                    updateMode(form);
                    updateMoveSummary();
                });
            });
            updateMode(form);
        });
        ['catalog-process-existing', 'catalog-process-workshop'].forEach(function (id) {
            const field = document.getElementById(id);
            if (field) field.addEventListener('change', function () {
                const confirmation = document.getElementById('catalog-confirm-move');
                if (confirmation) confirmation.checked = false;
                updateMoveSummary();
            });
        });
        updateMoveSummary();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
