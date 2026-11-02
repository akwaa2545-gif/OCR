(function () {
    'use strict';
    const form = document.getElementById('bulk-employee-form');
    if (!form) return;
    const toggle = document.getElementById('bulk-select-page');
    const status = document.getElementById('bulk-selection-count');
    const rows = Array.from(form.querySelectorAll('input[name="SelectedCodes"]:not(:disabled)'));
    function sync() {
        const selected = rows.filter(row => row.checked).length;
        if (toggle) { toggle.checked = rows.length > 0 && selected === rows.length; toggle.indeterminate = selected > 0 && selected < rows.length; toggle.disabled = rows.length === 0; }
        if (status) status.textContent = selected + ' selected on this page. You can also enter employee IDs below. Maximum 100 per update.';
    }
    if (toggle) toggle.addEventListener('change', function () { rows.forEach(row => { row.checked = toggle.checked; }); sync(); });
    rows.forEach(row => row.addEventListener('change', sync));
    sync();
})();
