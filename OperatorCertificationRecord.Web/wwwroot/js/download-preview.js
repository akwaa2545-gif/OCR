(function (root, factory) {
    const api = factory(root);
    if (typeof module === 'object' && module.exports) module.exports = api;
    else { root.DownloadPreview = api; root.validateDateRange = api.validateDateRange; }
})(typeof window === 'object' ? window : this, function (root) {
    'use strict';
    function validateDateRange(startId, endId, errId, previewBtnId, dlBtnId) {
        const document = root.document;
        const start = document.getElementById(startId), end = document.getElementById(endId);
        const error = document.getElementById(errId);
        if (!start || !end || !error) return true;
        const incomplete = Boolean(start.value) !== Boolean(end.value);
        const invalid = incomplete || Boolean(start.value && end.value && start.value > end.value);
        [start, end].forEach(field => field.classList[invalid ? 'add' : 'remove']('dm-invalid'));
        error.textContent = invalid ? (incomplete ? 'Please select both start and end dates.' : 'End date must be on or after start date.') : '';
        error.classList[invalid ? 'add' : 'remove']('visible');
        const preview = document.getElementById(previewBtnId), download = document.getElementById(dlBtnId);
        if (preview) preview.disabled = invalid;
        if (download) download.disabled = invalid || download.classList.contains('dl-pending');
        return !invalid;
    }
    function create(options) {
        let generation = 0, timer = null, openGeneration = 0, opened = false;
        const signature = () => JSON.stringify(options.filters.map(field => field ? [field.value, !!field.checked] : null));
        function invalidate() {
            generation++;
            if (timer !== null) clearTimeout(timer);
            timer = null;
            options.download.classList.add('dl-pending');
            if (options.onInvalidate) options.onInvalidate();
            if (options.disableDownload) options.download.disabled = true;
            options.badge.textContent = '—';
            options.loading.style.display = 'none';
            options.preview.disabled = false;
        }
        function begin() {
            invalidate();
            return { generation, signature: signature() };
        }
        function current(ticket) { return ticket.generation === generation && ticket.signature === signature(); }
        function complete(ticket) {
            if (!current(ticket)) return false;
            options.download.classList.remove('dl-pending');
            options.download.disabled = false;
            return true;
        }
        function schedule(callback) {
            invalidate();
            timer = setTimeout(function () { timer = null; callback(); }, 300);
        }
        function open() { opened = true; openGeneration++; invalidate(); return openGeneration; }
        function isOpen(version) { return opened && version === openGeneration; }
        function close() { opened = false; openGeneration++; invalidate(); }
        async function loadOptions(version, fields) {
            try {
                const response = await fetch('/api/download/meta/disqualified');
                if (!response.ok || !(response.headers.get('content-type') || '').toLowerCase().includes('application/json')) throw new Error('Filter list unavailable');
                const data = await response.json();
                if (!isOpen(version)) return;
                const escape = text => String(text).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
                fields.forEach(field => {
                    if (!field.element || !Array.isArray(data[field.key])) return;
                    const selected = field.element.value;
                    field.element.innerHTML = '<option value="">— Any —</option>' + data[field.key].map(value => `<option value="${escape(value)}">${escape(value)}</option>`).join('');
                    if (data[field.key].includes(selected)) field.element.value = selected;
                });
            } catch (error) {
                if (isOpen(version)) {
                    console.error('Download filter list could not load', error);
                    if (typeof window !== 'undefined' && window.Toast) window.Toast.warning('Filters unavailable', 'Showing the current report filters. Try reopening to reload the list.');
                }
            }
        }
        options.modal.addEventListener('hide.bs.modal', close);
        options.modal.addEventListener('hidden.bs.modal', close);
        return { begin, current, complete, invalidate, schedule, open, isOpen, loadOptions };
    }
    return { create, validateDateRange };
});
