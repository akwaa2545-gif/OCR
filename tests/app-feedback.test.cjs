'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function setup() {
    const timers = [];
    function element(tag) {
        const classes = new Set();
        return { tag, children: [], attributes: {}, style: {}, events: {}, textContent: '',
            className: '', classList: { add: name => classes.add(name) },
            appendChild(child) { this.children.push(child); return child; },
            setAttribute(name, value) { this.attributes[name] = value; },
            addEventListener(name, callback) { this.events[name] = callback; },
            querySelector(selector) { return this.children.flatMap(child => [child, ...child.children]).find(child => child.className === selector.slice(1)); },
            remove() { this.removed = true; },
            set innerHTML(value) {
                this.html = value;
                for (const name of ['toast-title', 'toast-message', 'toast-close']) {
                    if (!value.includes(name)) continue;
                    const child = element(name === 'toast-close' ? 'button' : 'div');
                    child.className = name;
                    this.children.push(child);
                }
            }
        };
    }
    const document = { body: element('body'), createElement: element, addEventListener() {} };
    const window = {};
    vm.runInNewContext(fs.readFileSync(require.resolve('../OperatorCertificationRecord.Web/wwwroot/js/app.js'), 'utf8'), {
        window, document, setTimeout(callback, delay) { timers.push({ callback, delay }); }
    });
    return { window, document, timers };
}

test('feedback renders names and skill text literally, without HTML interpolation', () => {
    const { window } = setup();
    const title = '<img src=x onerror=alert(1)>';
    const message = 'Skill </script><svg onload=alert(1)> updated for 0402567';
    const notice = window.Toast.success(title, message);
    assert.equal(notice.html, undefined);
    assert.equal(notice.querySelector('.toast-title').textContent, title);
    assert.equal(notice.querySelector('.toast-message').textContent, message);
    assert.equal(notice.attributes.role, 'status');
    assert.equal(notice.attributes['aria-atomic'], 'true');
});

test('errors remain available until explicitly dismissed and announce as alerts', () => {
    const { window, timers } = setup();
    const notice = window.Toast.error('Save failed', 'Please retry.');
    assert.equal(notice.attributes.role, 'alert');
    assert.equal(timers.length, 0);
    const close = notice.querySelector('.toast-close');
    assert.equal(close.attributes['aria-label'], 'Dismiss notification');
    close.events.click();
    assert.equal(timers.length, 1);
    timers[0].callback();
    assert.equal(notice.removed, true);
});

test('explicit timeout and manual-only notifications retain their caller choices', () => {
    const { window, timers } = setup();
    window.Toast.show('success', 'Saved', '', 1234);
    assert.equal(timers[0].delay, 1234);
    window.Toast.show('success', 'Saved', '', 0);
    assert.equal(timers.length, 1);
});

test('unknown notification type uses safe info styling', () => {
    const { window } = setup();
    const notice = window.Toast.show('danger unexpected-class', 'Title', 'Message', 0);
    assert.equal(notice.className, 'toast toast-info');
    assert.equal(notice.attributes.role, 'status');
});
