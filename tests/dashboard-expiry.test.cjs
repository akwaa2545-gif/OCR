'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { init, expiryState } = require('../OperatorCertificationRecord.Web/wwwroot/js/dashboard-expiry.js');

class Element {
    constructor(tag, id = '') {
        this.tagName = tag.toUpperCase(); this.id = id; this.children = []; this.style = {}; this.dataset = {};
        this.attributes = {}; this.events = {}; this.parentElement = null; this._text = '';
        this.classList = {
            add: (...names) => { this.className = [...new Set((this.className || '').split(' ').concat(names))].join(' '); },
            remove: name => { this.className = (this.className || '').split(' ').filter(value => value !== name).join(' '); },
            contains: name => (this.className || '').split(' ').includes(name)
        };
    }
    set innerHTML(value) { throw new Error('HTML parsing is forbidden for expiry rendering'); }
    set textContent(value) { this._text = String(value); this.children = []; }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
    appendChild(child) { this.children.push(child); child.parentElement = this; return child; }
    replaceChildren(...children) { this.children = []; this._text = ''; children.forEach(child => this.appendChild(child)); }
    setAttribute(name, value) { this.attributes[name] = String(value); if (name.startsWith('data-')) this.dataset[name.slice(5)] = String(value); }
    getAttribute(name) { return this.attributes[name] || null; }
    removeAttribute(name) { delete this.attributes[name]; }
    addEventListener(name, handler, options) { handler.once = options?.once; (this.events[name] ||= []).push(handler); }
    removeEventListener(name, handler) { this.events[name] = (this.events[name] || []).filter(value => value !== handler); }
    emit(name) {
        for (const handler of [...(this.events[name] || [])]) {
            if (handler.once) this.events[name] = this.events[name].filter(value => value !== handler);
            handler({ type: name, target: this });
        }
    }
    focus() { this.focused = true; }
    querySelectorAll(selector) {
        const matches = element => selector.startsWith('.') ? (element.className || '').split(' ').includes(selector.slice(1)) : selector.startsWith('#') ? element.id === selector.slice(1) : element.tagName === selector.toUpperCase();
        return this.children.flatMap(child => (matches(child) ? [child] : []).concat(child.querySelectorAll(selector)));
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
}
function deferred() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
function item(index, process = 'Process ' + index) { return { EmpCode: String(index).padStart(7, '0'), FirstNameEng: 'First', LastNameEng: 'Last', Process: process, ExpiryDate: '2026-11-10T00:00:00' }; }
function fixture(initialCount = 20) {
    const ids = {};
    for (const id of ['expiringModal', 'expiringPreviewBody', 'expiringPreviewInfo', 'expiringPreviewFooterInfo', 'expiringLoadMoreWrap', 'expiringLoadMoreBtn', 'expiringLoadMoreFooterBtn', 'expiringLoadMoreCount', 'expiringLoadMoreFooterCount', 'expiringLoadMoreSpinner', 'expiringLoadMoreFooterSpinner', 'expiringLoadMoreError', 'employeeExpiriesModal', 'employeeProfileLink']) ids[id] = new Element(id.includes('Btn') ? 'button' : 'div', id);
    const title = new Element('h5'); title.className = 'modal-title';
    const body = new Element('div'); body.className = 'modal-body';
    ids.employeeExpiriesModal.appendChild(title); ids.employeeExpiriesModal.appendChild(body); ids.employeeExpiriesModal.appendChild(ids.employeeProfileLink);
    const document = { getElementById: id => ids[id] || null, createElement: tag => new Element(tag), body: new Element('body') };
    document.body.appendChild(ids.employeeExpiriesModal);
    const requests = [];
    const view = { expiringItems: Array.from({ length: initialCount }, (_, i) => item(i)), fetch: url => { const response = deferred(); requests.push({ url, ...response }); return response.promise; }, bootstrap: { Modal: { getOrCreateInstance: () => ({ show() {} }) } } };
    for (let i = 0; i < initialCount; i++) ids.expiringPreviewBody.appendChild(new Element('tr'));
    const controller = init(document, view);
    const respond = (index, data, status = 200) => requests[index].resolve({ ok: status === 200, status, json: async () => data });
    return { ids, document, view, requests, respond, controller, title, body };
}

test('nonempty cached twenty rows fetch a larger prefix and do not claim twenty is the total', async () => {
    const f = fixture();
    assert.match(f.ids.expiringPreviewInfo.textContent, /20/);
    assert.doesNotMatch(f.ids.expiringPreviewInfo.textContent, /of 20/);
    assert.equal(f.ids.expiringLoadMoreFooterBtn.disabled, false);
    const loading = f.controller.loadMore();
    assert.equal(f.requests.length, 1); assert.match(f.requests[0].url, /top=40/);
    f.respond(0, Array.from({ length: 33 }, (_, i) => item(i)));
    await loading;
    assert.equal(f.ids.expiringPreviewBody.children.length, 33);
    assert.match(f.ids.expiringPreviewInfo.textContent, /33 of 33/);
    assert.equal(f.ids.expiringLoadMoreFooterBtn.disabled, true);
});

test('two load triggers share one request and one append', async () => {
    const f = fixture();
    const first = f.controller.loadMore(); const second = f.controller.loadMore();
    assert.equal(f.requests.length, 1);
    f.respond(0, Array.from({ length: 40 }, (_, i) => item(i)));
    await Promise.all([first, second]);
    assert.equal(f.ids.expiringPreviewBody.children.length, 40);
    assert.equal(f.ids.expiringLoadMoreFooterBtn.disabled, false);
});

test('disabling the focused Load more button keeps focus inside the modal', async () => {
    const f = fixture();
    const close = new Element('button'); close.className = 'expiring-close';
    f.ids.expiringModal.appendChild(close);
    f.document.activeElement = f.ids.expiringLoadMoreFooterBtn;
    const loading = f.controller.loadMore();
    assert.equal(close.focused, true);
    f.respond(0, Array.from({ length: 25 }, (_, i) => item(i)));
    await loading;
    assert.equal(f.ids.expiringLoadMoreFooterBtn.disabled, true);
});

test('failed loads preserve current rows, show a safe error, and permit retry', async () => {
    const f = fixture(); const first = f.controller.loadMore(); f.respond(0, { error: 'private SQL details' }, 500); await first;
    assert.equal(f.ids.expiringPreviewBody.children.length, 20);
    assert.match(f.ids.expiringLoadMoreError.textContent, /try again/i);
    assert.doesNotMatch(f.ids.expiringLoadMoreError.textContent, /SQL/);
    assert.equal(f.ids.expiringLoadMoreFooterBtn.disabled, false);
    const retry = f.controller.loadMore(); f.respond(1, [item(0)]); await retry;
    assert.equal(f.requests.length, 2); assert.equal(f.ids.expiringPreviewBody.children.length, 1);
    assert.equal(f.ids.expiringLoadMoreError.textContent, '');
});

test('API camelCase fields and hostile employee/process text are rendered literally in rows and popup', async () => {
    const f = fixture(0); const dangerous = '<img src=x onerror="alert(1)">';
    const data = [{ empCode: '10"<tag>', firstNameEng: dangerous, lastNameEng: '<em>Last</em>', process: dangerous, expiryDate: 'bad date <img>' }];
    const loading = f.controller.loadMore(); f.respond(0, data); await loading;
    const row = f.ids.expiringPreviewBody.children[0];
    assert.match(row.textContent, /<img src=x/);
    assert.equal(row.querySelectorAll('img').length, 0);
    assert.equal(row.querySelector('a').href, '/ViewUser?empCode=10%22%3Ctag%3E');
    f.view.openEmployeeExpiries('10"<tag>');
    assert.match(f.body.textContent, /<img src=x/); assert.equal(f.body.querySelectorAll('img').length, 0);
    assert.equal(f.ids.employeeProfileLink.href, '/ViewUser?empCode=10%22%3Ctag%3E');
});

test('calendar-day badges use Bangkok today and handle invalid dates without throwing', () => {
    const now = new Date('2026-10-29T20:30:00Z');
    assert.deepEqual(expiryState('2026-10-30T00:00:00', now), { date: '2026-10-30', text: '30 Oct 2026', badge: 'Today', className: 'expiry-critical' });
    assert.equal(expiryState('2026-11-14', now).className, 'expiry-soon');
    assert.equal(expiryState('2026-10-29', now).badge, 'Expired');
    assert.equal(expiryState('2026-02-30', now).badge, '');
    assert.equal(expiryState('not a date', now).date, '');
});

test('initial Razor badges are replaced rather than duplicated and init is idempotent', () => {
    const f = fixture(1); const span = new Element('span'); span.className = 'expiry-date'; span.setAttribute('data-expiry', '2000-01-01');
    span.textContent = 'old date'; const old = new Element('span'); old.className = 'expiry-badge'; old.textContent = 'old badge'; span.appendChild(old);
    f.ids.expiringPreviewBody.children[0].appendChild(span);
    f.ids.expiringModal.appendChild(f.ids.expiringPreviewBody);
    f.view.highlightExpiryDatesInModal(); f.view.highlightExpiryDatesInModal();
    assert.equal(span.querySelectorAll('.expiry-badge').length, 1);
    assert.equal(init(f.document, f.view), f.controller);
    assert.equal(f.ids.expiringLoadMoreFooterBtn.events.click.length, 1);
});

test('employee popup switches Bootstrap modals and restores the parent plus source focus after closing', () => {
    const f = fixture(1); const calls = [];
    const parent = f.ids.expiringModal, child = f.ids.employeeExpiriesModal;
    parent.classList.add('show'); f.document.body.classList.add('modal-open');
    f.view.bootstrap.Modal.getOrCreateInstance = node => ({
        show() { calls.push(node.id + '.show'); node.classList.add('show'); f.document.body.classList.add('modal-open'); },
        hide() { calls.push(node.id + '.hide'); node.classList.remove('show'); }
    });
    const row = f.ids.expiringPreviewBody.children[0];
    f.view.openEmployeeExpiries('0000000', row);
    assert.deepEqual(calls, ['expiringModal.hide']);
    assert.equal(child.classList.contains('show'), false);
    f.document.body.classList.remove('modal-open'); parent.emit('hidden.bs.modal');
    assert.deepEqual(calls, ['expiringModal.hide', 'employeeExpiriesModal.show']);
    assert.equal(f.document.body.classList.contains('modal-open'), true);
    child.classList.remove('show'); f.document.body.classList.remove('modal-open'); child.emit('hidden.bs.modal');
    assert.deepEqual(calls, ['expiringModal.hide', 'employeeExpiriesModal.show', 'expiringModal.show']);
    assert.equal(f.document.body.classList.contains('modal-open'), true);
    assert.equal(row.focused, undefined);
    parent.emit('shown.bs.modal'); assert.equal(row.focused, true);
    child.emit('hidden.bs.modal'); assert.equal(calls.length, 3);
    assert.equal(f.ids.expiringPreviewBody.children.length, 1);
});

test('a row clicked during the parent opening transition waits until the parent can hide', () => {
    const f = fixture(1); const calls = [];
    const parent = f.ids.expiringModal;
    parent.classList.add('show'); let opening = true;
    f.view.bootstrap.Modal.getOrCreateInstance = node => ({
        show() { calls.push(node.id + '.show'); },
        hide() { calls.push('hide'); if (!opening) parent.classList.remove('show'); }
    });
    f.view.openEmployeeExpiries('0000000'); assert.deepEqual(calls, ['hide']);
    opening = false; parent.emit('shown.bs.modal'); assert.deepEqual(calls, ['hide', 'hide']);
    parent.emit('hidden.bs.modal'); assert.equal(calls[2], 'employeeExpiriesModal.show');
    parent.emit('shown.bs.modal'); assert.equal(calls.length, 3);
});
