(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else {
        root.ViewUserSkillScroll = api;
        if (root.document.readyState === 'loading') root.document.addEventListener('DOMContentLoaded', () => api.init(root.document, root));
        else api.init(root.document, root);
    }
})(typeof window === 'undefined' ? null : window, function () {
    'use strict';
    const controllers = new WeakMap();

    function availableHeight(viewportHeight, top) {
        return Math.max(96, Math.min(560, Math.floor(viewportHeight - Math.max(0, top) - 24)));
    }

    function resizedColumnWidth(requested, initialWidth, minimum = 40) {
        return Math.max(Math.min(initialWidth, minimum), Math.min(640, Math.round(requested)));
    }

    function setFrozenColumns(table, state) {
        const selection = state.headers.findIndex(header => header.dataset?.columnKey === 'selection');
        const process = state.headers.findIndex(header => header.dataset?.columnKey === 'process');
        table.style.setProperty('--vu-selection-width', selection < 0 ? '0px' : state.widths[selection] + 'px');
        Array.from(table.rows).forEach(row => {
            if (selection >= 0) row.cells[selection]?.classList.add('vu-frozen-selection');
            if (process >= 0) row.cells[process]?.classList.add('vu-frozen-process');
        });
    }

    function columnPercentages(labels) {
        const weights = labels.map(label => {
            const text = label.trim().toLowerCase();
            if (text === 'selection') return 3;
            if (text === 'process') return 19;
            if (text === 'k' || text === 's') return 3;
            if (/classification|verifier|disqualified by/.test(text)) return 11;
            if (/reason|remark/.test(text)) return 10;
            if (/date|theory|^ojt$/.test(text)) return 8;
            return /result/.test(text) ? 5 : 7;
        });
        const total = weights.reduce((sum, weight) => sum + weight, 0);
        return weights.map(weight => weight / total * 100);
    }

    function prepareFitColumns(table) {
        const headers = Array.from(table.querySelectorAll('thead tr:first-child th'));
        let group = table.querySelector('colgroup');
        if (!group) {
            group = table.ownerDocument.createElement('colgroup');
            table.insertBefore(group, table.querySelector('thead'));
        }
        while (group.children.length < headers.length) group.appendChild(table.ownerDocument.createElement('col'));
        const percentages = columnPercentages(headers.map(header => header.dataset.columnKey === 'selection' ? 'selection' : header.textContent));
        headers.forEach((header, index) => {
            header.scope = 'col';
            if (!header.title) header.title = header.textContent.trim();
            group.children[index].style.setProperty('--vu-fit-width', percentages[index] + '%');
        });
        table.querySelectorAll('tbody td').forEach(cell => { if (!cell.title) cell.title = cell.textContent.trim(); });
    }

    function setFitMode(wrappers, button, enabled) {
        wrappers.forEach(wrapper => {
            wrapper.classList.toggle('skill-table-fit', enabled);
            if (enabled) wrapper.scrollLeft = 0;
        });
        if (!button) return;
        button.classList.toggle('fit-active', enabled);
        button.setAttribute('aria-pressed', String(enabled));
        button.title = enabled ? 'Expand columns for larger text and resizing' : 'Fit all columns on screen';
        const label = button.querySelector('span');
        if (label) label.textContent = enabled ? 'Expand' : 'Fit';
        const icon = button.querySelector('i');
        if (icon) icon.className = enabled ? 'bi bi-arrows-angle-expand' : 'bi bi-arrows-angle-contract';
    }

    function setDensity(page, buttons, value, storage) {
        const density = value === 'compact' ? 'compact' : 'comfortable';
        if (page) page.dataset.tableDensity = density;
        buttons.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.tableDensity === density)));
        try { storage?.setItem('OTD.skillTableDensity', density); } catch { /* Optional preference storage may be blocked. */ }
    }

    function attachHorizontalScroll(wrapper) {
        const doc = wrapper.ownerDocument;
        const top = doc.createElement('div');
        const spacer = doc.createElement('div');
        top.className = 'vu-table-top-scroll';
        top.hidden = true;
        top.tabIndex = 0;
        top.setAttribute('role', 'region');
        top.setAttribute('aria-label', 'Scroll columns: ' + wrapper.getAttribute('aria-label'));
        spacer.setAttribute('aria-hidden', 'true');
        top.appendChild(spacer);
        wrapper.parentNode.insertBefore(top, wrapper);
        top.addEventListener('scroll', () => { if (wrapper.scrollLeft !== top.scrollLeft) wrapper.scrollLeft = top.scrollLeft; }, { passive: true });
        wrapper.addEventListener('scroll', () => { if (top.scrollLeft !== wrapper.scrollLeft) top.scrollLeft = wrapper.scrollLeft; }, { passive: true });
        return () => {
            top.hidden = wrapper.classList.contains('skill-table-fit') || wrapper.scrollWidth <= wrapper.clientWidth + 1;
            top.style.width = wrapper.clientWidth + 'px';
            spacer.style.width = wrapper.scrollWidth + 'px';
            top.scrollLeft = wrapper.scrollLeft;
        };
    }

    function captureColumnWidths(table) {
        if (!table || table.offsetWidth <= 0 || table.getClientRects().length === 0) return null;
        const headers = Array.from(table.querySelectorAll('thead tr:first-child th'));
        const widths = headers.map(header => header.getBoundingClientRect().width);
        if (!widths.length || widths.some(width => !Number.isFinite(width) || width <= 0)) return null;
        return { headers, widths };
    }

    function freezeVisibleColumns(table) {
        // Capture every visible width before changing layout or creating a colgroup.
        const measured = captureColumnWidths(table);
        if (!measured) return null;
        let group = table.querySelector('colgroup');
        if (!group) {
            group = table.ownerDocument.createElement('colgroup');
            table.insertBefore(group, table.querySelector('thead'));
        }
        while (group.children.length < measured.headers.length) group.appendChild(table.ownerDocument.createElement('col'));
        const columns = Array.from(group.children).slice(0, measured.headers.length);
        measured.headers.forEach((header, index) => {
            header.style.width = measured.widths[index] + 'px';
            columns[index].style.width = measured.widths[index] + 'px';
        });
        table.style.tableLayout = 'fixed';
        table.style.width = measured.widths.reduce((sum, width) => sum + width, 0) + 'px';
        return { ...measured, columns, initialWidths: measured.widths.slice() };
    }

    function init(doc, win) {
        if (controllers.has(doc)) return controllers.get(doc);
        const wrappers = Array.from(doc.querySelectorAll('.vu-skill-scroll'));
        const fitButton = doc.getElementById('skillFitBtn');
        setFitMode(wrappers, fitButton, true);
        wrappers.forEach(wrapper => {
            const table = wrapper.querySelector('table');
            if (table) prepareFitColumns(table);
        });
        const scrollbars = new Map(wrappers.map(wrapper => [wrapper, attachHorizontalScroll(wrapper)]));
        const states = new WeakMap();
        let storage;
        try { storage = win.localStorage; } catch { /* A private browser can deny storage access. */ }
        const page = doc.querySelector('.vu-page');
        const densityButtons = Array.from(doc.querySelectorAll('[data-table-density]')).filter(button => button.tagName === 'BUTTON');
        setDensity(page, densityButtons, 'compact', null);
        densityButtons.forEach(button => button.addEventListener('click', () => {
            setDensity(page, densityButtons, button.dataset.tableDensity, storage);
            schedule();
        }));
        let scheduled = false;
        fitButton?.addEventListener('click', () => {
            setFitMode(wrappers, fitButton, fitButton.getAttribute('aria-pressed') !== 'true');
            schedule();
        });

        function refresh() {
            scheduled = false;
            const visible = wrappers.filter(wrapper => wrapper.getClientRects().length && wrapper.offsetWidth > 0);
            const heights = visible.map(wrapper => availableHeight(win.innerHeight, wrapper.getBoundingClientRect().top));
            visible.forEach((wrapper, index) => {
                const height = heights[index] + 'px';
                if (wrapper.style.getPropertyValue('--vu-skill-height') !== height) wrapper.style.setProperty('--vu-skill-height', height);
                scrollbars.get(wrapper)();
                const table = wrapper.querySelector('table');
                if (!table || states.has(table) || wrapper.classList.contains('skill-table-fit')) return;
                const state = freezeVisibleColumns(table);
                if (!state) return;
                states.set(table, state);
                state.headers.forEach((header, columnIndex) => {
                    header.scope = 'col';
                    const text = header.textContent.trim();
                    const minimum = header.dataset.columnKey === 'process' ? 220 : /date|theory|^ojt$/i.test(text) ? 104 : /verifier|disqualified by/i.test(text) ? 150 : /reason|remark/i.test(text) ? 180 : 0;
                    if (state.widths[columnIndex] < minimum) setWidth(table, state, columnIndex, minimum);
                });
                setFrozenColumns(table, state);
                state.headers.forEach((header, columnIndex) => attachResizer(table, wrapper, state, header, columnIndex));
            });
        }

        function schedule() {
            if (scheduled) return;
            scheduled = true;
            win.requestAnimationFrame(refresh);
        }

        function setWidth(table, state, index, width) {
            const minimum = state.headers[index].dataset.columnKey === 'process' ? 160 : 40;
            state.widths = state.widths.map((value, i) => i === index ? resizedColumnWidth(width, state.initialWidths[index], minimum) : value);
            state.headers[index].style.width = state.widths[index] + 'px';
            state.columns[index].style.width = state.widths[index] + 'px';
            const handle = state.headers[index].querySelector('.col-resizer');
            if (handle) {
                handle.setAttribute('aria-valuenow', state.widths[index]);
                handle.setAttribute('aria-valuetext', state.widths[index] + ' pixels');
            }
            table.style.width = state.widths.reduce((sum, value) => sum + value, 0) + 'px';
            setFrozenColumns(table, state);
            schedule();
        }

        function autoFit(table, state, header, index) {
            const canvas = doc.createElement('canvas');
            const context = canvas.getContext('2d');
            if (!context) return;
            const style = win.getComputedStyle(header);
            context.font = style.font || style.fontWeight + ' ' + style.fontSize + ' ' + style.fontFamily;
            let width = context.measureText(header.textContent.trim()).width + 28;
            table.querySelectorAll('tbody tr').forEach(row => {
                if (row.cells[index]) width = Math.max(width, context.measureText(row.cells[index].textContent.trim()).width + 24);
            });
            setWidth(table, state, index, Math.min(400, width));
        }

        function attachResizer(table, wrapper, state, header, index) {
            if (!header.title) header.title = header.textContent.trim();
            const handle = doc.createElement('span');
            handle.className = 'col-resizer';
            handle.tabIndex = 0;
            handle.setAttribute('role', 'separator');
            handle.setAttribute('aria-orientation', 'vertical');
            handle.setAttribute('aria-label', 'Resize ' + (header.title || 'selection') + ' column');
            handle.setAttribute('aria-valuemin', Math.min(state.initialWidths[index], header.dataset.columnKey === 'process' ? 160 : 40));
            handle.setAttribute('aria-valuemax', Math.max(640, Math.ceil(state.widths[index])));
            handle.setAttribute('aria-valuenow', Math.round(state.widths[index]));
            handle.setAttribute('aria-valuetext', Math.round(state.widths[index]) + ' pixels');
            handle.setAttribute('aria-controls', table.id);
            handle.addEventListener('mousedown', event => {
                if (wrapper.classList.contains('skill-table-fit')) return;
                event.preventDefault();
                const startX = event.clientX;
                const startWidth = state.widths[index];
                handle.classList.add('resizing');
                doc.body.classList.add('col-resizing');
                const move = next => setWidth(table, state, index, startWidth + next.clientX - startX);
                const stop = () => {
                    handle.classList.remove('resizing');
                    doc.body.classList.remove('col-resizing');
                    doc.removeEventListener('mousemove', move);
                    doc.removeEventListener('mouseup', stop);
                };
                doc.addEventListener('mousemove', move);
                doc.addEventListener('mouseup', stop);
            });
            handle.addEventListener('dblclick', event => {
                event.stopPropagation();
                if (!wrapper.classList.contains('skill-table-fit')) autoFit(table, state, header, index);
            });
            handle.addEventListener('keydown', event => {
                if (wrapper.classList.contains('skill-table-fit')) return;
                if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
                event.preventDefault();
                setWidth(table, state, index, state.widths[index] + (event.key === 'ArrowRight' ? 10 : -10));
            });
            header.appendChild(handle);
        }

        const controller = { refresh: schedule };
        controllers.set(doc, controller);
        win.addEventListener('resize', schedule);
        win.addEventListener('scroll', schedule, { passive: true });
        doc.addEventListener('click', event => {
            if (event.target.closest('.tab-btn, #skillFitBtn, #toggleSidebarBtn')) schedule();
        });
        doc.addEventListener('transitionend', schedule);
        if (win.ResizeObserver) {
            const observer = new win.ResizeObserver(schedule);
            wrappers.forEach(wrapper => observer.observe(wrapper));
        }
        schedule();
        return controller;
    }

    return { availableHeight, resizedColumnWidth, captureColumnWidths, freezeVisibleColumns, setFrozenColumns, columnPercentages, setFitMode, setDensity, attachHorizontalScroll, init };
});
