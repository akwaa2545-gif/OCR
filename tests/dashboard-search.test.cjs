'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { init } = require('../OperatorCertificationRecord.Web/wwwroot/js/dashboard-search.js');

class Element {
    constructor(tag = 'div') {
        this.tagName = tag.toUpperCase(); this.children = []; this.listeners = {}; this.attributes = {};
        this.style = {}; this.value = ''; this.className = ''; this._text = ''; this.focused = false;
    }
    set textContent(value) { this._text = String(value); this.children = []; }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
    set innerHTML(value) { throw new Error('Search rendering must not interpret HTML: ' + value); }
    appendChild(child) { this.children.push(child); return child; }
    replaceChildren(...children) { this._text = ''; this.children = children; }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    addEventListener(name, handler) { (this.listeners[name] ??= []).push(handler); }
    dispatch(name, options = {}) { for (const handler of this.listeners[name] ?? []) handler({ preventDefault() {}, ...options }); }
    focus() { this.focused = true; }
}

function fixture() {
    const nodes = Object.fromEntries(['qs-input', 'qs-clear-btn', 'qs-spinner', 'qs-results-area', 'qs-search-btn']
        .map(id => [id, new Element(id === 'qs-input' ? 'input' : 'div')]));
    const hint = new Element(); const timers = new Map(); const pending = []; let nextTimer = 0;
    const document = {
        getElementById: id => nodes[id] ?? null,
        querySelector: selector => selector === '.qs-hint' ? hint : null,
        createElement: tag => new Element(tag),
        defaultView: {
            AbortController,
            setTimeout(callback) { timers.set(++nextTimer, callback); return nextTimer; },
            clearTimeout(id) { timers.delete(id); },
            fetch(url, options) { return new Promise((resolve, reject) => pending.push({ url, options, resolve, reject })); }
        }
    };
    init(document);
    return { nodes, hint, document, pending,
        input(value) { nodes['qs-input'].value = value; nodes['qs-input'].dispatch('input'); },
        search(value) { if (value !== undefined) nodes['qs-input'].value = value; nodes['qs-search-btn'].dispatch('click'); },
        clear() { nodes['qs-clear-btn'].dispatch('click'); },
        flushTimers() { const callbacks = [...timers.values()]; timers.clear(); callbacks.forEach(callback => callback()); },
        text: () => nodes['qs-results-area'].textContent
    };
}
const employee = (name = 'Alice') => ({ empCode: '0000101', firstName: name, lastName: 'Example', jobGrade: '51T', department: 'QA', shift: 'A' });
const response = (data, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => data });
async function settle() { for (let index = 0; index < 10; index++) await Promise.resolve(); }
function descendants(root, predicate) {
    return root.children.flatMap(child => [...(predicate(child) ? [child] : []), ...descendants(child, predicate)]);
}

test('pages without search controls safely skip initialization', () => {
    assert.doesNotThrow(() => init({ getElementById: () => null }));
});

test('Clear cancels the queued query before debounce and restores input focus', () => {
    const view = fixture(); view.input('Alice'); view.clear(); view.flushTimers();
    assert.equal(view.pending.length, 0); assert.equal(view.text(), '');
    assert.equal(view.nodes['qs-input'].value, ''); assert.equal(view.nodes['qs-input'].focused, true);
    assert.equal(view.nodes['qs-spinner'].style.display, 'none');
});

test('short query aborts active fetch and ignores late responses even if abort is ignored', async () => {
    const view = fixture(); view.search('Alice'); view.input('A'); view.flushTimers();
    assert.equal(view.pending[0].options.signal.aborted, true);
    view.pending[0].resolve(response([employee()])); await settle();
    assert.equal(view.text(), ''); assert.equal(view.nodes['qs-spinner'].style.display, 'none');
});

