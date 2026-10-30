(function (root, factory) {
    'use strict';
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else {
        root.DashboardExpiry = api;
        const start = function () { api.init(root.document, root); };
        if (root.document.readyState === 'loading') root.document.addEventListener('DOMContentLoaded', start, { once: true });
        else start();
    }
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';
    const instances = new WeakMap();
    const pageSize = 20;
    const maxRows = 1000;

    function calendarDate(value) {
        if (typeof value !== 'string') return '';
        const match = /^(\d{4})-(\d{2})-(\d{2})(?:$|T|\s)/.exec(value);
        if (!match) return '';
        const date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
        return date.getUTCFullYear() === Number(match[1]) && date.getUTCMonth() + 1 === Number(match[2]) && date.getUTCDate() === Number(match[3]) ? match.slice(1).join('-') : '';
    }

    function expiryState(value, now = new Date()) {
        const date = calendarDate(value);
        if (!date) return { date: '', text: '', badge: '', className: '' };
        const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Bangkok', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
        const part = name => parts.find(item => item.type === name).value;
        const today = Date.UTC(Number(part('year')), Number(part('month')) - 1, Number(part('day')));
        const day = Date.parse(date + 'T00:00:00Z');
        const diff = Math.round((day - today) / 86400000);
        const className = diff < 0 ? 'expiry-expired' : diff <= 10 ? 'expiry-critical' : diff <= 30 ? 'expiry-soon' : '';
        const badge = diff < 0 ? 'Expired' : diff === 0 ? 'Today' : diff <= 30 ? 'in ' + diff + 'd' : '';
        return { date, text: new Date(day).toLocaleDateString('en-GB', { timeZone: 'UTC', day: '2-digit', month: 'short', year: 'numeric' }), badge, className };
    }

    function normalize(item) {
        const text = (upper, lower) => String(item[upper] ?? item[lower] ?? '');
        return { EmpCode: text('EmpCode', 'empCode'), FirstNameEng: text('FirstNameEng', 'firstNameEng'), LastNameEng: text('LastNameEng', 'lastNameEng'), Process: text('Process', 'process'), ExpiryDate: text('ExpiryDate', 'expiryDate') };
    }

    function init(document, view) {
        if (!document || !view) return;
        if (instances.has(document)) return instances.get(document);
        const tbody = document.getElementById('expiringPreviewBody');
        const modal = document.getElementById('expiringModal');
        if (!tbody || !modal) return;
        const byId = id => document.getElementById(id);
        const buttons = ['expiringLoadMoreBtn', 'expiringLoadMoreFooterBtn'].map(byId).filter(Boolean);
        let items = Array.isArray(view.expiringItems) ? view.expiringItems.filter(item => item && typeof item === 'object').slice(0, maxRows).map(normalize) : [];
        let complete = false;
        let pending = null;
        let loading = false;
        let employeeBackdrop = null;
        let returnToParent = false;
        let switchingModals = false;
        let returnFocus = null;
        let error = byId('expiringLoadMoreError');
        if (!error) {
            error = document.createElement('p'); error.id = 'expiringLoadMoreError'; error.className = 'text-danger small';
            error.setAttribute('role', 'status');
            (modal.querySelector('.modal-body') || modal).appendChild(error);
        }

        function element(tag, className, text) {
            const node = document.createElement(tag);
            if (className) node.className = className;
            if (text !== undefined) node.textContent = text;
            return node;
        }

        function setExpiry(node, value) {
            const state = expiryState(value);
            node.className = 'expiry-date' + (state.className ? ' ' + state.className : '');
            node.textContent = state.text;
            node.setAttribute('data-expiry', state.date);
            if (state.badge) node.appendChild(element('span', 'expiry-badge', state.badge));
        }

        function expiryCell(value) {
            const cell = element('td', 'expiry-cell');
            const date = element('span', 'expiry-date'); setExpiry(date, value); cell.appendChild(date);
            return cell;
        }

        function renderRows(count) {
            const rows = items.slice(0, count).map(item => {
                const row = element('tr'); row.style.cursor = 'pointer'; row.setAttribute('data-empcode', item.EmpCode);
                const name = [item.FirstNameEng, item.LastNameEng].filter(Boolean).join(' ') || item.EmpCode;
                [item.EmpCode, name, item.Process].forEach(text => row.appendChild(element('td', '', text)));
                row.appendChild(expiryCell(item.ExpiryDate));
                const actions = element('td'); actions.style.textAlign = 'right';
                const link = element('a', 'btn btn-sm btn-expiring', 'View'); link.href = '/ViewUser?empCode=' + encodeURIComponent(item.EmpCode);
                actions.appendChild(link); row.appendChild(actions);
                return row;
            });
            tbody.replaceChildren(...rows);
        }

        function updateInfo() {
            const current = tbody.children.length;
            const capped = !complete && current >= maxRows;
            const message = complete ? 'Showing ' + current + ' of ' + items.length + ' results.' :
                capped ? 'Showing the first ' + current + ' results. View full list for more.' : 'Showing ' + current + ' results. Load more to check for additional items.';
            ['expiringPreviewInfo', 'expiringPreviewFooterInfo'].map(byId).filter(Boolean).forEach(node => { node.textContent = message; });
            ['expiringLoadMoreCount', 'expiringLoadMoreFooterCount'].map(byId).filter(Boolean).forEach(node => { node.textContent = ''; });
            const done = complete && current >= items.length || capped;
            const wrap = byId('expiringLoadMoreWrap'); if (wrap) wrap.style.display = done ? 'none' : 'flex';
            if ((loading || done) && buttons.includes(document.activeElement)) {
                (modal.querySelector('.expiring-close') || modal).focus({ preventScroll: true });
            }
            buttons.forEach(button => {
                button.disabled = loading || done;
                if (loading) button.setAttribute('aria-busy', 'true'); else button.removeAttribute('aria-busy');
            });
            ['expiringLoadMoreSpinner', 'expiringLoadMoreFooterSpinner'].map(byId).filter(Boolean).forEach(node => { node.style.display = loading ? 'inline-block' : 'none'; });
        }

        function loadMore() {
            if (pending) return pending;
            if (complete || tbody.children.length >= maxRows) return Promise.resolve();
            const target = Math.min(maxRows, Math.max(pageSize, tbody.children.length + pageSize));
            loading = true; error.textContent = ''; updateInfo();
            pending = (async function () {
                try {
                    const response = await view.fetch('/api/download/expiry/preview?days=30&top=' + target, { headers: { Accept: 'application/json' } });
                    if (!response.ok) throw new Error(response.status === 401 || response.status === 403 ? 'signin' : 'unavailable');
                    const data = await response.json();
                    if (!Array.isArray(data) || data.some(item => !item || typeof item !== 'object' || Array.isArray(item))) throw new Error('unavailable');
                    items = data.slice(0, target).map(normalize);
                    complete = data.length < target;
                    view.expiringItems = items;
                    renderRows(target);
                } catch (failure) {
                    error.textContent = failure?.message === 'signin' ? 'Please sign in again to load more items.' : 'Could not load more items. Please try again.';
                } finally {
                    loading = false; pending = null; updateInfo();
                }
            }());
            return pending;
        }

        function highlightExpiryDatesInModal() {
            modal.querySelectorAll('.expiry-date').forEach(node => setExpiry(node, node.getAttribute('data-expiry')));
        }

        function openEmployeeExpiries(employeeCode, sourceRow) {
            const employee = byId('employeeExpiriesModal'); if (!employee || !employeeCode) return;
            if (switchingModals) return;
            const matching = items.filter(item => item.EmpCode === String(employeeCode));
            const title = employee.querySelector('.modal-title');
            if (title) title.textContent = 'Expiring items for ' + employeeCode + ' (' + matching.length + ' loaded)';
            const link = byId('employeeProfileLink'); if (link) link.href = '/ViewUser?empCode=' + encodeURIComponent(employeeCode);
            const body = employee.querySelector('.modal-body'); if (!body) return;
            if (!matching.length) body.replaceChildren(element('p', 'text-muted', 'No loaded expiring items found for this employee.'));
            else {
                const wrap = element('div', 'table-responsive'); const table = element('table', 'table table-sm');
                const head = element('thead'); const heading = element('tr');
                heading.appendChild(element('th', '', 'Process')); heading.appendChild(element('th', 'expiry-col', 'Expiry Date')); head.appendChild(heading); table.appendChild(head);
                const rows = element('tbody');
                matching.forEach(item => { const row = element('tr'); row.appendChild(element('td', '', item.Process)); row.appendChild(expiryCell(item.ExpiryDate)); rows.appendChild(row); });
                table.appendChild(rows); wrap.appendChild(table); body.replaceChildren(wrap);
            }
            if (employee.parentElement !== document.body) document.body.appendChild(employee);
            const showEmployee = function () {
                employee.style.zIndex = '2150';
                if (view.bootstrap?.Modal) view.bootstrap.Modal.getOrCreateInstance(employee).show();
                else employee.style.display = 'block';
            };
            if (view.bootstrap?.Modal && modal.classList.contains('show')) {
                returnToParent = true;
                returnFocus = sourceRow || Array.from(tbody.querySelectorAll('tr')).find(row => row.getAttribute('data-empcode') === String(employeeCode));
                switchingModals = true;
                const hideParent = function () { view.bootstrap.Modal.getOrCreateInstance(modal).hide(); };
                modal.addEventListener('shown.bs.modal', hideParent, { once: true });
                modal.addEventListener('hidden.bs.modal', function () {
                    modal.removeEventListener('shown.bs.modal', hideParent);
                    switchingModals = false;
                    showEmployee();
                }, { once: true });
                hideParent();
            } else showEmployee();
        }

        buttons.forEach(button => button.addEventListener('click', event => { event.preventDefault(); loadMore(); }));
        tbody.addEventListener('click', event => {
            if (event.target.closest('a, button')) return;
            const row = event.target.closest('tr[data-empcode]');
            if (row) openEmployeeExpiries(row.getAttribute('data-empcode'), row);
        });
        modal.addEventListener('shown.bs.modal', highlightExpiryDatesInModal);
        const employeeModal = byId('employeeExpiriesModal');
        if (employeeModal) {
            employeeModal.addEventListener('shown.bs.modal', function () {
                const backdrops = document.querySelectorAll('.modal-backdrop.show');
                employeeBackdrop = backdrops[backdrops.length - 1] || null;
                if (employeeBackdrop) {
                    employeeBackdrop.classList.add('employee-modal-backdrop');
                    employeeBackdrop.style.zIndex = '2140';
                }
            });
            employeeModal.addEventListener('hidden.bs.modal', function () {
                if (employeeBackdrop) {
                    employeeBackdrop.classList.remove('employee-modal-backdrop');
                    employeeBackdrop.style.zIndex = '';
                    employeeBackdrop = null;
                }
                employeeModal.style.zIndex = '';
                if (returnToParent && view.bootstrap?.Modal) {
                    returnToParent = false;
                    const target = returnFocus;
                    returnFocus = null;
                    if (target) modal.addEventListener('shown.bs.modal', function () {
                        if (target.isConnected !== false) {
                            target.tabIndex = -1;
                            target.focus({ preventScroll: true });
                        }
                    }, { once: true });
                    if (modal.parentElement !== document.body) document.body.appendChild(modal);
                    view.bootstrap.Modal.getOrCreateInstance(modal).show();
                }
            });
        }
        view.openEmployeeExpiries = openEmployeeExpiries;
        view.highlightExpiryDatesInModal = highlightExpiryDatesInModal;
        const controller = { loadMore };
        instances.set(document, controller);
        renderRows(Math.min(pageSize, items.length)); updateInfo();
        return controller;
    }
    return { init, expiryState };
}));
