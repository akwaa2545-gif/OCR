(function () {
    const OPEN_CLS = 'open';

    function buildList(select, list) {
        list.innerHTML = '';
        Array.from(select.options).forEach((opt) => {
            const item = document.createElement('div');
            item.className = 'fancy-select-item';
            if (opt.value === '' || opt.disabled) item.classList.add('placeholder');
            if (opt.disabled) item.classList.add('disabled');
            item.textContent = opt.textContent;
            item.dataset.value = opt.value;
            list.appendChild(item);
        });
    }

    function syncActive(list, value) {
        list.querySelectorAll('.fancy-select-item').forEach(x => {
            x.classList.toggle('active', x.dataset.value === value);
        });
    }

    function enhance(select) {
        if (select.__fancy) return;
        if (select.closest('[data-no-fancy]') || select.hasAttribute('data-no-fancy')) return;
        if (select.closest('.flatpickr-calendar')) return;  // skip flatpickr internal month/year selects
        if (select.size > 1 || select.multiple) return;    // skip multi/listbox selects
        select.__fancy = true;
        select.style.display = 'none';

        const wrapper = document.createElement('div');
        wrapper.className = 'fancy-select-wrapper';
        // copy inline width/style hints
        if (select.style.width) wrapper.style.width = select.style.width;

        const header = document.createElement('div');
        header.className = 'fancy-select-header';
        header.tabIndex = 0;
        header.setAttribute('role', 'combobox');
        header.setAttribute('aria-expanded', 'false');
        header.setAttribute('aria-haspopup', 'listbox');

        const label = document.createElement('span');
        label.className = 'label';

        const caret = document.createElement('span');
        caret.className = 'caret';
        caret.setAttribute('aria-hidden', 'true');
        caret.innerHTML = `<svg width="12" height="12" viewBox="0 0 12 12" fill="none">
            <path d="M2 4l4 4 4-4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>`;

        header.appendChild(label);
        header.appendChild(caret);

        const list = document.createElement('div');
        list.className = 'fancy-select-list';
        list.setAttribute('role', 'listbox');

        buildList(select, list);

        // Set initial displayed label
        function refreshLabel() {
            const sel = select.selectedOptions[0];
            label.textContent = sel ? sel.textContent : (select.options[0] ? select.options[0].textContent : '');
            syncActive(list, select.value);
        }
        refreshLabel();

        // Item click
        list.addEventListener('click', function (e) {
            const item = e.target.closest('.fancy-select-item');
            if (!item || item.classList.contains('disabled')) return;
            e.stopPropagation();
            select.value = item.dataset.value;
            select.dispatchEvent(new Event('change', { bubbles: true }));
            syncActive(list, select.value);
            label.textContent = item.textContent;
            closeList();
        });

        function openList() {
            // close all others
            document.querySelectorAll('.fancy-select-wrapper.' + OPEN_CLS).forEach(w => {
                if (w !== wrapper) w.classList.remove(OPEN_CLS);
            });
            wrapper.classList.add(OPEN_CLS);
            header.setAttribute('aria-expanded', 'true');
            const active = list.querySelector('.fancy-select-item.active');
            requestAnimationFrame(() => {
                if (active) active.scrollIntoView({ block: 'nearest' });
            });
        }

        function closeList() {
            wrapper.classList.remove(OPEN_CLS);
            header.setAttribute('aria-expanded', 'false');
        }

        function toggleList() {
            wrapper.classList.contains(OPEN_CLS) ? closeList() : openList();
        }

        header.addEventListener('click', function (e) {
            e.stopPropagation();
            toggleList();
        });

        header.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleList(); }
            if (e.key === 'Escape') closeList();
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                openList();
                const items = Array.from(list.querySelectorAll('.fancy-select-item:not(.disabled):not(.placeholder)'));
                const idx = items.findIndex(x => x.classList.contains('active'));
                const next = e.key === 'ArrowDown'
                    ? Math.min(items.length - 1, idx < 0 ? 0 : idx + 1)
                    : Math.max(0, idx < 0 ? items.length - 1 : idx - 1);
                if (items[next]) {
                    items[next].click();
                }
            }
        });

        // MutationObserver — rebuild when options change (AJAX-loaded selects)
        const observer = new MutationObserver(() => {
            buildList(select, list);
            refreshLabel();
        });
        observer.observe(select, { childList: true, subtree: true, attributes: true, attributeFilter: ['selected', 'disabled'] });

        // Sync if native value changes externally
        select.addEventListener('change', refreshLabel);

        wrapper.appendChild(header);
        wrapper.appendChild(list);
        select.parentNode.insertBefore(wrapper, select.nextSibling);
    }

    function init() {
        document.querySelectorAll('select').forEach(enhance);
    }

    // Close on outside click
    document.addEventListener('click', () => {
        document.querySelectorAll('.fancy-select-wrapper.' + OPEN_CLS).forEach(w => w.classList.remove(OPEN_CLS));
    });

    // Run on load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Re-run when new selects are injected into the DOM
    const domObserver = new MutationObserver(() => {
        document.querySelectorAll('select:not([data-fancy-enhanced])').forEach(sel => {
            sel.setAttribute('data-fancy-enhanced', '1');
            enhance(sel);
        });
    });
    domObserver.observe(document.body, { childList: true, subtree: true });

    window.FancySelect = { init, enhance };

})();

