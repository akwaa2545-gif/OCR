(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else {
        root.AppDatePicker = api.create(root.document, root, root.flatpickr);
        if (root.document.readyState === 'loading') root.document.addEventListener('DOMContentLoaded', () => root.AppDatePicker.init());
        else root.AppDatePicker.init();
    }
})(typeof window === 'object' ? window : this, function () {
    'use strict';
    const months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    const thaiMonths = {
        shorthand: ['ม.ค.', 'ก.พ.', 'มี.ค.', 'เม.ย.', 'พ.ค.', 'มิ.ย.', 'ก.ค.', 'ส.ค.', 'ก.ย.', 'ต.ค.', 'พ.ย.', 'ธ.ค.'],
        longhand: ['มกราคม', 'กุมภาพันธ์', 'มีนาคม', 'เมษายน', 'พฤษภาคม', 'มิถุนายน', 'กรกฎาคม', 'สิงหาคม', 'กันยายน', 'ตุลาคม', 'พฤศจิกายน', 'ธันวาคม']
    };
    const thaiLocale = {
        months: thaiMonths,
        weekdays: { shorthand: ['อา', 'จ', 'อ', 'พ', 'พฤ', 'ศ', 'ส'],
            longhand: ['อาทิตย์', 'จันทร์', 'อังคาร', 'พุธ', 'พฤหัสบดี', 'ศุกร์', 'เสาร์'] },
        rangeSeparator: ' ถึง ', time_24hr: true
    };
    const pad = value => String(value).padStart(2, '0');
    const yearDropdowns = new WeakMap();

    function installYearDropdown(instance) {
        if (yearDropdowns.has(instance)) return yearDropdowns.get(instance);
        const wrapper = instance.currentYearElement?.parentNode;
        const doc = instance.calendarContainer?.ownerDocument;
        if (!wrapper?.parentNode || !doc) return;
        instance.calendarContainer.setAttribute?.('lang', 'th');
        instance.monthsDropdownContainer?.setAttribute('aria-label', 'เดือน');
        const select = doc.createElement('select');
        select.className = 'fp-year-select';
        select.setAttribute('aria-label', 'ปี');
        const previousDisplay = wrapper.style.display;
        const previousHidden = wrapper.getAttribute('aria-hidden');
        wrapper.style.display = 'none';
        wrapper.setAttribute('aria-hidden', 'true');
        wrapper.parentNode.insertBefore(select, wrapper);
        let signature = '';
        function sync() {
            const now = new Date().getFullYear();
            const minimum = instance.config.minDate?.getFullYear() || 1;
            const maximum = instance.config.maxDate?.getFullYear() || 9999;
            const years = new Set();
            for (let year = Math.max(minimum, now - 100); year <= Math.min(maximum, now + 20); year++) years.add(year);
            [instance.currentYear, ...instance.selectedDates.map(date => date.getFullYear()),
                instance.config.minDate?.getFullYear(), instance.config.maxDate?.getFullYear()]
                .forEach(year => { if (year >= minimum && year <= maximum) years.add(year); });
            const values = [...years].sort((a, b) => a - b);
            const nextSignature = values.join(',');
            if (signature !== nextSignature) {
                select.replaceChildren(...values.map(year => {
                    const option = doc.createElement('option');
                    option.value = String(year); option.textContent = String(year);
                    return option;
                }));
                signature = nextSignature;
            }
            select.value = String(instance.currentYear);
        }
        const change = () => { instance.changeYear(Number(select.value)); sync(); };
        select.addEventListener('change', change);
        const hookNames = ['onYearChange', 'onMonthChange', 'onOpen'];
        hookNames.forEach(name => {
            const previous = instance.config[name];
            instance.config[name] = [...(Array.isArray(previous) ? previous : previous ? [previous] : []), sync];
        });
        // setDate(false) intentionally suppresses hooks; synchronize silent programmatic updates too.
        const methods = ['setDate', 'clear', 'set'].map(name => {
            const original = instance[name];
            if (!original) return null;
            const wrapped = function (...args) { const result = original.apply(instance, args); sync(); return result; };
            instance[name] = wrapped;
            return { name, original, wrapped };
        }).filter(Boolean);
        function destroy() {
            select.removeEventListener('change', change);
            select.remove();
            wrapper.style.display = previousDisplay;
            if (previousHidden === null) wrapper.removeAttribute('aria-hidden');
            else wrapper.setAttribute('aria-hidden', previousHidden);
            [...hookNames, 'onDestroy'].forEach(name => {
                instance.config[name] = instance.config[name].filter(fn => fn !== sync && fn !== destroy);
            });
            methods.forEach(({ name, original, wrapped }) => { if (instance[name] === wrapped) instance[name] = original; });
            yearDropdowns.delete(instance);
        }
        const previousDestroy = instance.config.onDestroy;
        instance.config.onDestroy = [...(Array.isArray(previousDestroy) ? previousDestroy : previousDestroy ? [previousDestroy] : []), destroy];
        const controller = { sync, destroy };
        yearDropdowns.set(instance, controller);
        sync();
        return controller;
    }

    function parseDate(value) {
        if (value instanceof Date) return Number.isNaN(value.getTime()) ? undefined : new Date(value.getTime());
        if (typeof value !== 'string') return undefined;
        const text = value.trim();
        let year, month, day;
        let match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text);
        if (match) [, year, month, day] = match;
        else if ((match = /^(\d{1,2})\s+([^\s]+)\s+(\d{4})$/.exec(text))) {
            day = match[1]; year = match[3];
            const index = months.findIndex((name, i) => name.toLowerCase() === match[2].toLowerCase() ||
                name.slice(0, 3).toLowerCase() === match[2].toLowerCase() ||
                thaiMonths.shorthand[i] === match[2] || thaiMonths.longhand[i] === match[2]);
            month = index + 1;
        } else if ((match = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(text))) {
            [, day, month, year] = match;
        } else return undefined;
        year = Number(year); month = Number(month); day = Number(day);
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > 31) return undefined;
        const result = new Date(0);
        result.setHours(0, 0, 0, 0);
        result.setFullYear(year, month - 1, day);
        return result.getFullYear() === year && result.getMonth() === month - 1 && result.getDate() === day ? result : undefined;
    }

    function formatISO(date) {
        return date ? String(date.getFullYear()).padStart(4, '0') + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate()) : '';
    }

    function positionCalendar(instance, win) {
        const calendar = instance.calendarContainer;
        const rect = instance._input.getBoundingClientRect();
        // A transformed dialog becomes the containing block for fixed children.
        // Measure its viewport origin and scale rather than assuming body coordinates.
        Object.assign(calendar.style, { position: 'fixed', left: '0px', top: '0px', right: 'auto',
            maxWidth: Math.max(0, win.innerWidth - 16) + 'px', maxHeight: Math.max(0, win.innerHeight - 16) + 'px' });
        const origin = calendar.getBoundingClientRect();
        const scaleX = calendar.offsetWidth && origin.width ? origin.width / calendar.offsetWidth : 1;
        const scaleY = calendar.offsetHeight && origin.height ? origin.height / calendar.offsetHeight : 1;
        calendar.style.maxWidth = Math.max(0, win.innerWidth - 16) / scaleX + 'px';
        calendar.style.maxHeight = Math.max(0, win.innerHeight - 16) / scaleY + 'px';
        const size = calendar.getBoundingClientRect();
        const width = Math.min(size.width || 310, win.innerWidth - 16);
        const height = Math.min(size.height || calendar.offsetHeight || 300, win.innerHeight - 16);
        const needsScroll = Math.max(size.height, (calendar.scrollHeight || calendar.offsetHeight || 300) * scaleY) > win.innerHeight - 16;
        const below = rect.bottom + 8;
        const top = below + height <= win.innerHeight - 8 ? below : Math.max(8, rect.top - height - 8);
        Object.assign(calendar.style, {
            left: (Math.max(8, Math.min(rect.left, win.innerWidth - width - 8)) - origin.left) / scaleX + 'px',
            top: (Math.min(top, win.innerHeight - height - 8) - origin.top) / scaleY + 'px', overflowY: needsScroll ? 'auto' : 'hidden'
        });
        calendar.classList.remove('arrowTop', 'arrowBottom');
    }

    function create(doc, win, flatpickr) {
        const states = new Map();
        const nativeValue = Object.getOwnPropertyDescriptor(win.HTMLInputElement.prototype, 'value');
        let initialized = false;
        let serial = 0;
        const listen = (state, target, name, handler, options) => {
            target.addEventListener(name, handler, options);
            state.cleanup.push(() => target.removeEventListener(name, handler, options));
        };
        const validity = (input, message) => {
            input.setCustomValidity(message);
            input.setAttribute('aria-invalid', String(Boolean(message)));
        };

        function messageFor(el, raw) {
            if (!raw.trim()) return '';
            const date = parseDate(raw);
            if (!date) return 'กรุณากรอกวันที่ให้ถูกต้องเป็นปี ค.ศ. เช่น 28 ต.ค. 2026 หรือ 2026-10-28';
            const iso = formatISO(date);
            if (el.min && iso < el.min) return 'กรุณาเลือกวันที่ตั้งแต่ ' + el.min;
            if (el.max && iso > el.max) return 'กรุณาเลือกวันที่ไม่เกิน ' + el.max;
            return '';
        }

        function commit(state, fireChange) {
            const { el, fp } = state;
            const raw = fp.altInput.value;
            const error = messageFor(el, raw);
            validity(fp.altInput, error);
            if (error) {
                nativeValue.set.call(el, '');
                return false;
            }
            const date = parseDate(raw);
            const iso = formatISO(date);
            const changed = nativeValue.get.call(el) !== iso || formatISO(fp.selectedDates[0]) !== iso;
            state.busy = true;
            try {
                if (date) fp.setDate(date, fireChange && changed);
                else fp.clear(fireChange && changed);
            } finally { state.busy = false; }
            state.dirty = false;
            return true;
        }

        function syncValue(state, value) {
            const { el, fp } = state;
            const raw = value == null ? '' : String(value);
            if (state.busy || !state.dirty && formatISO(fp.selectedDates[0]) === raw &&
                formatISO(parseDate(fp.altInput.value)) === raw && fp.altInput.validationMessage === '') return;
            state.busy = true;
            try {
                const date = parseDate(raw);
                const error = messageFor(el, raw);
                if (raw && (!date || error)) {
                    fp.clear(false);
                    fp.altInput.value = raw;
                    validity(fp.altInput, error);
                    nativeValue.set.call(el, '');
                } else {
                    if (date) fp.setDate(date, false);
                    else fp.clear(false);
                    validity(fp.altInput, '');
                }
                state.dirty = false;
            } finally { state.busy = false; }
        }

        function syncAttributes(state) {
            const { el, fp } = state;
            fp.altInput.disabled = el.disabled;
            fp.altInput.readOnly = el.readOnly;
            fp.altInput.required = el.required;
            fp.altInput.setAttribute('aria-required', String(el.required));
            state.button.disabled = el.disabled || el.readOnly;
            if (el.disabled || el.readOnly) fp.close();
            const minimum = el.min || undefined;
            const maximum = el.max || undefined;
            if (state.min !== minimum || state.max !== maximum) {
                const raw = fp.altInput.value;
                state.busy = true;
                try { fp.set({ minDate: minimum, maxDate: maximum }); }
                finally { state.busy = false; }
                state.min = minimum; state.max = maximum;
                if (raw) { fp.altInput.value = raw; commit(state, false); }
            }
        }

        function enhance(el) {
            if (!flatpickr || states.has(el) || el._flatpickr || !el.matches('input[type="date"]') || el.hasAttribute('data-no-flatpickr')) return;
            const labels = Array.from(el.labels || []);
            const nearbyLabel = el.parentElement && el.parentElement.querySelector('label');
            if (!labels.length && nearbyLabel) labels.push(nearbyLabel);
            // Hidden inputs reflect .value writes into defaultValue; preserve the native date reset baseline first.
            const state = { el, fp: null, busy: false, dirty: false, cleanup: [], resetValue: el.defaultValue,
                descriptor: Object.getOwnPropertyDescriptor(el, 'value') };
            const fp = flatpickr(el, {
                // The adapter owns typed input; disable Flatpickr's loose outside-click parser.
                dateFormat: 'Y-m-d', altInput: true, altFormat: 'j M Y', allowInput: false,
                disableMobile: true, monthSelectorType: 'dropdown', parseDate,
                locale: { ...(flatpickr.l10ns?.th || thaiLocale), firstDayOfWeek: 0,
                    yearAriaLabel: 'ปี', monthAriaLabel: 'เดือน' },
                minDate: el.min || undefined, maxDate: el.max || undefined,
                appendTo: el.closest('[role="dialog"], .modal') || doc.body,
                position: instance => positionCalendar(instance, win),
                onChange: () => { if (state.fp) { state.dirty = false; validity(state.fp.altInput, ''); } }
            });
            if (!fp || !fp.altInput) return;
            installYearDropdown(fp);
            state.fp = fp;
            states.set(el, state);
            const alt = fp.altInput;
            alt.id = (el.id || 'date') + '-display-' + (++serial);
            alt.placeholder = el.placeholder || 'วัน เดือน ปี ค.ศ.';
            alt.setAttribute('lang', 'th');
            alt.autocomplete = 'off';
            const labelledBy = labels.map(label => { if (!label.id) label.id = 'date-label-' + (++serial); return label.id; });
            if (el.getAttribute('aria-labelledby')) labelledBy.push(el.getAttribute('aria-labelledby'));
            if (labelledBy.length) alt.setAttribute('aria-labelledby', labelledBy.join(' '));
            else alt.setAttribute('aria-label', el.getAttribute('aria-label') || el.placeholder || 'วันที่');
            if (el.getAttribute('aria-describedby')) alt.setAttribute('aria-describedby', el.getAttribute('aria-describedby'));
            if (el.getAttribute('aria-errormessage')) alt.setAttribute('aria-errormessage', el.getAttribute('aria-errormessage'));
            const wrap = doc.createElement('div');
            wrap.className = 'fp-date-wrap';
            alt.parentNode.insertBefore(wrap, alt);
            wrap.appendChild(alt);
            state.wrap = wrap;
            const button = doc.createElement('button');
            button.type = 'button';
            button.className = 'fp-cal-icon';
            button.setAttribute('aria-label', 'เปิดปฏิทิน ' + (labels.map(label => label.textContent.trim()).join(' ') || el.getAttribute('aria-label') || 'วันที่'));
            button.innerHTML = '<svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/></svg>';
            wrap.appendChild(button);
            state.button = button;
            listen(state, button, 'click', () => open(el));
            Object.defineProperty(el, 'value', {
                configurable: true,
                get() { return nativeValue.get.call(el); },
                set(value) { nativeValue.set.call(el, value == null ? '' : value); syncValue(state, value); }
            });
            listen(state, alt, 'input', () => {
                state.dirty = true;
                nativeValue.set.call(el, '');
                validity(alt, messageFor(el, alt.value));
            });
            listen(state, alt, 'change', event => { event.stopImmediatePropagation(); commit(state, true); }, true);
            listen(state, alt, 'blur', event => { event.stopImmediatePropagation(); if (state.dirty) commit(state, true); }, true);
            listen(state, alt, 'keydown', event => {
                if ((el.disabled || el.readOnly) && event.key !== 'Tab' && event.key !== 'Escape') {
                    event.stopImmediatePropagation();
                    return;
                }
                if (event.key === 'Enter' && state.dirty) {
                    event.preventDefault(); event.stopImmediatePropagation();
                    if (commit(state, true)) fp.close();
                    else alt.reportValidity();
                } else if (event.key === 'Backspace' || event.key === 'Delete') {
                    // Preserve normal text editing instead of Flatpickr's clear-on-key behavior.
                    event.stopImmediatePropagation();
                }
            }, true);
            for (const name of ['click', 'focus']) listen(state, alt, name, event => {
                if (el.disabled || el.readOnly) { event.stopImmediatePropagation(); fp.close(); }
            }, true);
            for (const label of labels) listen(state, label, 'click', event => {
                if (event.target.closest('button,a,input,select')) return;
                event.preventDefault(); open(el);
            });
            state.min = el.min || undefined; state.max = el.max || undefined;
            syncAttributes(state);
            return fp;
        }

        function destroy(state) {
            const currentValue = nativeValue.get.call(state.el);
            state.cleanup.forEach(remove => remove());
            states.delete(state.el);
            if (state.descriptor) Object.defineProperty(state.el, 'value', state.descriptor);
            else delete state.el.value;
            state.fp.destroy();
            state.el.defaultValue = state.resetValue;
            nativeValue.set.call(state.el, currentValue);
            state.wrap.remove();
        }

        function open(el) {
            const fp = states.get(el)?.fp || el?._flatpickr;
            if (!fp || el.disabled) return;
            fp.altInput?.focus();
            if (el.readOnly) return;
            fp.open();
        }

        function init() {
            if (initialized || !flatpickr) return;
            initialized = true;
            doc.querySelectorAll('input[type="date"]').forEach(enhance);
            new win.MutationObserver(records => {
                for (const state of states.values()) if (!state.el.isConnected || state.el.hasAttribute('data-no-flatpickr')) destroy(state);
                records.forEach(record => {
                    if (record.type === 'attributes') {
                        const state = states.get(record.target);
                        if (state) syncAttributes(state);
                        else if (record.target.matches('input[type="date"]')) enhance(record.target);
                    }
                    record.addedNodes?.forEach(node => {
                        if (node.nodeType !== 1 || !node.isConnected) return;
                        if (node.matches('input[type="date"]')) enhance(node);
                        node.querySelectorAll('input[type="date"]').forEach(enhance);
                    });
                });
            }).observe(doc.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled', 'readonly', 'required', 'min', 'max', 'data-no-flatpickr'] });
            doc.addEventListener('reset', event => win.setTimeout(() => {
                if (event.defaultPrevented) return;
                for (const state of states.values()) if (state.el.form === event.target) syncValue(state, state.resetValue);
            }, 0));
            doc.addEventListener('submit', event => {
                for (const state of states.values()) {
                    if (state.el.form !== event.target || state.el.disabled) continue;
                    if (!commit(state, true) || !state.fp.altInput.checkValidity()) {
                        event.preventDefault(); state.fp.altInput.reportValidity(); break;
                    }
                }
            }, true);
            const reposition = () => { for (const state of states.values()) if (state.fp.isOpen) positionCalendar(state.fp, win); };
            win.addEventListener('resize', reposition);
            win.addEventListener('scroll', reposition, true);
            win.addEventListener('pageshow', () => { for (const state of states.values()) state.fp.close(); });
            doc.addEventListener('hidden.bs.modal', event => {
                for (const state of states.values()) if (event.target.contains(state.el)) state.fp.close();
            });
        }
        return { init, enhance, open, installYearDropdown };
    }
    return { create, parseDate, formatISO, positionCalendar, installYearDropdown };
});
