(function () {
    'use strict';

    function normalize(value) {
        return String(value || '').normalize('NFC').trim().toLocaleLowerCase();
    }

    function matchWorkshop(name, processNames, query) {
        const term = normalize(query);
        const workshopMatches = !term || normalize(name).includes(term);
        const processes = processNames.map(name => workshopMatches || normalize(name).includes(term));
        return { visible: workshopMatches || processes.some(Boolean), processes: processes };
    }

    function updateMode(form, confirmation, resetConfirmation) {
        const selected = form.querySelector('[data-catalog-mode]:checked');
        if (!selected) return;
        if (resetConfirmation && confirmation) confirmation.checked = false;
        form.querySelectorAll('[data-catalog-mode-fields]').forEach(function (group) {
            const active = group.dataset.catalogModeFields === selected.value;
            group.hidden = !active;
            group.querySelectorAll('input:not([type="hidden"]), select').forEach(function (field) {
                field.disabled = !active;
                field.required = active;
            });
        });
        const label = form.querySelector('[data-catalog-submit-label]');
        if (label) label.textContent = selected.value === 'move' ? 'Move process' : 'Add process';
    }

    function updateMoveSummary(doc) {
        const process = doc.getElementById('catalog-process-existing');
        const destination = doc.getElementById('catalog-process-workshop');
        const summary = doc.getElementById('catalog-move-summary');
        if (!process || !destination || !summary) return;
        const sourceOption = process.selectedOptions[0];
        const destinationOption = destination.selectedOptions[0];
        if (!process.value || !destination.value || !sourceOption || !destinationOption) {
            summary.textContent = 'Choose a process and destination to see the move.';
            return;
        }
        summary.textContent = sourceOption.dataset.processName + ': ' +
            sourceOption.dataset.sourceWorkshop + ' \u2192 ' + destinationOption.textContent.trim();
    }

    function initBrowser(browser, doc) {
        const search = doc.getElementById('catalog-search');
        const status = doc.getElementById('catalog-search-status');
        const empty = doc.getElementById('catalog-no-results');
        const clear = browser.querySelector('[data-catalog-clear]');
        const expand = browser.querySelector('[data-catalog-expand]');
        const groups = Array.from(browser.querySelectorAll('[data-catalog-workshop]'));
        if (!search) return;
        let beforeSearch = null;

        function updateExpandLabel() {
            const visible = groups.filter(group => !group.hidden);
            if (expand) {
                expand.textContent = visible.length && visible.every(group => group.open) ? 'Collapse all' : 'Expand all';
                expand.disabled = visible.length === 0;
            }
        }

        function filter() {
            const searching = !!normalize(search.value);
            if (searching && !beforeSearch) beforeSearch = groups.map(group => group.open);
            let visibleGroups = 0;
            let workshopCount = 0;
            let processCount = 0;
            groups.forEach(function (group, index) {
                const rows = Array.from(group.querySelectorAll('[data-catalog-process]'));
                const match = matchWorkshop(group.dataset.workshopName,
                    rows.map(row => (row.dataset.processName || '') + ' ' + (row.dataset.processId || '')), search.value);
                group.hidden = !match.visible;
                rows.forEach(function (row, rowIndex) { row.hidden = !match.processes[rowIndex]; });
                if (match.visible) {
                    visibleGroups++;
                    if (group.dataset.catalogUnassigned === undefined) workshopCount++;
                    processCount += match.processes.filter(Boolean).length;
                }
                if (searching && match.visible) group.open = true;
                else if (!searching && beforeSearch) group.open = beforeSearch[index];
            });
            if (!searching) beforeSearch = null;
            if (status) status.textContent = workshopCount + (workshopCount === 1 ? ' workshop' : ' workshops') +
                ' \u00b7 ' + processCount + (processCount === 1 ? ' process' : ' processes') +
                (searching ? ' found' : ' in the catalog');
            if (empty) empty.hidden = visibleGroups !== 0 || !searching;
            if (clear) clear.hidden = !searching;
            updateExpandLabel();
        }

        search.addEventListener('input', filter);
        if (clear) clear.addEventListener('click', function () {
            search.value = '';
            filter();
            search.focus();
        });
        if (expand) expand.addEventListener('click', function () {
            const visible = groups.filter(group => !group.hidden);
            const open = !visible.every(group => group.open);
            visible.forEach(function (group) { group.open = open; });
            updateExpandLabel();
        });
        groups.forEach(function (group) { group.addEventListener('toggle', updateExpandLabel); });
        filter();
    }

    function init(doc) {
        const confirmation = doc.getElementById('catalog-confirm-move');
        const forms = Array.from(doc.querySelectorAll('[data-catalog-form]'));
        forms.forEach(function (form) {
            form.querySelectorAll('[data-catalog-mode]').forEach(function (input) {
                input.addEventListener('change', function () {
                    updateMode(form, confirmation, true);
                    updateMoveSummary(doc);
                });
            });
            updateMode(form, confirmation, false);
        });
        ['catalog-process-existing', 'catalog-process-workshop'].forEach(function (id) {
            const field = doc.getElementById(id);
            if (field) field.addEventListener('change', function () {
                if (confirmation) confirmation.checked = false;
                updateMoveSummary(doc);
            });
        });
        doc.querySelectorAll('[data-catalog-add-to], [data-catalog-move-process]').forEach(function (button) {
            button.addEventListener('click', function () {
                const destination = doc.getElementById('catalog-process-workshop');
                const existing = doc.getElementById('catalog-process-existing');
                const moving = button.hasAttribute('data-catalog-move-process');
                if (!destination || !existing || !forms.length) return;
                if (moving) existing.value = button.dataset.catalogMoveProcess;
                else destination.value = button.dataset.catalogAddTo;
                forms.forEach(function (form) {
                    form.querySelectorAll('[data-catalog-mode]').forEach(function (radio) {
                        radio.checked = radio.value === (moving ? 'move' : 'new');
                    });
                    updateMode(form, confirmation, true);
                });
                updateMoveSummary(doc);
                const focusTarget = moving ? destination : doc.getElementById('catalog-process-name');
                if (focusTarget) focusTarget.focus();
            });
        });
        doc.querySelectorAll('[data-catalog-browser]').forEach(function (browser) { initBrowser(browser, doc); });
        updateMoveSummary(doc);
    }

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { matchWorkshop: matchWorkshop, updateMode: updateMode, initBrowser: initBrowser, init: init };
    }
    if (typeof document !== 'undefined') {
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { init(document); });
        else init(document);
    }
})();
