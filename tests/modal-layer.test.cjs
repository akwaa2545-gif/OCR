const test = require('node:test');
const assert = require('node:assert/strict');
const { init } = require('../OperatorCertificationRecord.Web/wwwroot/js/modal-layer.js');

function fixture() {
    const listeners = new Map(), bodyClasses = new Set();
    let moves = 0, remainingModal = null, removed = 0;
    const body = { classList: { contains: name => bodyClasses.has(name) }, appendChild(node) { moves++; node.parentElement = this; } };
    const document = {
        body,
        addEventListener(type, callback) { if (!listeners.has(type)) listeners.set(type, []); listeners.get(type).push(callback); },
        querySelector() { return remainingModal; },
        querySelectorAll() { return [{ remove() { removed++; } }]; }
    };
    const modal = { parentElement: {}, style: {}, classList: { contains: name => name === 'modal' } };
    const emit = (type, target = modal) => (listeners.get(type) || []).forEach(callback => callback({ target }));
    return { document, body, modal, bodyClasses, emit, get moves() { return moves; }, get removed() { return removed; }, set remainingModal(value) { remainingModal = value; } };
}

test('popup enters its final parent before opening and is not moved again after appearing', () => {
    const page = fixture(); init(page.document);
    page.emit('show.bs.modal');
    assert.equal(page.modal.parentElement, page.body);
    assert.equal(page.moves, 1);
    assert.deepEqual(page.modal.style, { zIndex: '10050' });
    page.emit('shown.bs.modal');
    assert.equal(page.moves, 1);
    assert.deepEqual(page.modal.style, { zIndex: '10050' });
    page.emit('show.bs.modal'); assert.equal(page.moves, 1);
});

test('initialization is idempotent and unrelated events do not move content', () => {
    const page = fixture(); init(page.document); init(page.document);
    const other = { parentElement: {}, classList: { contains: () => false } };
    page.emit('show.bs.modal', other); assert.equal(page.moves, 0);
    page.emit('hidden.bs.modal'); assert.equal(page.removed, 1);
});

test('cleanup preserves another open modal and its backdrop', () => {
    const page = fixture(); init(page.document);
    page.bodyClasses.add('modal-open'); page.emit('hidden.bs.modal'); assert.equal(page.removed, 0);
    page.bodyClasses.clear(); page.remainingModal = {}; page.emit('hidden.bs.modal'); assert.equal(page.removed, 0);
    page.remainingModal = null; page.emit('hidden.bs.modal'); assert.equal(page.removed, 1);
});
