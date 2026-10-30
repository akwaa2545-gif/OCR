'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { updateAction, invalidatePreview, initDialog, restoreFocusFromHash, init } = require('../OperatorCertificationRecord.Web/wwwroot/js/skill-recovery.js');

function fixture(action) {
    const replace = { checked: true };
    const moveField = { disabled: false, required: false };
    const priorField = { disabled: false, required: false };
    const tabField = { disabled: false, required: false };
    const pdfField = { type: 'file', disabled: false, required: false };
    const uploadId = { type: 'hidden', value: '', disabled: false, required: false };
    const group = (name, field) => ({ dataset: { recoveryFields: name }, hidden: false,
        querySelectorAll: () => [field] });
    const pdfGroup = { dataset: { recoveryFields: 'replace-pdf' }, hidden: false, querySelectorAll: () => [pdfField, uploadId] };
    const groups = [group('move', moveField), group('undo-edit', priorField), group('move-tab', tabField), pdfGroup];
    const help = { textContent: '' };
    const destination = { open: false };
    const form = {
        querySelector: selector => selector === '[data-recovery-action]:checked:not(:disabled)' ? { value: action }
            : selector === '#recovery-replace' ? replace : selector === '#recovery-action-help' ? help : selector === '#recovery-upload-id' ? uploadId : null,
        querySelectorAll: () => groups,
        ownerDocument: { querySelector: () => destination }
    };
    return { form, replace, moveField, priorField, tabField, pdfField, uploadId, groups, help, destination };
}

test('Move requires destination and disables the unrelated prior-version field', () => {
    const view = fixture('move');
    updateAction(view.form, true);
    assert.equal(view.moveField.required, true);
    assert.equal(view.moveField.disabled, false);
    assert.equal(view.priorField.required, false);
    assert.equal(view.priorField.disabled, true);
    assert.equal(view.groups[1].hidden, true);
    assert.equal(view.destination.open, true);
});

test('Undo Edit requires the prior version without posting a stale destination', () => {
    const view = fixture('undo-edit');
    updateAction(view.form, true);
    assert.equal(view.priorField.required, true);
    assert.equal(view.moveField.required, false);
    assert.equal(view.moveField.disabled, true);
    assert.match(view.help.textContent, /previous version/i);
});

test('changing the recovery action clears previous replacement consent', () => {
    const view = fixture('restore');
    updateAction(view.form, true);
    assert.equal(view.replace.checked, false);
    assert.equal(view.groups.every(group => group.hidden), true);
    assert.equal(view.priorField.required, false);
});

test('initialization preserves explicit server-rendered replacement choice', () => {
    const view = fixture('restore');
    updateAction(view.form, false);
    assert.equal(view.replace.checked, true);
});

test('Undo Add explains removing the skill while retaining recovery history and PDF', () => {
    const view = fixture('undo-add');
    updateAction(view.form, false);
    assert.equal(view.help.textContent, 'Remove this incorrect entry from skill records. Recovery history and PDF are kept.');
    assert.match(view.help.textContent, /PDF/i);
});

test('editing the action, destination, reason or conflict choice invalidates a displayed preview', () => {
    const button = { disabled: false };
    const confirmation = { checked: true };
    const message = { hidden: true };
    const nodes = { 'recovery-apply': button, ConfirmApply: confirmation, 'recovery-preview-stale': message };
    invalidatePreview({ getElementById: id => nodes[id] });
    assert.equal(button.disabled, true);
    assert.equal(confirmation.checked, false);
    assert.equal(message.hidden, false);
});

test('Move Tab requires only the same-employee destination tab', () => {
    const view = fixture('move-tab');
    updateAction(view.form, true);
    assert.equal(view.tabField.required, true);
    assert.equal(view.tabField.disabled, false);
    assert.equal(view.moveField.disabled, true);
    assert.equal(view.priorField.disabled, true);
    assert.match(view.help.textContent, /same employee/i);
    assert.equal(view.replace.checked, false);
});

