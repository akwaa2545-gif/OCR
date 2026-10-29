const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const layout = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/Pages/Shared/_Layout.cshtml'), 'utf8');
const start = layout.indexOf('    /* ===== Animation system ===== */');
const end = layout.indexOf('        /* globally keep modals on top and in body */', start);
assert.ok(start >= 0 && end > start, 'Navigation animation script must be present');
const animation = layout.slice(start, end) + '\n})();';

function setup({ hasBar = true } = {}) {
    const windowEvents = new Map();
    const documentEvents = new Map();
    const timers = new Map();
    const frames = new Map();
    let nextId = 1;
    let now = 0;
    const classes = new Set();
    const body = { classList: { add: value => classes.add(value), remove: value => classes.delete(value) } };
    const bar = { style: {}, classList: { remove() {} } };
    const window = { location: { href: '/UpdateUser?empCode=0402567' },
        addEventListener(name, callback) { windowEvents.set(name, callback); } };
    const document = { body, getElementById: () => hasBar ? bar : null,
        addEventListener(name, callback) { documentEvents.set(name, callback); } };
    const context = vm.createContext({ window, document,
        setTimeout(callback, delay) { const id = nextId++; timers.set(id, { callback, due: now + delay }); return id; },
        clearTimeout(id) { timers.delete(id); },
        requestAnimationFrame(callback) { const id = nextId++; frames.set(id, callback); return id; },
        cancelAnimationFrame(id) { frames.delete(id); }
    });
    vm.runInContext(animation, context);
    function advance(ms) {
        const target = now + ms;
        for (;;) {
            const pending = [...timers].filter(([, timer]) => timer.due <= target).sort((a, b) => a[1].due - b[1].due)[0];
            if (!pending) break;
            now = pending[1].due;
            timers.delete(pending[0]);
            pending[1].callback();
        }
        now = target;
    }
    function flushFrames() {
        for (const [id, callback] of [...frames]) { frames.delete(id); callback(); }
    }
    function click(href) {
        let prevented = false;
        const anchor = { target: '', getAttribute: () => href, hasAttribute: () => false };
        documentEvents.get('click')({ target: { closest: () => anchor }, preventDefault() { prevented = true; } });
        return prevented;
    }
    return { window, windowEvents, bar, classes, advance, flushFrames, click };
}

test('history restore makes the page visible and clears navigation progress', () => {
    const page = setup();
    page.classes.add('page-leaving');
    page.windowEvents.get('pageshow')?.({ persisted: true });
    page.advance(1000);
    assert.equal(page.classes.has('page-leaving'), false);
    assert.equal(page.bar.style.opacity, '0');
    assert.equal(page.bar.style.width, '0%');
});

test('queued start animation cannot restore stale progress after history return', () => {
    const page = setup();
    page.flushFrames(); // The first animation frame has queued its second frame.
    page.windowEvents.get('pageshow')?.({ persisted: true });
    page.advance(1000);
    page.flushFrames();
    page.flushFrames();
    assert.equal(page.bar.style.width, '0%');
    assert.equal(page.bar.style.opacity, '0');
});

test('normal internal navigation still leaves and opens the requested profile', () => {
    const page = setup();
    assert.equal(page.click('/ViewUser?empCode=0402567'), true);
    assert.equal(page.classes.has('page-leaving'), true);
    page.advance(160);
    assert.equal(page.window.location.href, '/ViewUser?empCode=0402567');
});

test('a previous completion timer cannot hide a new navigation indicator', () => {
    const page = setup();
    page.windowEvents.get('DOMContentLoaded')();
    page.click('/ViewUser?empCode=0402567');
    page.advance(150);
    assert.equal(page.bar.style.opacity, '1');
});

test('history restoration also makes pages without a progress bar visible', () => {
    const page = setup({ hasBar: false });
    page.classes.add('page-leaving');
    page.windowEvents.get('pageshow')?.({ persisted: true });
    assert.equal(page.classes.has('page-leaving'), false);
});
