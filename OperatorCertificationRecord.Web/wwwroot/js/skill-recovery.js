(function () {
    'use strict';

    const descriptions = {
        reinstate: 'Remove the mistaken disqualification. Original dates, results and certificate stay unchanged.',
        restore: 'Create a restored copy of this archived version. The original history stays available.',
        move: 'Correct the employee assignment. Review both employees and the certificate before applying.',
        'undo-edit': 'Restore the exact previous version, including its certificate. Archive the incorrect update.',
        'undo-add': 'Remove this incorrect entry from skill records. Recovery history and PDF are kept.',
        'move-tab': 'Move this exact skill to another tab for the same employee. Dates, results and PDF stay unchanged.',
        'remove-pdf': 'Remove only this skill’s PDF link. Skill fields stay unchanged. Recovery history keeps the original PDF.',
        'replace-pdf': 'Replace or add the PDF for this exact skill. Skill fields stay unchanged. Preview both files before applying.'
    };

    function updateAction(form, resetConsent) {
        const selected = form.querySelector('[data-recovery-action]:checked:not(:disabled)');
        const action = selected ? selected.value : '';
        const replace = form.querySelector('#recovery-replace');
        const uploadId = form.querySelector('#recovery-upload-id');
        if (resetConsent && replace) replace.checked = false;
        form.querySelectorAll('[data-recovery-fields]').forEach(function (group) {
            const active = group.dataset.recoveryFields === action;
            group.hidden = !active;
            group.querySelectorAll('input, select').forEach(function (field) {
                field.disabled = !active;
                field.required = active && field.type !== 'hidden' && (field.type !== 'file' || !uploadId?.value);
            });
        });
        const help = form.querySelector('#recovery-action-help');
        if (help) help.textContent = descriptions[action] || 'Select an available correction to review its effect.';
        const destination = form.ownerDocument.querySelector('.recovery-target-search');
        if (destination && action === 'move') destination.open = true;
        const replacement = form.querySelector('[data-recovery-replace]');
        const targetTab = form.querySelector('#recovery-tab');
        if (replacement && replace) {
            const allowed = !['undo-add', 'remove-pdf', 'replace-pdf'].includes(action) && (action !== 'move-tab' || targetTab?.value === 'current');
            replacement.hidden = !allowed;
            replace.disabled = !allowed;
            if (!allowed) replace.checked = false;
        }
        const reason = form.querySelector('#recovery-reason');
        const reasonHelp = form.querySelector('#recovery-reason-help');
        const limit = action === 'move-tab' && targetTab?.value === 'disqualified' ? 300 : 500;
        if (reason) reason.maxLength = limit;
        if (reasonHelp) reasonHelp.textContent = 'Optional. Up to ' + limit + ' characters. Saved in recovery history. Original Remark stays unchanged.';
    }

    function init(document) {
        restoreFocusFromHash(document);
        initDialog(document);
        const form = document.getElementById('recovery-form');
        if (!form) return;
        form.querySelectorAll('[data-recovery-action]').forEach(function (radio) {
            radio.addEventListener('change', function () { updateAction(form, true); });
        });
        ['recovery-destination', 'recovery-prior', 'recovery-tab'].forEach(function (id) {
            const field = document.getElementById(id);
            if (field) field.addEventListener('change', function () {
                const replace = document.getElementById('recovery-replace');
                if (replace) replace.checked = false;
                if (id === 'recovery-tab') updateAction(form, true);
            });
        });
        form.addEventListener('input', function () { invalidatePreview(document); });
        form.addEventListener('change', function () { invalidatePreview(document); });
        const pdf = document.getElementById('recovery-pdf-file');
        if (pdf) pdf.addEventListener('change', function () {
            const changed = !!pdf.files?.length;
            const retained = document.getElementById('recovery-staged-pdf');
            const notice = document.getElementById('recovery-upload-change');
            if (retained) retained.hidden = changed;
            if (notice) notice.hidden = !changed;
            invalidatePreview(document);
        });
        updateAction(form, false);
    }

    function initDialog(document) {
        const dialog = document.getElementById('recovery-dialog');
        if (!dialog) return;
        const view = document.defaultView;
        function open() {
            // Server-rendered open is a readable no-JS fallback, not a native modal.
            if (typeof dialog.showModal === 'function') {
                if (dialog.open) dialog.removeAttribute('open');
                dialog.showModal();
            }
            document.documentElement.classList.add('recovery-modal-open');
            const initial = document.getElementById('recovery-dialog-error') ||
                document.getElementById('recovery-preview-heading') || document.getElementById('recovery-dialog-title');
            if (initial) initial.focus();
        }
        Array.from(dialog.querySelectorAll?.('[data-recovery-close]') || []).forEach(function (button) {
            button.addEventListener('click', function () { dialog.close(); });
        });
        dialog.addEventListener('close', function () {
            document.documentElement.classList.remove('recovery-modal-open');
            invalidatePreview(document);
            const origin = document.getElementById(dialog.dataset.returnFocus) ||
                document.getElementById('recovery-source-employee') || document.getElementById('recovery-search');
            if (view?.history && view.location) {
                const url = new URL(view.location.href);
                const popupKeys = ['rowid', 'table', 'choice', 'targetquery', 'targetcode', 'undoactionid', 'handler'];
                Array.from(url.searchParams.keys()).forEach(function (key) {
                    if (popupKeys.includes(key.toLowerCase())) url.searchParams.delete(key);
                    if ((dialog.dataset.employee && key.toLowerCase() === 'empcode') ||
                        (dialog.dataset.filter && key.toLowerCase() === 'filter')) url.searchParams.delete(key);
                });
                if (dialog.dataset.employee) url.searchParams.set('EmpCode', dialog.dataset.employee);
                if (dialog.dataset.filter) url.searchParams.set('Filter', dialog.dataset.filter);
                if (dialog.dataset.postback === 'true') {
                    // replaceState changes a POST entry's URL, not its HTTP method.
                    // A real GET replacement prevents Preview/Undo/failed Apply resubmission.
                    url.hash = origin?.id || dialog.dataset.returnFocus || 'recovery-source-employee';
                    view.location.replace(url.href);
                    return;
                }
                view.history.replaceState(view.history.state, '', url.href);
            }
            if (origin) origin.focus();
        });
        if (dialog.open) open();
        view?.addEventListener('pageshow', function () {
            if (dialog.open) {
                document.documentElement.classList.add('recovery-modal-open');
                if (typeof dialog.matches === 'function' && !dialog.matches(':modal')) open();
            } else document.documentElement.classList.remove('recovery-modal-open');
        });
    }

    function restoreFocusFromHash(document) {
        const view = document.defaultView;
        if (!view?.location) return;
        let target;
        try { target = decodeURIComponent(new URL(view.location.href).hash.slice(1)); }
        catch { return; }
        if (!target.startsWith('recovery-actions-') && !target.startsWith('recovery-undo-') &&
            target !== 'recovery-source-employee' && target !== 'recovery-search') return;
        const origin = document.getElementById(target) || document.getElementById('recovery-source-employee') ||
            document.getElementById('recovery-search');
        if (origin) origin.focus();
    }

    function invalidatePreview(document) {
        const apply = document.getElementById('recovery-apply');
        if (!apply) return;
        apply.disabled = true;
        const confirmation = document.getElementById('ConfirmApply');
        if (confirmation) confirmation.checked = false;
        const message = document.getElementById('recovery-preview-stale');
        if (message) message.hidden = false;
    }

    if (typeof module !== 'undefined' && module.exports) module.exports = { updateAction: updateAction, init: init, initDialog: initDialog, invalidatePreview: invalidatePreview, restoreFocusFromHash: restoreFocusFromHash };
    if (typeof document !== 'undefined') {
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { init(document); });
        else init(document);
    }
})();
