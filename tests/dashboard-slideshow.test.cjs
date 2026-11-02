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
    const attributes = new Map();
    // A decorative slideshow has no button or other interactive descendants.
    const card = {
        setAttribute: (name, value) => attributes.set(name, value),
        getAttribute: name => attributes.get(name)
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
    return { card, doc, win, media, start: () => init(doc, win),
        paused: () => card.getAttribute('data-slideshow-paused') };
}

test('decorative slideshow starts without any button or interactive controls', () => {
    const f = fixture();
    assert.doesNotThrow(f.start);
    assert.equal(f.paused(), 'false');
});

test('hidden page pauses the slideshow and visible page resumes it', () => {
    const f = fixture(); f.start();
    f.doc.hidden = true; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'true');
    f.doc.hidden = false; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'false');
});

test('initially hidden page is paused before its first visibility change', () => {
    const f = fixture({ hidden: true }); f.start();
    assert.equal(f.paused(), 'true');
    f.doc.hidden = false; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'false');
});

test('reduced motion initially pauses playback and reacts to live preference changes', () => {
    const f = fixture({ reduced: true }); f.start();
    assert.equal(f.paused(), 'true');
    f.media.matches = false; f.media.fire('change');
    assert.equal(f.paused(), 'false');
    f.media.matches = true; f.media.fire('change');
    assert.equal(f.paused(), 'true');
});

test('visibility and motion preference cannot override each other while either requires pause', () => {
    const f = fixture({ hidden: true }); f.start();
    f.media.matches = true; f.media.fire('change');
    f.doc.hidden = false; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'true');
    f.doc.hidden = true; f.doc.fire('visibilitychange');
    f.media.matches = false; f.media.fire('change');
    assert.equal(f.paused(), 'true');
    f.doc.hidden = false; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'false');
});

test('repeated initialization binds only one visibility and one preference listener', () => {
    const f = fixture(); f.start(); f.start(); f.start();
    assert.equal(f.doc.listeners.get('visibilitychange').length, 1);
    assert.equal(f.media.listeners.get('change').length, 1);
    f.media.matches = true; f.media.fire('change');
    f.start(); assert.equal(f.paused(), 'true');
});

test('pages without a slideshow safely skip initialization', () => {
    assert.doesNotThrow(() => init({ querySelector: () => null }, {}));
});

test('visibility pausing works when matchMedia is unavailable', () => {
    const f = fixture({ mediaApi: 'missing' }); f.start();
    assert.equal(f.paused(), 'false');
    f.doc.hidden = true; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'true');
    f.doc.hidden = false; f.doc.fire('visibilitychange');
    assert.equal(f.paused(), 'false');
});

test('legacy preference listeners still receive reduced-motion changes', () => {
    const f = fixture({ mediaApi: 'legacy' }); f.start();
    f.media.matches = true; f.media.fire('change');
    assert.equal(f.paused(), 'true');
});