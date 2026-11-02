'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { init } = require('../OperatorCertificationRecord.Web/wwwroot/js/dashboard-slideshow.js');

function fixture({ reduced = false, hidden = false, mediaApi = 'modern' } = {}) {
    function target() {
        const listeners = new Map();
        return {
            listeners,
            addEventListener(name, listener) {
                const list = listeners.get(name) || [];
                list.push(listener); listeners.set(name, list);
            },
            fire(name) { (listeners.get(name) || []).forEach(listener => listener()); }
        };
    }
    const label = { textContent: '' };
    const icon = { className: '' };
    const attributes = new Map();
    const button = Object.assign(target(), {
        hidden: true,
        setAttribute: (name, value) => attributes.set(name, value),
        getAttribute: name => attributes.get(name),
        querySelector: selector => selector === '[data-slideshow-label]' ? label : icon
    });
    const card = {
        dataset: {},
        querySelector: () => button,
        setAttribute(name, value) { this.dataset[name] = value; },
        getAttribute(name) { return this.dataset[name]; }
    };
    const doc = Object.assign(target(), { hidden, querySelector: () => card });
    const media = Object.assign(target(), { matches: reduced });
    if (mediaApi === 'legacy') {
        delete media.addEventListener;
        media.addListener = listener => {
            const list = media.listeners.get('change') || [];
            list.push(listener); media.listeners.set('change', list);
        };
    }
    const win = mediaApi === 'missing' ? {} : { matchMedia: () => media };
    function start() { init(doc, win); }
    return { start, card, button, label, icon, doc, win, media,
        paused: () => card.getAttribute('data-slideshow-paused') };
}

test('slideshow reveals a labelled pause control and user can pause and play', () => {
    const f = fixture(); f.start();
    assert.equal(f.button.hidden, false);
    assert.equal(f.paused(), 'false');
    assert.equal(f.label.textContent, 'Pause');
    assert.equal(f.button.getAttribute('aria-label'), 'Pause background slideshow');
    f.button.fire('click');
    assert.equal(f.paused(), 'true');
    assert.equal(f.label.textContent, 'Play');
    assert.equal(f.button.getAttribute('aria-label'), 'Play background slideshow');
    assert.match(f.icon.className, /bi-play-fill/);
    f.button.fire('click');
    assert.equal(f.paused(), 'false');
    assert.match(f.icon.className, /bi-pause-fill/);
});

test('hidden document pauses playback without clearing the user pause choice', () => {
    const f = fixture(); f.start();
    f.doc.hidden = true; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'true');
    f.doc.hidden = false; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'false');
    f.button.fire('click');
    f.doc.hidden = true; f.doc.fire('visibilitychange');
    f.doc.hidden = false; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'true');
    assert.equal(f.label.textContent, 'Play');
});

test('initially hidden page does not animate until visible', () => {
    const f = fixture({ hidden: true }); f.start();
    assert.equal(f.paused(), 'true');
    f.doc.hidden = false; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'false');
});

test('reduced motion keeps the background still and hides the playback control', () => {
    const f = fixture({ reduced: true }); f.start();
    assert.equal(f.paused(), 'true');
    assert.equal(f.button.hidden, true);
    f.button.fire('click');
    f.media.matches = false; f.media.fire('change');
    assert.equal(f.paused(), 'false');
    assert.equal(f.button.hidden, false);
    assert.equal(f.label.textContent, 'Pause');
});

test('live motion preference changes retain an existing user pause', () => {
    const f = fixture(); f.start(); f.button.fire('click');
    f.media.matches = true; f.media.fire('change');
    assert.equal(f.button.hidden, true);
    f.media.matches = false; f.media.fire('change');
    assert.equal(f.button.hidden, false);
    assert.equal(f.paused(), 'true');
    assert.equal(f.label.textContent, 'Play');
});

test('repeated initialization does not bind duplicate events or reset manual pause', () => {
    const f = fixture(); f.start(); f.button.fire('click'); f.start(); f.start();
    assert.equal(f.button.listeners.get('click').length, 1);
    assert.equal(f.doc.listeners.get('visibilitychange').length, 1);
    assert.equal(f.media.listeners.get('change').length, 1);
    assert.equal(f.paused(), 'true');
    f.button.fire('click'); assert.equal(f.paused(), 'false');
});

test('pages without a complete slideshow safely skip initialization', () => {
    assert.doesNotThrow(() => init({ querySelector: () => null }, {}));
    assert.doesNotThrow(() => init({ querySelector: () => ({ querySelector: () => null }) }, {}));
});

test('playback controls still work without matchMedia support', () => {
    const f = fixture({ mediaApi: 'missing' }); f.start();
    assert.equal(f.paused(), 'false');
    f.button.fire('click'); assert.equal(f.paused(), 'true');
});

test('older media change listeners receive reduced-motion updates', () => {
    const f = fixture({ mediaApi: 'legacy' }); f.start();
    f.media.matches = true; f.media.fire('change');
    assert.equal(f.paused(), 'true');
    assert.equal(f.button.hidden, true);
});
