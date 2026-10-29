(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.ReportDateRanges = api;
})(typeof window === 'object' ? window : this, function () {
    'use strict';
    const initialized = new WeakSet();
    function formatLocal(date) {
        return date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0') + '-' + String(date.getDate()).padStart(2, '0');
    }
    function currentYear(now = new Date()) {
        return { start: now.getFullYear() + '-01-01', end: formatLocal(now) };
    }
    function apply(start, end, preset, now = new Date()) {
        if (!start || !end) return;
        let range;
        if (preset === 'year') range = currentYear(now);
        else if (Number(preset) === 0) range = { start: '2000-01-01', end: '2099-12-31' };
        else {
            const first = new Date(now.getTime());
            first.setDate(now.getDate() - (Number(preset) - 1));
            range = { start: formatLocal(first), end: formatLocal(now) };
        }
        start.value = range.start;
        end.value = range.end;
        return range;
    }
    function initialize(start, end, owner) {
        if (!start || !end || initialized.has(start)) return;
        initialized.add(start);
        apply(start, end, 'year');
        if (owner) owner.querySelectorAll('button[data-days]').forEach(button => {
            button.classList.toggle('active', button.getAttribute('data-days') === 'year');
        });
    }
    return { formatLocal, currentYear, apply, initialize };
});
