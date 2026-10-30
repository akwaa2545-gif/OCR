'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const catalog = require('../OperatorCertificationRecord.Web/wwwroot/js/admin-catalog.js');

test('search matches workshop names and retains every process in that workshop', () => {
    const result = catalog.matchWorkshop('Inspection', ['Visual check', 'Packing'], ' INSPECTION ');
    assert.deepEqual(result, { visible: true, processes: [true, true] });
});

test('process search ignores case and normalizes Unicode including Thai names', () => {
    assert.deepEqual(catalog.matchWorkshop('Manufacturing', ['POL coating', 'Packing'], 'pol'),
        { visible: true, processes: [true, false] });
    assert.deepEqual(catalog.matchWorkshop('งานผลิต', ['ตรวจสอบคุณภาพ', 'บรรจุ'], 'ตรวจสอบ'),
        { visible: true, processes: [true, false] });
    assert.equal(catalog.matchWorkshop('Caf\u00e9', [], 'Cafe\u0301').visible, true);
});

test('empty groups remain visible without a query and unmatched groups disappear', () => {
    assert.deepEqual(catalog.matchWorkshop('Empty workshop', [], ''), { visible: true, processes: [] });
    assert.deepEqual(catalog.matchWorkshop('Inspection', ['Packing'], 'missing'),
        { visible: false, processes: [false] });
});

function node(extra = {}) {
    return Object.assign({ hidden: false, open: false, textContent: '', value: '', checked: false,
        dataset: {}, listeners: {}, addEventListener(name, callback) { this.listeners[name] = callback; },
        focus() { this.focused = true; }, setAttribute(name, value) { this[name] = value; } }, extra);
}

function browserFixture(includeUnassigned = false) {
    const process = name => node({ dataset: { processName: name, processId: name === 'Coating' ? 'P042' : '' }, textContent: name + ' Move' });
    const group = (name, names, open) => node({ open, dataset: { workshopName: name },
        rows: names.map(process), querySelectorAll() { return this.rows; } });
    const groups = [group('Inspection', ['Visual check', 'Packing'], false), group('Polymer', ['Coating'], true)];
    if (includeUnassigned) {
        const unassigned = group('Processes awaiting a workshop', ['Unassigned check'], false);
        unassigned.dataset.catalogUnassigned = '';
        groups.push(unassigned);
    }
    const search = node();
    const status = node();
    const empty = node({ hidden: true });
    const clear = node();
    const expand = node();
    const browser = node({ querySelectorAll: () => groups,
        querySelector: selector => ({ '[data-catalog-clear]': clear, '[data-catalog-expand]': expand })[selector] });
    const document = { getElementById: id => ({ 'catalog-search': search, 'catalog-search-status': status,
        'catalog-no-results': empty })[id] };
    catalog.initBrowser(browser, document);
    return { groups, search, status, empty, clear, expand };
}

test('filtering opens matches, hides unrelated rows and clearing restores original expansion', () => {
    const fixture = browserFixture();
    fixture.search.value = 'visual';
    fixture.search.listeners.input();
    assert.equal(fixture.groups[0].open, true);
    assert.equal(fixture.groups[0].rows[1].hidden, true);
    assert.equal(fixture.groups[1].hidden, true);
    assert.match(fixture.status.textContent, /1 workshop.*1 process/);
    fixture.clear.listeners.click();
    assert.equal(fixture.search.value, '');
    assert.equal(fixture.groups[0].open, false);
    assert.equal(fixture.groups[1].open, true);
    assert.equal(fixture.groups[0].rows[1].hidden, false);
    assert.equal(fixture.search.focused, true);
});

test('zero search results show an empty message and accurate counts', () => {
    const fixture = browserFixture();
    fixture.search.value = 'missing';
    fixture.search.listeners.input();
    assert.equal(fixture.empty.hidden, false);
    assert.match(fixture.status.textContent, /0 workshops.*0 processes/);
    assert.equal(fixture.expand.disabled, true);
});

test('unassigned processes count as processes without inventing an additional workshop', () => {
    const fixture = browserFixture(true);
    assert.match(fixture.status.textContent, /2 workshops.*4 processes/);
    fixture.search.value = 'unassigned';
    fixture.search.listeners.input();
    assert.match(fixture.status.textContent, /0 workshops.*1 process/);
    assert.equal(fixture.empty.hidden, true);
    assert.equal(fixture.groups[2].hidden, false);
});

