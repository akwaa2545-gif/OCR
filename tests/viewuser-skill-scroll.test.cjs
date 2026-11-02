const { test } = require('node:test');
const assert = require('node:assert/strict');
const { availableHeight, resizedColumnWidth, captureColumnWidths, freezeVisibleColumns, setFrozenColumns, setDensity, setFitMode, columnPercentages, init, attachHorizontalScroll } = require('../OperatorCertificationRecord.Web/wwwroot/js/viewuser-skill-scroll.js');

function fixture(widths, visible = true) {
    const events = [];
    function style(name) {
        return new Proxy({}, { set(target, key, value) { events.push(`write:${name}.${key}`); target[key] = value; return true; } });
    }
    const headers = widths.map((width, index) => ({
        style: style(`header${index}`),
        getBoundingClientRect() { events.push(`read:header${index}`); return { width }; }
    }));
    const group = { children: widths.map((_, index) => ({ style: style(`col${index}`) })), appendChild(col) { this.children.push(col); } };
    const table = {
        style: style('table'), offsetWidth: visible ? 900 : 0,
        getClientRects: () => visible ? [{}] : [],
        querySelectorAll: () => headers,
        querySelector: () => group,
        ownerDocument: { createElement: () => ({ style: style('created') }) }
    };
    return { table, headers, group, events };
}

test('viewport height keeps the native scrollbar above the bottom margin', () => {
    assert.equal(availableHeight(704, 502), 178);
    assert.equal(502 + availableHeight(704, 502), 680);
    assert.equal(availableHeight(768, -100), 560);
    assert.equal(availableHeight(300, 250), 96);
});

test('short laptop viewport keeps a visible table scrollbar inside the viewport', () => {
    assert.equal(availableHeight(648, 502), 122);
    assert.equal(502 + availableHeight(648, 502), 624);
    assert.ok(502 + availableHeight(648, 502) <= 648);
});

test('shrinking an initially narrow K/S column never grows it to the general minimum', () => {
    assert.equal(resizedColumnWidth(26, 36), 36);
    assert.equal(resizedColumnWidth(35, 36), 36);
    assert.equal(resizedColumnWidth(46, 36), 46);
    assert.equal(resizedColumnWidth(30, 160), 40);
});

test('hidden tab measurements are deferred without creating zero-width columns', () => {
    const { table, events } = fixture([0, 0, 0], false);
    assert.equal(captureColumnWidths(table), null);
    assert.equal(freezeVisibleColumns(table), null);
    assert.deepEqual(events, []);
});

test('a nonpositive visible measurement cannot partially freeze a table', () => {
    const { table, events } = fixture([160, 0, 36]);
    assert.equal(freezeVisibleColumns(table), null);
    assert.equal(events.filter(event => event.startsWith('write:')).length, 0);
});

test('all column widths are read before layout changes and narrow K/S columns are preserved', () => {
    const { table, group, headers, events } = fixture([160, 140, 90, 36, 36]);
    const state = freezeVisibleColumns(table);
    assert.deepEqual(state.widths, [160, 140, 90, 36, 36]);
    assert.equal(table.style.tableLayout, 'fixed');
    assert.equal(table.style.width, '462px');
    assert.equal(headers[3].style.width, '36px');
    assert.equal(group.children[3].style.width, '36px');
    assert.equal(events.slice(0, 5).every(event => event.startsWith('read:')), true);
    assert.equal(events.slice(5).every(event => event.startsWith('write:')), true);
});

test('column resizing has a reasonable upper limit and supports readable process minimums', () => {
    assert.equal(resizedColumnWidth(3000, 200), 640);
    assert.equal(resizedColumnWidth(60, 220, 160), 160);
});

test('frozen Process follows the resized selection column without freezing other cells', () => {
    const cell = () => ({ classList: { values: new Set(), add(value) { this.values.add(value); } } });
    const rows = [{ cells: [cell(), cell(), cell()] }, { cells: [cell(), cell(), cell()] }];
    const properties = {};
    const table = { rows, style: { setProperty: (key, value) => properties[key] = value } };
    const state = { headers: [{ dataset: { columnKey: 'selection' } }, { dataset: { columnKey: 'process' } }, { dataset: {} }], widths: [52, 240, 100] };
    setFrozenColumns(table, state);
    assert.equal(properties['--vu-selection-width'], '52px');
    for (const row of rows) {
        assert.equal(row.cells[0].classList.values.has('vu-frozen-selection'), true);
        assert.equal(row.cells[1].classList.values.has('vu-frozen-process'), true);
        assert.equal(row.cells[2].classList.values.size, 0);
    }
    state.widths[0] = 72;
    setFrozenColumns(table, state);
    assert.equal(properties['--vu-selection-width'], '72px');
});

test('tables without checkboxes freeze Process at the left edge', () => {
    const properties = {};
    const table = { rows: [], style: { setProperty: (key, value) => properties[key] = value } };
    setFrozenColumns(table, { headers: [{ dataset: { columnKey: 'process' } }], widths: [220] });
    assert.equal(properties['--vu-selection-width'], '0px');
});

