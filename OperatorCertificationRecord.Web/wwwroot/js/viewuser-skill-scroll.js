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

    function resizedColumnWidth(requested, initialWidth) {
        return Math.max(Math.min(initialWidth, 40), Math.min(2000, Math.round(requested)));
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
            table.insertBefore(group, table.firstChild);
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
        const states = new WeakMap();
        let scheduled = false;

        function refresh() {
            scheduled = false;
            const visible = wrappers.filter(wrapper => wrapper.getClientRects().length && wrapper.offsetWidth > 0);
            const heights = visible.map(wrapper => availableHeight(win.innerHeight, wrapper.getBoundingClientRect().top));
            visible.forEach((wrapper, index) => {
                const height = heights[index] + 'px';
                if (wrapper.style.getPropertyValue('--vu-skill-height') !== height) wrapper.style.setProperty('--vu-skill-height', height);
                const table = wrapper.querySelector('table');
                if (!table || states.has(table) || wrapper.classList.contains('skill-table-fit')) return;
                const state = freezeVisibleColumns(table);
                if (!state) return;
                states.set(table, state);
                state.headers.forEach((header, columnIndex) => attachResizer(table, wrapper, state, header, columnIndex));
            });
        }

        function schedule() {
            if (scheduled) return;
            scheduled = true;
            win.requestAnimationFrame(refresh);
        }

        function setWidth(table, state, index, width) {
            state.widths = state.widths.map((value, i) => i === index ? resizedColumnWidth(width, state.initialWidths[index]) : value);
            state.headers[index].style.width = state.widths[index] + 'px';
            state.columns[index].style.width = state.widths[index] + 'px';
            const handle = state.headers[index].querySelector('.col-resizer');
            if (handle) {
                handle.setAttribute('aria-valuenow', state.widths[index]);
                handle.setAttribute('aria-valuetext', state.widths[index] + ' pixels');
            }
            table.style.width = state.widths.reduce((sum, value) => sum + value, 0) + 'px';
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
            handle.setAttribute('aria-valuemin', Math.min(state.initialWidths[index], 40));
            handle.setAttribute('aria-valuemax', Math.max(2000, Math.ceil(state.widths[index])));
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

    return { availableHeight, resizedColumnWidth, captureColumnWidths, freezeVisibleColumns, init };
});