test('search matches process IDs without treating action button text as process data', () => {
    const fixture = browserFixture();
    fixture.search.value = 'p042';
    fixture.search.listeners.input();
    assert.equal(fixture.groups[1].hidden, false);
    assert.match(fixture.status.textContent, /1 workshop.*1 process/);
    fixture.search.value = 'Move';
    fixture.search.listeners.input();
    assert.equal(fixture.empty.hidden, false);
});

test('expand all toggles visible groups and updates after native details toggles', () => {
    const fixture = browserFixture();
    fixture.expand.listeners.click();
    assert.ok(fixture.groups.every(group => group.open));
    assert.equal(fixture.expand.textContent, 'Collapse all');
    fixture.groups[0].open = false;
    fixture.groups[0].listeners.toggle();
    assert.equal(fixture.expand.textContent, 'Expand all');
});

test('mode switching clears move consent and updates required, disabled and submit label', () => {
    const choice = node({ value: 'move' });
    const confirmation = node({ checked: true });
    const submit = node();
    const groups = ['new', 'move'].map(mode => node({ dataset: { catalogModeFields: mode },
        fields: [node()], querySelectorAll() { return this.fields; } }));
    const form = { querySelector: selector => selector.includes(':checked') ? choice : submit,
        querySelectorAll: () => groups };
    catalog.updateMode(form, confirmation, true);
    assert.equal(confirmation.checked, false);
    assert.equal(groups[0].fields[0].disabled, true);
    assert.equal(groups[0].fields[0].required, false);
    assert.equal(groups[1].fields[0].disabled, false);
    assert.equal(groups[1].fields[0].required, true);
    assert.equal(submit.textContent, 'Move process');
    confirmation.checked = true;
    choice.value = 'new';
    catalog.updateMode(form, confirmation, true);
    assert.equal(confirmation.checked, false);
    assert.equal(groups[1].hidden, true);
    assert.equal(submit.textContent, 'Add process');
});

function editorFixture() {
    const confirmation = node({ checked: true });
    const destination = node({ value: 'target', selectedOptions: [node({ textContent: 'Target workshop' })] });
    const existing = node({ value: 'process-key', selectedOptions: [node({ dataset: {
        processName: 'Visual check', sourceWorkshop: 'Inspection' } })] });
    const summary = node();
    const name = node();
    const label = node();
    const radios = ['new', 'move'].map(value => node({ value, checked: value === 'new' }));
    const groups = ['new', 'move'].map(mode => node({ dataset: { catalogModeFields: mode },
        fields: [node()], querySelectorAll() { return this.fields; } }));
    const form = { querySelector: selector => selector.includes(':checked') ? radios.find(radio => radio.checked) : label,
        querySelectorAll: selector => selector === '[data-catalog-mode]' ? radios : groups };
    const buttons = [node({ dataset: { catalogAddTo: 'inspection' }, hasAttribute: () => false }),
        node({ dataset: { catalogMoveProcess: 'another-key' }, hasAttribute: () => true })];
    const document = {
        getElementById: id => ({ 'catalog-confirm-move': confirmation, 'catalog-process-existing': existing,
            'catalog-process-workshop': destination, 'catalog-process-name': name, 'catalog-move-summary': summary })[id],
        querySelectorAll: selector => selector === '[data-catalog-form]' ? [form] :
            selector === '[data-catalog-browser]' ? [] : buttons
    };
    catalog.init(document);
    return { confirmation, destination, existing, summary, name, radios, buttons, label };
}

test('changing source or destination invalidates consent before updating the move preview', () => {
    const fixture = editorFixture();
    fixture.destination.listeners.change();
    assert.equal(fixture.confirmation.checked, false);
    assert.equal(fixture.summary.textContent, 'Visual check: Inspection \u2192 Target workshop');
    fixture.confirmation.checked = true;
    fixture.existing.listeners.change();
    assert.equal(fixture.confirmation.checked, false);
});

test('contextual add and move select the editor mode and focus the enabled next field', () => {
    const fixture = editorFixture();
    fixture.buttons[0].listeners.click();
    assert.equal(fixture.destination.value, 'inspection');
    assert.equal(fixture.radios[0].checked, true);
    assert.equal(fixture.name.focused, true);
    assert.equal(fixture.confirmation.checked, false);
    fixture.confirmation.checked = true;
    fixture.buttons[1].listeners.click();
    assert.equal(fixture.existing.value, 'another-key');
    assert.equal(fixture.destination.value, 'inspection');
    assert.equal(fixture.radios[1].checked, true);
    assert.equal(fixture.destination.focused, true);
    assert.equal(fixture.confirmation.checked, false);
    assert.equal(fixture.label.textContent, 'Move process');
});
