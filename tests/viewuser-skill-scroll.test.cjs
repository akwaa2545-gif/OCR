const { test } = require('node:test');
const assert = require('node:assert/strict');
const { availableHeight, resizedColumnWidth, captureColumnWidths, freezeVisibleColumns } = require('../OperatorCertificationRecord.Web/wwwroot/js/viewuser-skill-scroll.js');

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