test('Replace PDF requires a new file only until a retained upload exists', () => {
    const view = fixture('replace-pdf');
    updateAction(view.form, true);
    assert.equal(view.pdfField.disabled, false);
    assert.equal(view.pdfField.required, true);
    assert.equal(view.uploadId.required, false);
    view.uploadId.value = 'staged-upload-id';
    updateAction(view.form, false);
    assert.equal(view.pdfField.required, false);
    assert.match(view.help.textContent, /skill fields.*unchanged/i);
});

test('other actions do not post or require a retained replacement upload', () => {
    const view = fixture('remove-pdf');
    view.uploadId.value = 'staged-upload-id';
    updateAction(view.form, false);
    assert.equal(view.pdfField.disabled, true);
    assert.equal(view.pdfField.required, false);
    assert.equal(view.uploadId.disabled, true);
    assert.equal(view.uploadId.required, false);
    assert.match(view.help.textContent, /history/i);
});

test('choosing another PDF hides the retained-file card and invalidates Apply', () => {
    const view = fixture('replace-pdf');
    const events = {};
    const pdf = { files: [{ name: 'new.pdf' }], addEventListener: (name, callback) => { events[name] = callback; } };
    const retained = { hidden: false };
    const notice = { hidden: true };
    const apply = { disabled: false };
    const confirmation = { checked: true };
    const nodes = { 'recovery-form': view.form, 'recovery-pdf-file': pdf, 'recovery-staged-pdf': retained,
        'recovery-upload-change': notice, 'recovery-apply': apply, ConfirmApply: confirmation };
    view.form.addEventListener = () => {};
    const groups = view.form.querySelectorAll;
    view.form.querySelectorAll = selector => selector === '[data-recovery-action]' ? [] : groups(selector);
    init({ getElementById: id => nodes[id] || null });
    events.change();
    assert.equal(retained.hidden, true);
    assert.equal(notice.hidden, false);
    assert.equal(apply.disabled, true);
    assert.equal(confirmation.checked, false);
    pdf.files = [];
    events.change();
    assert.equal(retained.hidden, false);
    assert.equal(notice.hidden, true);
});

test('disqualified tab uses the actual 300-character reason limit without truncating input', () => {
    const view = fixture('move-tab');
    const reason = { value: 'x'.repeat(620), maxLength: 500, required: false };
    const reasonHelp = { textContent: '' };
    const target = { value: 'disqualified' };
    const previousQuery = view.form.querySelector;
    view.form.querySelector = selector => ({ '#recovery-reason': reason, '#recovery-reason-help': reasonHelp,
        '#recovery-tab': target })[selector] || previousQuery(selector);
    updateAction(view.form, true);
    assert.equal(reason.maxLength, 300);
    assert.equal(reason.value.length, 620);
    assert.equal(reason.required, false);
    assert.match(reasonHelp.textContent, /300/);
    assert.match(reasonHelp.textContent, /optional/i);
    assert.match(reasonHelp.textContent, /original Remark.*unchanged/i);
    target.value = 'obsolete';
    updateAction(view.form, true);
    assert.equal(reason.maxLength, 500);
    assert.equal(reason.value.length, 620);
    assert.equal(reason.required, false);
});