test('new input invalidates the previous response immediately before its debounce runs', async () => {
    const view = fixture(); view.search('Alice'); view.input('Bob');
    view.pending[0].resolve(response([employee('Alice')])); await settle();
    assert.doesNotMatch(view.text(), /Alice/); view.flushTimers();
    assert.equal(view.pending.length, 2); view.pending[1].resolve(response([employee('Bob')])); await settle();
    assert.match(view.text(), /Bob/); assert.doesNotMatch(view.text(), /Alice/);
});

test('a late earlier response cannot overwrite the newer result or loading state', async () => {
    const view = fixture(); view.search('Alice'); view.input('Bob'); view.flushTimers();
    view.pending[0].resolve(response([employee('Alice')])); await settle();
    assert.equal(view.nodes['qs-spinner'].style.display, 'block');
    view.pending[1].resolve(response([employee('Bob')])); await settle();
    assert.match(view.text(), /Bob/); assert.equal(view.nodes['qs-spinner'].style.display, 'none');
});

test('same-query Search retries after a failed fetch and succeeds', async () => {
    const view = fixture(); view.search('Alice'); view.pending[0].reject(new Error('Synthetic offline')); await settle();
    assert.match(view.text(), /Search failed/); view.search(); assert.equal(view.pending.length, 2);
    view.pending[1].resolve(response([employee()])); await settle(); assert.match(view.text(), /Alice/);
});

test('HTTP authorization and server failures are shown instead of interpreted as employee data', async () => {
    for (const status of [401, 403, 500]) {
        const view = fixture(); view.search('Alice'); view.pending[0].resolve(response({ error: 'private technical detail' }, status)); await settle();
        assert.match(view.text(), status === 401 ? /sign in/i : status === 403 ? /access/i : /Search failed/);
        assert.doesNotMatch(view.text(), /private technical detail|No employees found/);
    }
});

test('malformed JSON response shapes are errors and a retry remains available', async () => {
    const view = fixture(); view.search('Alice'); view.pending[0].resolve(response({ error: 'not an array' })); await settle();
    assert.match(view.text(), /Search failed/); view.search(); assert.equal(view.pending.length, 2);
});

test('hostile fields render as literal text and photo/link URLs remain local', async () => {
    const view = fixture(); view.search('Alice');
    view.pending[0].resolve(response([{ ...employee('<em>literal</em>'), empCode: '001/"?x', department: '<b>QA</b>',
        photoPath: '/api/photo/" onerror="unexpected' }])); await settle();
    assert.match(view.text(), /<em>literal<\/em>/); assert.match(view.text(), /<b>QA<\/b>/);
    assert.equal(descendants(view.nodes['qs-results-area'], node => node.tagName === 'EM' || node.tagName === 'B').length, 0);
    assert.equal(descendants(view.nodes['qs-results-area'], node => node.tagName === 'IMG').length, 0);
    const links = descendants(view.nodes['qs-results-area'], node => node.tagName === 'A');
    assert.equal(links[0].href, '/ViewUser?empCode=001%2F%22%3Fx');
    assert.equal(links[1].href, '/UpdateUser?empCode=001%2F%22%3Fx');
});

test('valid SQL photo URLs work and broken photos use text initials without HTML', async () => {
    const view = fixture(); view.search('Alice');
    view.pending[0].resolve(response([{ ...employee(), photoPath: '/api/photo/employee/0000101/current' }])); await settle();
    const image = descendants(view.nodes['qs-results-area'], node => node.tagName === 'IMG')[0];
    assert.equal(image.src, '/api/photo/employee/0000101/current'); image.dispatch('error');
    assert.equal(descendants(view.nodes['qs-results-area'], node => node.tagName === 'IMG').length, 0);
    assert.match(view.text(), /AE/);
});

test('Enter and hints issue one immediate query and keep the clear button available', () => {
    const view = fixture(); view.input('Alice'); view.nodes['qs-input'].dispatch('keydown', { key: 'Enter' }); view.flushTimers();
    assert.equal(view.pending.length, 1); view.hint.children[0].dispatch('click');
    assert.equal(view.pending.length, 2); assert.equal(view.nodes['qs-input'].value, 'E001');
    assert.equal(view.nodes['qs-clear-btn'].style.display, 'inline-flex');
});
