const { test } = require('node:test');
const assert = require('node:assert/strict');
const { createState, snapshot, init, submit } = require('../OperatorCertificationRecord.Web/wwwroot/js/employee-forms.js');

test('editing and restoring a field accurately tracks unsaved changes', () => {
    let value = 'Before';
    const state = createState(() => value);
    assert.equal(state.dirty(), false);
    value = 'After';
    assert.equal(state.dirty(), true);
    value = 'Before';
    assert.equal(state.dirty(), false);
});

test('a canceled invalid submission remains dirty and retryable', () => {
    let value = 'Before';
    const state = createState(() => value);
    value = 'After';
    assert.equal(state.begin(true), false);
    assert.equal(state.busy(), false);
    assert.equal(state.dirty(), true);
    assert.equal(state.begin(false), true);
    assert.equal(state.begin(false), false);
});

test('an accepted submission suppresses leave warnings and duplicate saves', () => {
    let value = 'Before';
    const state = createState(() => value);
    value = 'After';
    assert.equal(state.begin(false), true);
    assert.equal(state.dirty(), false);
    assert.equal(state.begin(false), false);
});

test('history restoration releases the save lock without losing the dirty baseline', () => {
    let value = 'Before';
    const state = createState(() => value);
    value = 'After';
    state.begin(false);
    state.restore();
    assert.equal(state.busy(), false);
    assert.equal(state.dirty(), true);
    assert.equal(state.begin(false), true);
});

test('reset state keeps the original persisted values as the comparison baseline', () => {
    let value = 'Before';
    const state = createState(() => value);
    value = 'After';
    state.restore();
    value = 'Before';
    assert.equal(state.dirty(), false);
});

test('file identity, checkboxes, and multiple selects participate in change detection', () => {
    const file = { name: 'certificate.pdf', size: 120, lastModified: 12 };
    const fields = [
        { name: '__RequestVerificationToken', type: 'hidden', value: 'one' },
        { name: 'action', type: 'hidden', value: 'update' },
        { name: 'SkillFile', type: 'file', files: [] },
        { name: 'Enabled', type: 'checkbox', checked: false },
        { name: 'Tags', type: 'select-multiple', selectedOptions: [{ value: 'a' }] }
    ];
    const form = { elements: fields };
    const before = snapshot(form);
    fields[0].value = 'rotated-token';
    fields[1].value = 'promote';
    assert.equal(snapshot(form), before);
    fields[2].files = [file];
    assert.notEqual(snapshot(form), before);
    const withFile = snapshot(form);
    fields[2].files = [{ ...file, lastModified: 13 }];
    assert.notEqual(snapshot(form), withFile);
    fields[2].files = [];
    fields[3].checked = true;
    assert.notEqual(snapshot(form), before);
    fields[3].checked = false;
    fields[4].selectedOptions = [{ value: 'b' }];
    assert.notEqual(snapshot(form), before);
});

function page() {
    const documentEvents = new Map(), windowEvents = new Map(), formEvents = new Map();
    const control = { name: 'Remark', type: 'text', value: 'Before', closest: () => null };
    const button = { name: 'action', type: 'submit', value: 'addskill', disabled: false,
        setAttribute() {}, removeAttribute() {}, closest: () => null };
    const statuses = [];
    let valid = true, posts = 0;
    const form = { elements: [control, button], ownerDocument: null,
        classList: { toggle() {} }, setAttribute() {},
        querySelectorAll: selector => selector === '[data-valmsg-for]' ? [] : [button],
        querySelector: () => statuses[0],
        appendChild(element) { if (element.type !== 'submit') statuses.push(element); },
        addEventListener(name, callback) { const events = formEvents.get(name) || []; events.push(callback); formEvents.set(name, events); },
        checkValidity: () => valid,
        requestSubmit(submitter) {
            const event = { target: form, submitter, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
            for (const listener of formEvents.get('submit') || []) listener(event);
            documentEvents.get('submit')(event);
            if (!event.defaultPrevented) posts++;
        }
    };
    const document = { querySelectorAll: () => [form],
        addEventListener: (name, callback) => documentEvents.set(name, callback),
        createElement() { return { dataset: {}, setAttribute() {}, remove() {} }; },
        getElementById: () => null
    };
    form.ownerDocument = document;
    const window = { document, setTimeout: callback => callback(),
        addEventListener: (name, callback) => windowEvents.set(name, callback), confirm: () => false };
    init(window);
    return { form, button, control, formEvents, documentEvents, windowEvents,
        posts: () => posts, setValid: value => valid = value,
        change() { for (const listener of formEvents.get('change') || []) listener({ target: control }); },
        leave() { const event = { prevented: false, preventDefault() { this.prevented = true; } }; windowEvents.get('beforeunload')(event); return event; }
    };
}

test('a page validator can cancel save without locking the form or losing action values', () => {
    const view = page();
    let canceled = true;
    view.form.addEventListener('submit', event => { if (canceled) event.preventDefault(); });
    assert.equal(submit(view.form), false);
    assert.equal(view.button.disabled, false);
    assert.equal(view.button.name, 'action');
    assert.equal(view.button.value, 'addskill');
    canceled = false;
    assert.equal(submit(view.form), true);
    assert.equal(submit(view.form), false);
    assert.equal(view.posts(), 1);
});

test('native constraint failure remains retryable after correction', () => {
    const view = page();
    view.setValid(false);
    assert.equal(submit(view.form), false);
    assert.equal(view.posts(), 0);
    view.setValid(true);
    assert.equal(submit(view.form), true);
    assert.equal(view.posts(), 1);
});

test('confirmed employee transitions preserve their explicit validation bypass', () => {
    const view = page();
    view.setValid(false);
    assert.equal(submit(view.form, { validate: false }), true);
    assert.equal(view.posts(), 1);
});

test('initial async field hydration does not warn before any user edit', () => {
    const view = page();
    view.control.value = 'Hydrated';
    assert.equal(view.leave().prevented, false);
});

test('changed fields warn on close, accepted saves do not, and history return unlocks', () => {
    const view = page();
    view.control.value = 'After'; view.change();
    assert.equal(view.leave().prevented, true);
    submit(view.form);
    assert.equal(view.leave().prevented, false);
    view.windowEvents.get('pageshow')();
    assert.equal(view.leave().prevented, true);
    assert.equal(submit(view.form), true);
});

test('canceling navigation stops the global navigation animation before it starts', () => {
    const view = page();
    view.control.value = 'After'; view.change();
    const anchor = { target: '', hasAttribute: () => false, getAttribute: () => '/ViewUser?empCode=101' };
    const event = { button: 0, target: { closest: selector => selector === 'a[href]' ? anchor : null },
        preventDefault() { this.prevented = true; }, stopImmediatePropagation() { this.stopped = true; } };
    view.documentEvents.get('click')(event);
    assert.equal(event.prevented, true);
    assert.equal(event.stopped, true);
});

test('date changes remain visible in dirty snapshots when Flatpickr hides the source input', () => {
    const date = { name: 'CertifiedDate', type: 'hidden', value: '2026-10-30', _flatpickr: {} };
    const form = { elements: [date] }, before = snapshot(form);
    date.value = '2026-10-31';
    assert.notEqual(snapshot(form), before);
});
