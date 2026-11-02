(function (root, factory) {
    'use strict';
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root && root.document) {
        root.EmployeeForms = api;
        if (root.document.readyState === 'loading') root.document.addEventListener('DOMContentLoaded', () => root.setTimeout(() => api.init(root), 0));
        else api.init(root);
    }
})(typeof window === 'undefined' ? null : window, function () {
    'use strict';
    const states = new WeakMap();
    const edited = new WeakSet();

    function snapshot(form) {
        return JSON.stringify(Array.from(form.elements).filter(field => field.name &&
            (!['hidden', 'submit', 'button', 'reset'].includes(field.type) || field._flatpickr)).map(field => {
            let value = field.value ?? '';
            if (field.type === 'file') value = Array.from(field.files || []).map(file => [file.name, file.size, file.lastModified]);
            if (field.type === 'checkbox' || field.type === 'radio') value = field.checked;
            if (field.type === 'select-multiple') value = Array.from(field.selectedOptions).map(option => option.value);
            return [field.name, value];
        }));
    }

    function createState(read) {
        let baseline = read();
        let submitting = false;
        return {
            dirty: () => !submitting && read() !== baseline,
            busy: () => submitting,
            begin(canceled) {
                if (canceled || submitting) return false;
                submitting = true;
                return true;
            },
            restore() { submitting = false; },
            rebase() { baseline = read(); }
        };
    }

    function fieldMessage(field, document) {
        if (!field.name || field.type === 'hidden' || !field.validity) return;
        const container = field.closest('.au-field, .uu-field, .ask-field, .usk-field') || field.parentElement;
        if (!container) return;
        const id = field.id || (field.id = 'field-' + field.name.replace(/[^a-z0-9_-]/gi, '-'));
        const label = container.querySelector('label');
        if (label && !label.htmlFor) label.htmlFor = id;
        const errorId = id + '-client-error';
        let error = document.getElementById(errorId);
        if (!error) {
            error = document.createElement('span');
            error.id = errorId;
            error.className = 'employee-field-error';
            error.setAttribute('aria-live', 'polite');
            container.appendChild(error);
            field.setAttribute('aria-describedby', [field.getAttribute('aria-describedby'), errorId].filter(Boolean).join(' '));
        }
        const valid = field.validity.valid;
        error.textContent = valid ? '' : field.validationMessage;
        field.setAttribute('aria-invalid', String(!valid));
        field.classList.toggle('employee-invalid', !valid);
        if (valid) {
            field.classList.remove('input-validation-error');
            container.querySelectorAll('[data-valmsg-for]').forEach(message => {
                if (message.getAttribute('data-valmsg-for') === field.name) message.textContent = '';
            });
        }
        const visibleSelect = container.querySelector('.fancy-select-header');
        if (visibleSelect && field.tagName === 'SELECT') {
            visibleSelect.setAttribute('aria-invalid', String(!valid));
            visibleSelect.setAttribute('aria-describedby', errorId);
        }
    }

    function refresh(form) {
        const state = states.get(form);
        if (!state) return;
        form.classList.toggle('employee-form-saving', state.busy());
        form.setAttribute('aria-busy', String(state.busy()));
        form.querySelectorAll('button[type="submit"], [data-save-action]').forEach(button => {
            if (state.busy()) button.setAttribute('aria-disabled', 'true');
            else button.removeAttribute('aria-disabled');
        });
        const status = form.querySelector('[data-form-state]');
        if (status) status.textContent = state.busy() ? 'Saving…' : edited.has(form) && state.dirty() ? 'Unsaved changes' : '';
    }

    function submit(form, options = {}) {
        if (!form || states.get(form)?.busy()) return false;
        const button = form.ownerDocument.createElement('button');
        button.type = 'submit';
        button.hidden = true;
        button.formNoValidate = options.validate === false;
        form.appendChild(button);
        form.requestSubmit(button);
        button.remove();
        return states.get(form)?.busy() ?? false;
    }

    function init(window) {
        const document = window.document;
        const forms = Array.from(document.querySelectorAll('form[data-employee-form]'));
        if (!forms.length) return;
        let navigationAccepted = false;
        forms.forEach(form => {
            if (states.has(form)) return;
            states.set(form, createState(() => snapshot(form)));
            let interacted = false;
            form.addEventListener('focusin', () => { if (!interacted) states.get(form).rebase(); });
            // Native constraints still run below; custom selects and file dropzones need a visible focus target.
            form.noValidate = true;
            Array.from(form.elements).forEach(field => {
                if (!field.name || field.type === 'hidden') return;
                const label = field.closest('.au-field, .uu-field, .ask-field, .usk-field')?.querySelector('label');
                if (label && !label.htmlFor) label.htmlFor = field.id || (field.id = 'field-' + field.name.replace(/[^a-z0-9_-]/gi, '-'));
            });
            const status = document.createElement('p');
            status.className = 'employee-form-state';
            status.dataset.formState = '';
            status.setAttribute('role', 'status');
            form.appendChild(status);
            form.addEventListener('input', event => { interacted = true; edited.add(form); fieldMessage(event.target, document); refresh(form); });
            form.addEventListener('change', event => { interacted = true; edited.add(form); fieldMessage(event.target, document); refresh(form); });
            form.addEventListener('employee:filechange', event => { interacted = true; edited.add(form); fieldMessage(event.target, document); refresh(form); });
            form.addEventListener('invalid', event => fieldMessage(event.target, document), true);
            form.addEventListener('reset', () => window.setTimeout(() => refresh(form), 0));
            form.querySelectorAll('[data-valmsg-for]').forEach(error => {
                if (!error.textContent.trim()) return;
                const field = Array.from(form.elements).find(item => item.name === error.getAttribute('data-valmsg-for'));
                if (field) field.setAttribute('aria-invalid', 'true');
            });
        });
        // Document bubbling runs after page-specific validation has had a chance to cancel.
        document.addEventListener('submit', event => {
            const form = event.target;
            const state = states.get(form);
            if (!state) return;
            if (!event.defaultPrevented && !event.submitter?.formNoValidate && !form.checkValidity()) {
                event.preventDefault();
                const invalid = Array.from(form.elements).find(field => field.willValidate && !field.validity.valid);
                const visibleSelect = invalid?.closest('.au-field, .uu-field, .ask-field, .usk-field')?.querySelector('.fancy-select-header');
                const focus = invalid?._flatpickr?.altInput || visibleSelect || (invalid?.type === 'file' ? document.getElementById('uploadZone') : invalid);
                if (focus) { focus.tabIndex = focus.tabIndex < 0 ? 0 : focus.tabIndex; focus.focus(); }
            }
            if (!state.begin(event.defaultPrevented)) {
                if (state.busy()) event.preventDefault();
                return;
            }
            refresh(form);
        });
        document.addEventListener('click', event => {
            const button = event.target.closest('button, input[type="submit"]');
            if (button && states.get(button.form)?.busy()) {
                event.preventDefault();
                event.stopImmediatePropagation();
                return;
            }
            const anchor = event.target.closest('a[href]');
            if (!anchor || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey ||
                event.button !== 0 || anchor.target === '_blank' || anchor.hasAttribute('download')) return;
            const href = anchor.getAttribute('href');
            if (!href || href.startsWith('#') || !forms.some(form => edited.has(form) && states.get(form).dirty())) return;
            if (!window.confirm('Leave this page? Your unsaved changes will be lost.')) {
                event.preventDefault();
                event.stopImmediatePropagation();
            } else navigationAccepted = true;
        }, true);
        window.addEventListener('beforeunload', event => {
            if (!navigationAccepted && forms.some(form => edited.has(form) && states.get(form).dirty())) {
                event.preventDefault();
                event.returnValue = '';
            }
        });
        window.addEventListener('pageshow', () => {
            navigationAccepted = false;
            forms.forEach(form => { states.get(form).restore(); refresh(form); });
        });
    }

    return { createState, snapshot, submit, init };
});
