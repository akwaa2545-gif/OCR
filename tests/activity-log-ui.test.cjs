'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { init } = require('../OperatorCertificationRecord.Web/wwwroot/js/activity-log.js');

function fixture(nativeModal = true) {
    const handlers = {};
    const viewHandlers = {};
    const closeHandlers = [];
    const classes = new Set();
    let focused = '';
    let replaced = '';
    const dialog = {
        open: true,
        dataset: { returnFocus: 'activity-event-42' },
        querySelectorAll: () => [{ addEventListener: (name, handler) => closeHandlers.push(handler) }],
        addEventListener: (name, handler) => { handlers[name] = handler; },
        removeAttribute: name => { if (name === 'open') dialog.open = false; },
        close: () => { dialog.open = false; handlers.close(); },
        matches: () => true
    };
    if (nativeModal) dialog.showModal = () => { dialog.open = true; dialog.modal = true; };
    const document = {
        getElementById: id => id === 'activity-dialog' ? dialog :
            id === 'activity-dialog-title' ? { focus: () => { focused = 'title'; } } :
            id === 'activity-event-42' ? { focus: () => { focused = 'origin'; } } : null,
        documentElement: { classList: { add: name => classes.add(name), remove: name => classes.delete(name) } },
        defaultView: {
            location: { href: 'http://localhost/DevTools/ActivityLog?View=changes&Employee=0002519&EventId=42&Skill=Inspection&PageNumber=3' },
            history: { state: {}, replaceState: (state, title, url) => { replaced = url; } },
            addEventListener: (name, handler) => { viewHandlers[name] = handler; }
        }
    };
    return { document, dialog, handlers, viewHandlers, classes, closeHandlers,
        focused: () => focused, replaced: () => replaced };
}

test('details become a real native modal, lock only page scrolling and focus the title', () => {
    const view = fixture(); init(view.document);
    assert.equal(view.dialog.modal, true);
    assert.equal(view.classes.has('activity-modal-open'), true);
    assert.equal(view.focused(), 'title');
});

test('close returns focus to the exact event while preserving filters and paging', () => {
    const view = fixture(); init(view.document); view.closeHandlers[0]();
    assert.equal(view.focused(), 'origin');
    assert.equal(view.classes.has('activity-modal-open'), false);
    const url = new URL(view.replaced());
    assert.equal(url.searchParams.has('EventId'), false);
    assert.equal(url.searchParams.get('Employee'), '0002519');
    assert.equal(url.searchParams.get('Skill'), 'Inspection');
    assert.equal(url.searchParams.get('PageNumber'), '3');
});

test('native Escape close follows the same canonical GET URL cleanup', () => {
    const view = fixture(); init(view.document); view.dialog.close();
    assert.equal(view.focused(), 'origin');
    assert.equal(new URL(view.replaced()).searchParams.has('EventId'), false);
});

test('browser page restore keeps modal page lock only while details remain open', () => {
    const view = fixture(); init(view.document);
    view.classes.delete('activity-modal-open'); view.viewHandlers.pageshow();
    assert.equal(view.classes.has('activity-modal-open'), true);
    view.dialog.open = false; view.viewHandlers.pageshow();
    assert.equal(view.classes.has('activity-modal-open'), false);
});

test('server-rendered details stay readable when native showModal is unavailable', () => {
    const view = fixture(false); init(view.document);
    assert.equal(view.dialog.open, true);
    assert.equal(view.focused(), 'title');
});

test('list-only page does not install modal state or mutate its URL', () => {
    const view = fixture();
    view.document.getElementById = () => null;
    init(view.document);
    assert.equal(view.classes.size, 0);
    assert.equal(view.replaced(), '');
});
