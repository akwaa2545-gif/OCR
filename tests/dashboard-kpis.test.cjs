'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { init } = require('../OperatorCertificationRecord.Web/wwwroot/js/dashboard-kpis.js');

function fixture(data = { ta: { Operator: 11, Process: 2, Skill: 31, Handicap: 1, OneSkill: 3, NoSkill: 4 } }) {
    const elements = new Map();
    function element(id, value = '') {
        const classes = new Set();
        const attributes = new Map([['data-all', value]]);
        const result = { textContent: value, classList: { add: value => classes.add(value), remove: value => classes.delete(value), contains: value => classes.has(value) },
            getAttribute: key => attributes.get(key), setAttribute: (key, value) => attributes.set(key, value),
            dataset: {}, classes };
        elements.set(id, result); return result;
    }
    ['Operator', 'Process', 'Skill', 'Handicap', 'OneSkill', 'NoSkill'].forEach((key, i) => element('kpi' + key, String([1234, 22, 5678, 3, 44, 55][i])));
    const buttons = ['all', 'ta', 'qa', 'sc', 'planning'].map(key => { const button = element('button-' + key); button.dataset.department = key; return button; });
    const cards = ['pcTa', 'pcQa', 'pcSc', 'pcPlanning'].map(key => element(key));
    element('dashboard-department-kpis').textContent = JSON.stringify(data);
    element('kpi-filter-status');
    const doc = { getElementById: id => elements.get(id) ?? null, querySelectorAll: selector => selector === '.kpi-dept-btn' ? buttons : cards };
    const win = {}; init(doc, win);
    return { win, elements, buttons, cards };
}

test('department counts come from the supplied database snapshot, then restore exact all-data totals', () => {
    const f = fixture();
    f.win.filterKpiByDept('ta', f.buttons[1]);
    assert.equal(f.elements.get('kpiOperator').textContent, '11');
    assert.equal(f.elements.get('kpiSkill').textContent, '31');
    assert.equal(f.buttons[1].getAttribute('aria-pressed'), 'true');
    f.win.filterKpiByDept('ta', f.buttons[1]);
    assert.equal(f.elements.get('kpiOperator').textContent, '1,234');
    assert.equal(f.elements.get('kpiSkill').textContent, '5,678');
    assert.equal(f.buttons[0].getAttribute('aria-pressed'), 'true');
});

test('missing or invalid department data preserves real values and shows an error', () => {
    for (const data of [{}, { ta: { Operator: 11 } }]) {
        const f = fixture(data);
        f.win.filterKpiByDept('ta', f.buttons[1]);
        assert.equal(f.elements.get('kpiSkill').textContent, '5,678');
        assert.match(f.elements.get('kpi-filter-status').textContent, /unavailable/i);
        assert.equal(f.buttons[0].getAttribute('aria-pressed'), 'true');
    }
});

test('process cards and department buttons share selection without needing a browser event global', () => {
    const f = fixture();
    f.win.handleProcessClick('Ta Manufacturing');
    assert.equal(f.elements.get('kpiSkill').textContent, '31');
    assert.equal(f.cards[0].classes.has('active-dept'), true);
    f.win.handleProcessClick('Ta Manufacturing');
    assert.equal(f.elements.get('kpiSkill').textContent, '5,678');
    assert.equal(f.cards[0].classes.has('active-dept'), false);
});

test('guest page without KPI markup initializes without throwing', () => {
    const win = {};
    assert.doesNotThrow(() => init({ getElementById: () => null }, win));
});

test('unavailable all-data attributes do not turn placeholders into zero counts', () => {
    const f = fixture();
    for (const key of ['Operator', 'Process', 'Skill', 'Handicap', 'OneSkill', 'NoSkill']) {
        f.elements.get('kpi' + key).setAttribute('data-all', '');
        f.elements.get('kpi' + key).textContent = '—';
    }
    init({ getElementById: id => f.elements.get(id), querySelectorAll: () => [] }, f.win);
    f.win.filterKpiByDept('all');
    assert.equal(f.elements.get('kpiSkill').textContent, '—');
});