function dialogFixture(withError = false) {
    const classes = new Set();
    const focus = [];
    const listeners = {};
    const dialog = {
        open: true, dataset: { returnFocus: 'recovery-actions-Current-one' },
        removeAttribute(name) { if (name === 'open') this.open = false; },
        showModal() { this.open = true; this.modalCalls = (this.modalCalls || 0) + 1; },
        close() { this.open = false; listeners.close(); },
        addEventListener(name, callback) { listeners[name] = callback; },
        querySelectorAll() { return [closeButton]; }
    };
    const closeButton = { addEventListener(name, callback) { this[name] = callback; } };
    const nodes = {
        'recovery-dialog': dialog,
        'recovery-dialog-title': { focus: () => focus.push('title') },
        'recovery-actions-Current-one': { focus: () => focus.push('origin') }
    };
    if (withError) nodes['recovery-dialog-error'] = { focus: () => focus.push('error') };
    const windowEvents = {};
    const history = { state: null, replaceState(state, title, value) { this.url = value; } };
    const doc = {
        getElementById: id => nodes[id] || null,
        documentElement: { classList: { add: item => classes.add(item), remove: item => classes.delete(item) } },
        defaultView: {
            location: { href: 'http://localhost/DevTools/SkillRecovery?EmpCode=001&RowId=one&Table=Current&Choice=move',
                replace(value) { this.replaced = value; } },
            history, addEventListener: (name, callback) => { windowEvents[name] = callback; }
        }
    };
    return { doc, dialog, classes, focus, history, windowEvents, closeButton };
}

test('a server-rendered open popup becomes a real modal and locks only its page', () => {
    const view = dialogFixture();
    initDialog(view.doc);
    assert.equal(view.dialog.modalCalls, 1);
    assert.equal(view.dialog.open, true);
    assert.equal(view.classes.has('recovery-modal-open'), true);
    assert.deepEqual(view.focus, ['title']);
});

test('failed popup requests focus the in-dialog error instead of background content', () => {
    const view = dialogFixture(true);
    initDialog(view.doc);
    assert.deepEqual(view.focus, ['error']);
});

test('closing a popup restores the originating action and removes stale popup URL state', () => {
    const view = dialogFixture();
    initDialog(view.doc);
    view.dialog.close();
    assert.equal(view.classes.has('recovery-modal-open'), false);
    assert.equal(view.focus.at(-1), 'origin');
    const url = new URL(view.history.url);
    assert.equal(url.searchParams.get('EmpCode'), '001');
    assert.equal(url.searchParams.has('RowId'), false);
    assert.equal(url.searchParams.has('Choice'), false);
});

test('Close button dismisses locally and preserves canonical source after a POST URL', () => {
    const view = dialogFixture();
    view.doc.defaultView.location.href = 'http://localhost/DevTools/SkillRecovery?handler=Preview';
    view.dialog.dataset.employee = '0012345';
    view.dialog.dataset.filter = 'disqualified';
    initDialog(view.doc);
    view.closeButton.click();
    assert.equal(view.dialog.open, false);
    const url = new URL(view.history.url);
    assert.equal(url.searchParams.get('EmpCode'), '0012345');
    assert.equal(url.searchParams.get('Filter'), 'disqualified');
    assert.equal(url.searchParams.has('handler'), false);
});

test('closing a POST-rendered popup replaces the document with a clean GET before reload', () => {
    const view = dialogFixture();
    view.doc.defaultView.location.href = 'http://localhost/DevTools/SkillRecovery?handler=Preview';
    Object.assign(view.dialog.dataset, { postback: 'true', employee: '0012345', filter: 'disqualified' });
    initDialog(view.doc);
    view.closeButton.click();
    assert.ok(view.doc.defaultView.location.replaced);
    const url = new URL(view.doc.defaultView.location.replaced);
    assert.equal(url.pathname, '/DevTools/SkillRecovery');
    assert.equal(url.searchParams.get('EmpCode'), '0012345');
    assert.equal(url.searchParams.get('Filter'), 'disqualified');
    assert.equal(url.searchParams.has('handler'), false);
    assert.equal(url.hash, '#recovery-actions-Current-one');
    assert.equal(view.history.url, undefined);
    assert.equal(view.classes.has('recovery-modal-open'), false);
});

test('the clean GET restores keyboard focus to its permitted recovery hash target', () => {
    let focused = 0;
    const document = {
        defaultView: { location: { href: 'http://localhost/DevTools/SkillRecovery?EmpCode=0012345#recovery-actions-Current-one' } },
        getElementById: id => id === 'recovery-actions-Current-one' ? { focus() { focused++; } } : null
    };
    restoreFocusFromHash(document);
    assert.equal(focused, 1);
});
