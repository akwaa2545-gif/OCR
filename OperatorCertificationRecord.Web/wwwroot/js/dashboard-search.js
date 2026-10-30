(function () {
    'use strict';

    const initialized = new WeakSet();

    function photoUrl(value) {
        if (typeof value !== 'string' || !value || value.length > 2048 || /[\u0000-\u001f\u007f<>"']/.test(value)) return '';
        if (value.startsWith('/api/photo/')) {
            try {
                const url = new URL(value, 'https://dashboard.invalid');
                if (url.origin !== 'https://dashboard.invalid' || !url.pathname.startsWith('/api/photo/') || url.search || url.hash) return '';
                const segments = url.pathname.split('/');
                if (segments.some(function (segment) {
                    const decoded = decodeURIComponent(segment);
                    return decoded === '.' || decoded === '..' || /[\\/?#\u0000-\u001f\u007f<>"']/.test(decoded);
                })) return '';
                return url.pathname;
            } catch (_) { return ''; }
        }
        const name = value.replace(/\\/g, '/').split('/').pop();
        return name && name !== '.' && name !== '..' ? '/api/photo/' + encodeURIComponent(name) : '';
    }

    function init(document) {
        const input = document.getElementById('qs-input');
        const clearButton = document.getElementById('qs-clear-btn');
        const searchButton = document.getElementById('qs-search-btn');
        const spinner = document.getElementById('qs-spinner');
        const results = document.getElementById('qs-results-area');
        if (!input || !clearButton || !searchButton || !spinner || !results || initialized.has(input)) return;
        initialized.add(input);

        const view = document.defaultView;
        const hint = document.querySelector('.qs-hint');
        let revision = 0;
        let debounce = null;
        let controller = null;
        let pendingQuery = null;
        let successfulQuery = null;

        function element(tag, className, text) {
            const node = document.createElement(tag);
            if (className) node.className = className;
            if (text !== undefined) node.textContent = text;
            return node;
        }
        function icon(className) { const node = element('i', className); node.setAttribute('aria-hidden', 'true'); return node; }
        function text(value) { return typeof value === 'string' || typeof value === 'number' ? String(value) : ''; }
        function setLoading(loading) {
            spinner.style.display = loading ? 'block' : 'none';
            results.setAttribute('aria-busy', loading ? 'true' : 'false');
        }
        function cancelPending() {
            revision++;
            view.clearTimeout(debounce);
            debounce = null;
            if (controller) controller.abort();
            controller = null;
            pendingQuery = null;
            setLoading(false);
        }
        function syncClearButton() { clearButton.style.display = input.value.trim() ? 'inline-flex' : 'none'; }
        function message(iconClass, title, description, query) {
            const box = element('div', 'qs-empty');
            const image = element('div', 'qs-empty-icon'); image.appendChild(icon(iconClass)); box.appendChild(image);
            box.appendChild(element('div', 'qs-empty-title', title));
            const detail = element('div', 'qs-empty-sub');
            if (query !== undefined) {
                detail.appendChild(element('span', '', 'No match for '));
                detail.appendChild(element('strong', '', query));
                detail.appendChild(element('span', '', '. Try a different ID or name.'));
            } else detail.textContent = description;
            box.appendChild(detail);
            results.replaceChildren(box);
        }
        function chip(className, iconClass, value) {
            const node = element('span', className);
            node.appendChild(icon(iconClass)); node.appendChild(element('span', '', ' ' + value));
            return node;
        }
        function card(employee) {
            const exact = employee.isExact === true;
            const resigned = employee.isResigned === true;
            const code = text(employee.empCode);
            const first = text(employee.firstName); const last = text(employee.lastName);
            const initials = (first.charAt(0) + last.charAt(0)).toUpperCase() || '?';
            const node = element('div', 'qs-emp-card' + (exact ? ' qs-exact' : '') + (resigned ? ' qs-resigned' : ''));
            const top = element('div', 'qs-emp-top');
            const avatar = element('div', 'qs-emp-avatar', initials);
            avatar.setAttribute('data-ini', initials);
            const photo = photoUrl(employee.photoPath);
            if (photo) {
                const image = element('img'); image.src = photo; image.alt = '';
                image.style.width = '100%'; image.style.height = '100%'; image.style.objectFit = 'cover'; image.style.borderRadius = '50%';
                image.addEventListener('error', function () { avatar.textContent = initials; avatar.title = 'Photo unavailable'; });
                avatar.replaceChildren(image);
            }
            top.appendChild(avatar);
            const identity = element('div'); identity.style.minWidth = '0';
            identity.appendChild(element('div', 'qs-emp-name', [text(employee.prefixEng), first, last].filter(Boolean).join(' ')));
            const employeeCode = element('div', 'qs-emp-code');
            employeeCode.appendChild(icon('bi bi-hash')); employeeCode.appendChild(element('span', '', ' ' + code));
            identity.appendChild(employeeCode); top.appendChild(identity);
            if (exact) top.appendChild(element('span', 'qs-exact-tag', 'Exact'));
            if (resigned) top.appendChild(chip('qs-resigned-tag', 'bi bi-person-x', 'Resigned'));
            node.appendChild(top);
            const metadata = element('div', 'qs-emp-meta');
            if (text(employee.department)) metadata.appendChild(chip('qs-meta-chip', 'bi bi-building', text(employee.department)));
            if (text(employee.shift)) metadata.appendChild(chip('qs-meta-chip', 'bi bi-clock', text(employee.shift)));
            if (text(employee.jobGrade)) metadata.appendChild(element('span', 'qs-grade-chip', text(employee.jobGrade)));
            node.appendChild(metadata);
            const actions = element('div', 'qs-emp-actions');
            for (const action of [['View', '/ViewUser', 'qs-btn-view', 'bi bi-eye'], ['Edit', '/UpdateUser', 'qs-btn-edit', 'bi bi-pencil']]) {
                const link = element('a', action[2]); link.href = action[1] + '?empCode=' + encodeURIComponent(code);
                link.appendChild(icon(action[3])); link.appendChild(element('span', '', ' ' + action[0])); actions.appendChild(link);
            }
            node.appendChild(actions);
            return node;
        }
        function render(data, query) {
            if (data.length === 0) { message('bi bi-person-x', 'No employees found', '', query); return; }
            const count = element('div', 'qs-count-bar');
            const summary = element('div'); summary.style.display = 'flex'; summary.style.alignItems = 'center'; summary.style.gap = '0.5rem';
            summary.appendChild(element('span', 'qs-count-badge', String(data.length)));
            summary.appendChild(element('span', 'qs-count-text', 'result' + (data.length === 1 ? '' : 's') + ' for'));
            summary.appendChild(element('span', 'qs-query-pill', query)); count.appendChild(summary);
            const grid = element('div', 'qs-results-grid'); data.forEach(function (employee) { grid.appendChild(card(employee)); });
            results.replaceChildren(count, grid);
        }
        async function search(query) {
            query = query.trim();
            view.clearTimeout(debounce);
            if (query.length < 2) { cancelPending(); successfulQuery = null; results.replaceChildren(); return; }
            if (pendingQuery === query || successfulQuery === query) return;
            cancelPending();
            const token = revision;
            pendingQuery = query;
            const activeController = typeof view.AbortController === 'function' ? new view.AbortController() : null;
            controller = activeController;
            setLoading(true);
            function current() { return revision === token && input.value.trim() === query; }
            try {
                const response = await view.fetch('/Dashboard?handler=Search&q=' + encodeURIComponent(query), {
                    signal: activeController ? activeController.signal : undefined,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });
                if (!current()) return;
                if (!response.ok) {
                    if (response.status === 401) { message('bi bi-box-arrow-in-right', 'Please sign in again', 'Your session has expired. Sign in to search employees.'); return; }
                    if (response.status === 403) { message('bi bi-shield-lock', 'Search access denied', 'You do not have access to employee search.'); return; }
                    throw new Error('Employee search unavailable.');
                }
                const data = await response.json();
                if (!current()) return;
                if (!Array.isArray(data) || data.some(function (employee) { return !employee || typeof employee !== 'object' || Array.isArray(employee); }))
                    throw new Error('Invalid employee search response.');
                render(data, query);
                successfulQuery = query;
            } catch (error) {
                if (!current() || error && error.name === 'AbortError') return;
                successfulQuery = null;
                message('bi bi-wifi-off', 'Search failed', 'Could not reach the server. Please try again.');
            } finally {
                if (current()) { controller = null; pendingQuery = null; setLoading(false); }
            }
        }

        input.addEventListener('input', function () {
            cancelPending(); successfulQuery = null; syncClearButton();
            const query = input.value.trim();
            if (query.length < 2) { results.replaceChildren(); return; }
            const token = revision;
            debounce = view.setTimeout(function () { if (revision === token) search(query); }, 320);
        });
        input.addEventListener('keydown', function (event) {
            if (event.key === 'Enter') { event.preventDefault(); search(input.value); }
        });
        clearButton.addEventListener('click', function () {
            cancelPending(); successfulQuery = null; input.value = ''; syncClearButton(); results.replaceChildren(); input.focus();
        });
        searchButton.addEventListener('click', function () { search(input.value); });
        if (hint) ['E001', 'E002', 'John'].forEach(function (value) {
            const button = element('button', 'qs-hint-pill', value); button.type = 'button';
            button.addEventListener('click', function () { input.value = value; successfulQuery = null; syncClearButton(); search(value); });
            hint.appendChild(button);
        });
        syncClearButton();
        setLoading(false);
    }

    if (typeof module !== 'undefined' && module.exports) module.exports = { init: init };
    if (typeof document !== 'undefined') {
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { init(document); });
        else init(document);
    }
})();
