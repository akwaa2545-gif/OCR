const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/Pages/DownloadSection.cshtml'), 'utf8');
const openScript = source.slice(source.indexOf('(function initOperatorNoSkillModal()'), source.indexOf('\nfunction setSkillPreset'));
const validation = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/wwwroot/js/download-preview.js'), 'utf8');

test('shared modals can validate dates without loading DownloadSection', () => {
    const context = { document: { getElementById: () => null } };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/wwwroot/js/download-preview.js'), 'utf8'), context);
    assert.equal(typeof context.validateDateRange, 'function');
});

function element() {
    const listeners = new Map(), classes = new Set();
    return {
        value: '', disabled: false, style: {}, attributes: {}, events: [],
        classList: { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x) },
        addEventListener(type, callback) { if (!listeners.has(type)) listeners.set(type, new Set()); listeners.get(type).add(callback); },
        removeEventListener(type, callback) { listeners.get(type)?.delete(callback); },
        dispatchEvent(event) { this.events.push(event.type); for (const callback of [...(listeners.get(event.type) || [])]) callback.call(this, event); return !event.defaultPrevented; },
        setAttribute(name, value) { this.attributes[name] = value; }, removeAttribute(name) { delete this.attributes[name]; },
        focus() { this.focused = true; }, remove() { this.removed = true; },
        querySelector() { return null; }, querySelectorAll() { return []; }
    };
}
class FakeEvent {
    constructor(type, options = {}) { Object.assign(this, options); this.type = type; this.defaultPrevented = false; }
    preventDefault() { this.defaultPrevented = true; }
}
function opening(bootstrap) {
    const modal = element(), button = element(), close = element(), document = element(), body = element();
    const children = [], timers = [];
    document.readyState = 'complete'; document.body = body; document.activeElement = button;
    document.getElementById = id => id === 'operatorNoSkillModal' ? modal : id === 'operatorNoSkillBtn' ? button : null;
    document.createElement = () => element();
    body.appendChild = child => children.push(child);
    modal.querySelector = () => close; modal.querySelectorAll = () => [close];
    const context = { document, Event: FakeEvent, console: { debug() {}, warn() {}, error() {} }, setTimeout: callback => timers.push(callback) };
    context.window = context; context.bootstrap = bootstrap;
    vm.runInNewContext(openScript, context);
    return { modal, button, close, document, children, timers, click(node) { node.dispatchEvent(new FakeEvent('click', { target: node })); } };
}

test('No-skill waits for Bootstrap animation instead of opening a competing fallback', () => {
    let shows = 0;
    class Modal { static getInstance() { return null; } show() { shows++; } }
    const page = opening({ Modal });
    page.click(page.button);
    page.timers.forEach(callback => callback());
    assert.equal(shows, 1);
    assert.equal(page.children.length, 0);
    assert.equal(page.modal.events.includes('shown.bs.modal'), false);
});

test('fallback close buttons remove backdrop and emit lifecycle events', () => {
    const page = opening();
    page.click(page.button);
    assert.equal(page.modal.classList.contains('show'), true);
    page.click(page.close);
    assert.equal(page.modal.classList.contains('show'), false);
    assert.equal(page.children[0].removed, true);
    assert.deepEqual(page.modal.events, ['show.bs.modal', 'shown.bs.modal', 'hide.bs.modal', 'hidden.bs.modal']);
    assert.equal(page.button.focused, true);
    page.click(page.button); page.click(page.close);
    assert.equal(page.modal.events.filter(x => x === 'hidden.bs.modal').length, 2);
});

test('fallback Escape closes and restores scrolling', () => {
    const page = opening(); page.document.body.style.overflow = 'auto';
    page.click(page.button);
    page.document.dispatchEvent(new FakeEvent('keydown', { key: 'Escape' }));
    assert.equal(page.modal.classList.contains('show'), false);
    assert.equal(page.document.body.style.overflow, 'auto');
});

function validateFixture() {
    const nodes = Object.fromEntries(['start','end','error','preview','download'].map(id => [id, element()]));
    const context = { document: { getElementById: id => nodes[id] } };
    vm.runInNewContext(validation, context);
    return { nodes, validate: () => context.validateDateRange('start','end','error','preview','download') };
}

test('valid dates do not enable Download while the count is pending', () => {
    const { nodes, validate } = validateFixture();
    nodes.start.value = '2026-01-01'; nodes.end.value = '2026-10-30';
    nodes.download.classList.add('dl-pending');
    assert.equal(validate(), true);
    assert.equal(nodes.download.disabled, true);
    nodes.download.classList.remove('dl-pending'); validate();
    assert.equal(nodes.download.disabled, false);
});

test('one-sided dates are invalid, both empty remain available for No-skill All time', () => {
    const { nodes, validate } = validateFixture();
    nodes.start.value = '2026-01-01';
    assert.equal(validate(), false);
    assert.equal(nodes.download.disabled, true);
    assert.match(nodes.error.textContent, /both/i);
    nodes.start.value = ''; assert.equal(validate(), true);
    assert.equal(nodes.error.classList.contains('visible'), false);
});