test('density is restricted to supported choices and blocked storage is harmless', () => {
    const page = { dataset: {} };
    const buttons = ['compact', 'comfortable'].map(value => ({ dataset: { tableDensity: value }, attributes: {}, setAttribute(key, text) { this.attributes[key] = text; } }));
    setDensity(page, buttons, 'compact', { setItem() { throw new Error('denied'); } });
    assert.equal(page.dataset.tableDensity, 'compact');
    assert.equal(buttons[0].attributes['aria-pressed'], 'true');
    assert.equal(buttons[1].attributes['aria-pressed'], 'false');
    setDensity(page, buttons, 'malicious-value', null);
    assert.equal(page.dataset.tableDensity, 'comfortable');
});

test('Fit allocates every column a bounded share with more room for Process than K/S', () => {
    const headers = ['selection', 'Process', 'Classification', 'Theory', 'OJT', 'Result', 'K', 'S', 'Judgment', 'Certified Date', 'Expiry Date', 'Verifier', 'Remark', 'Download'];
    const widths = columnPercentages(headers);
    assert.equal(widths.length, headers.length);
    assert.ok(Math.abs(widths.reduce((sum, width) => sum + width, 0) - 100) < 0.001);
    assert.ok(widths.every(width => width > 0 && width < 20));
    assert.ok(widths[1] > widths[6]);
    assert.ok(widths[9] > widths[6]);
});

test('Fit applies to hidden tabs too and offers Expand as the opt-in action', () => {
    const wrappers = [true, false, false].map(visible => ({ visible, classes: new Set(), scrollLeft: 450, classList: { toggle(value, on) { on ? this.owner.classes.add(value) : this.owner.classes.delete(value); } } }));
    wrappers.forEach(wrapper => wrapper.classList.owner = wrapper);
    const label = { textContent: '' }; const attributes = {};
    const button = { classList: { toggle() {} }, setAttribute: (key, value) => attributes[key] = value, querySelector: selector => selector === 'span' ? label : null };
    setFitMode(wrappers, button, true);
    assert.ok(wrappers.every(wrapper => wrapper.classes.has('skill-table-fit')));
    assert.ok(wrappers.every(wrapper => wrapper.scrollLeft === 0));
    assert.equal(attributes['aria-pressed'], 'true'); assert.equal(label.textContent, 'Expand');
    setFitMode(wrappers, button, false);
    assert.ok(wrappers.every(wrapper => !wrapper.classes.has('skill-table-fit')));
    assert.equal(attributes['aria-pressed'], 'false'); assert.equal(label.textContent, 'Fit');
});

test('each visit starts compact and fitted even when storage contains Comfortable', () => {
    const page = { dataset: {} };
    const wrappers = [0, 1].map(() => ({ classes: new Set(), classList: { toggle(value, on) { on ? this.owner.classes.add(value) : this.owner.classes.delete(value); } }, querySelector: () => null, getAttribute: () => 'Skills', ownerDocument: { createElement: () => ({ setAttribute() {}, appendChild() {}, addEventListener() {} }) }, parentNode: { insertBefore() {} }, addEventListener() {} }));
    wrappers.forEach(wrapper => wrapper.classList.owner = wrapper);
    const doc = { querySelectorAll: selector => selector === '.vu-skill-scroll' ? wrappers : [], querySelector: selector => selector === '.vu-page' ? page : null, getElementById: () => null, addEventListener() {} };
    const win = { localStorage: { getItem: () => 'comfortable' }, addEventListener() {}, requestAnimationFrame() {} };
    init(doc, win);
    assert.equal(page.dataset.tableDensity, 'compact');
    assert.ok(wrappers.every(wrapper => wrapper.classes.has('skill-table-fit')));
});

test('top scrollbar tracks both directions and hides when no overflow or Fit is active', () => {
    function element() { return { style: {}, listeners: {}, setAttribute() {}, appendChild() {}, addEventListener(type, callback) { this.listeners[type] = callback; } }; }
    const top = element(); const spacer = element();
    const doc = { createElement: () => doc.created++ === 0 ? top : spacer, created: 0 };
    let fit = false;
    const wrapper = { ...element(), ownerDocument: doc, clientWidth: 700, scrollWidth: 1400, scrollLeft: 120, getAttribute: () => 'Current skills', classList: { contains: () => fit }, parentNode: { insertBefore(node, target) { assert.equal(node, top); assert.equal(target, wrapper); } } };
    const refresh = attachHorizontalScroll(wrapper);
    refresh();
    assert.equal(top.hidden, false);
    assert.equal(top.style.width, '700px');
    assert.equal(spacer.style.width, '1400px');
    assert.equal(top.scrollLeft, 120);
    top.scrollLeft = 400; top.listeners.scroll();
    assert.equal(wrapper.scrollLeft, 400);
    wrapper.scrollLeft = 200; wrapper.listeners.scroll();
    assert.equal(top.scrollLeft, 200);
    fit = true; refresh(); assert.equal(top.hidden, true);
    fit = false; wrapper.scrollWidth = 700; refresh(); assert.equal(top.hidden, true);
});
