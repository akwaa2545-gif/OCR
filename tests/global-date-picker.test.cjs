const { test } = require('node:test');
const assert = require('node:assert/strict');
const { create, parseDate, formatISO, positionCalendar, installYearDropdown } = require('../OperatorCertificationRecord.Web/wwwroot/js/global-date-picker.js');

function yearFixture() {
    const doc = { createElement(tag) { return { tagName: tag, style: {}, attrs: {}, children: [], handlers: {},
        setAttribute(name, value) { this.attrs[name] = value; }, getAttribute(name) { return this.attrs[name] ?? null; },
        removeAttribute(name) { delete this.attrs[name]; }, addEventListener(name, fn) { this.handlers[name] = fn; },
        removeEventListener(name) { delete this.handlers[name]; }, replaceChildren(...nodes) { this.children = nodes; },
        remove() { this.parentNode.children = this.parentNode.children.filter(node => node !== this); } }; } };
    const header = { children: [], insertBefore(child, next) { this.children.splice(this.children.indexOf(next), 0, child); child.parentNode = this; } };
    const wrapper = doc.createElement('span'); wrapper.parentNode = header; header.children.push(wrapper);
    const fp = { currentYearElement: { parentNode: wrapper }, calendarContainer: { ownerDocument: doc },
        currentYear: 2026, selectedDates: [], config: { onYearChange: [], onMonthChange: [], onOpen: [], onDestroy: [] },
        changeYear(year) { this.currentYear = year; this.config.onYearChange.forEach(fn => fn()); },
        setDate(date) { this.selectedDates = [date]; this.currentYear = date.getFullYear(); }, clear() { this.selectedDates = []; }, set(options) { Object.assign(this.config, options); } };
    return { fp, header, wrapper };
}

test('year dropdown browses without selecting a date, synchronizes and restores on destroy', () => {
    const { fp, header, wrapper } = yearFixture();
    const control = installYearDropdown(fp);
    const select = header.children[0];
    assert.equal(select.attrs['aria-label'], 'ปี'); assert.equal(select.value, '2026');
    select.value = '2028'; select.handlers.change();
    assert.equal(fp.currentYear, 2028); assert.deepEqual(fp.selectedDates, []);
    fp.setDate(parseDate('1850-06-12'));
    assert.equal(select.value, '1850'); assert.ok(select.children.some(option => option.value === '1850'));
    fp.currentYear = 2027; fp.config.onMonthChange.forEach(fn => fn());
    assert.equal(select.value, '2027');
    assert.equal(installYearDropdown(fp), control); assert.equal(header.children.length, 2);
    fp.config.onDestroy.forEach(fn => fn());
    assert.equal(header.children.length, 1); assert.equal(wrapper.style.display, undefined);
});

test('year dropdown constrains years without generating thousands of options', () => {
    const { fp, header } = yearFixture();
    fp.config.minDate = parseDate('2024-07-01'); fp.config.maxDate = parseDate('2028-03-01');
    installYearDropdown(fp);
    const select = header.children[0];
    assert.deepEqual(select.children.map(option => option.value), ['2024', '2025', '2026', '2027', '2028']);
    fp.set({ minDate: parseDate('0001-01-01'), maxDate: parseDate('9999-12-31') });
    assert.ok(select.children.length < 130);
    assert.ok(select.children.some(option => option.value === '1'));
    assert.ok(select.children.some(option => option.value === '9999'));
});

test('strict dates preserve local Gregorian dates and reject rollover', () => {
    for (const value of ['2024-02-29', '29 Feb 2024', '29 February 2024', '29/02/2024']) {
        assert.equal(formatISO(parseDate(value)), '2024-02-29');
    }
    for (const value of ['2025-02-29', '31 Apr 2026', '2026-13-01', '2026-00-10', 'garbage', '10/28/2026']) {
        assert.equal(parseDate(value), undefined, value);
    }
});

test('Thai month names retain Gregorian years and reject invalid leap dates', () => {
    for (const value of ['29 ก.พ. 2024', '29 กุมภาพันธ์ 2024']) assert.equal(formatISO(parseDate(value)), '2024-02-29');
    assert.equal(formatISO(parseDate('28 ต.ค. 2026')), '2026-10-28');
    assert.equal(formatISO(parseDate('18 กุมภาพันธ์ 2004')), '2004-02-18');
    assert.equal(parseDate('29 ก.พ. 2025'), undefined);
    assert.equal(parseDate('31 เม.ย. 2026'), undefined);
});

test('ISO formatting does not convert through UTC and handles early years', () => {
    assert.equal(formatISO(parseDate('2004-02-18')), '2004-02-18');
    assert.equal(formatISO(parseDate('0099-01-02')), '0099-01-02');
    assert.equal(parseDate('0000-01-01'), undefined);
});

test('calendar stays inside a narrow viewport and above a bottom-edge field', () => {
    const calendar = { style: {}, offsetWidth: 310, offsetHeight: 300, getBoundingClientRect: () => ({ left: 0, top: 0, width: 310, height: 300 }), classList: { remove() {} } };
    const instance = { calendarContainer: calendar, _input: { getBoundingClientRect: () => ({ left: 350, right: 390, top: 610, bottom: 650 }) } };
    positionCalendar(instance, { innerWidth: 390, innerHeight: 700 });
    assert.equal(calendar.style.left, '72px');
    assert.equal(calendar.style.top, '302px');
    assert.equal(calendar.style.position, 'fixed');
    assert.equal(calendar.style.maxWidth, '374px');
    assert.equal(calendar.style.overflowY, 'hidden');
    positionCalendar(instance, { innerWidth: 390, innerHeight: 240 });
    assert.equal(calendar.style.overflowY, 'auto');
});

test('calendar compensates for a translated and scaled modal containing block', () => {
    const calendar = { style: {}, offsetWidth: 310, offsetHeight: 300, classList: { remove() {} } };
    calendar.getBoundingClientRect = () => ({ left: 400 + Number.parseFloat(calendar.style.left || '0') * 0.8,
        top: 100 + Number.parseFloat(calendar.style.top || '0') * 0.8, width: 248, height: 240 });
    const instance = { calendarContainer: calendar, _input: { getBoundingClientRect: () => ({ left: 520, right: 740, top: 300, bottom: 340 }) } };
    positionCalendar(instance, { innerWidth: 1280, innerHeight: 720 });
    const rendered = calendar.getBoundingClientRect();
    assert.equal(rendered.left, 520);
    assert.equal(rendered.top, 348);
});

// A small DOM boundary fake keeps the adapter's lifecycle and event behavior testable.
function setup() {
    class Element {
        constructor(tag = 'div') { this.tagName = tag.toUpperCase(); this.attrs = {}; this.handlers = {}; this.children = []; this.style = {}; this.isConnected = true; this.nodeType = 1; this.textContent = ''; this.id = ''; this.classList = { remove() {} }; }
        addEventListener(name, fn) { (this.handlers[name] ||= []).push(fn); }
        removeEventListener(name, fn) { this.handlers[name] = (this.handlers[name] || []).filter(x => x !== fn); }
        emit(name, extra = {}) { const event = { target: this, key: '', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, stopImmediatePropagation() { this.stopped = true; }, ...extra }; for (const fn of this.handlers[name] || []) { fn(event); if (event.stopped) break; } return event; }
        setAttribute(name, value) { this.attrs[name] = String(value); }
        getAttribute(name) { return this.attrs[name] ?? null; }
        hasAttribute(name) { return Object.hasOwn(this.attrs, name); }
        appendChild(child) { child.parentNode?.removeChild(child); this.children.push(child); child.parentNode = child.parentElement = this; }
        insertBefore(child, next) { this.children.splice(this.children.indexOf(next), 0, child); child.parentNode = child.parentElement = this; }
        removeChild(child) { this.children = this.children.filter(x => x !== child); child.parentNode = child.parentElement = null; }
        remove() { this.parentNode?.removeChild(this); }
        querySelector(selector) { return selector === 'label' ? this.children.find(x => x.tagName === 'LABEL') || null : null; }
        querySelectorAll() { return []; }
        matches(selector) { return selector === 'input[type="date"]' && this.tagName === 'INPUT' && this.type === 'date'; }
        closest() { return null; }
        contains(el) { return this.children.includes(el); }
    }
    class Input extends Element {
        constructor() { super('input'); this._value = ''; this.type = 'date'; this.labels = []; this.disabled = false; this.readOnly = false; this.required = false; this.min = ''; this.max = ''; this.defaultValue = ''; this.validationMessage = ''; }
        get value() { return this._value; }
        set value(value) { this._value = String(value); if (this.type === 'hidden') this.defaultValue = this._value; }
        setCustomValidity(message) { this.validationMessage = message; }
        checkValidity() { return !this.validationMessage && !(this.required && !this.value); }
        reportValidity() { return this.checkValidity(); }
        focus() { this.focused = true; this.emit('focus'); }
    }
    const doc = new Element(); doc.body = new Element(); doc.createElement = tag => tag === 'input' ? new Input() : new Element(tag);
    const original = new Input(); original.id = 'JoinDate'; original.value = original.defaultValue = '2004-02-18';
    const label = new Element('label'); label.textContent = 'Join date'; original.labels = [label];
    const field = new Element(); field.appendChild(label); field.appendChild(original); doc.body.appendChild(field);
    const form = new Element('form'); original.form = form;
    doc.querySelectorAll = () => original.type === 'date' ? [original] : [];
    let observer; const timers = [];
    const win = { HTMLInputElement: Input, MutationObserver: class { constructor(fn) { observer = fn; } observe() {} }, setTimeout(fn) { timers.push(fn); }, addEventListener() {}, innerWidth: 1280, innerHeight: 720 };
    function flatpickr(el, options) {
        const alt = new Input(); alt.type = 'text'; field.appendChild(alt); el.type = 'hidden';
        const fp = { altInput: alt, config: options, selectedDates: [], isOpen: false, calendarContainer: new Element(), _input: alt,
            setDate(value, trigger = false) { const date = parseDate(value); this.selectedDates = date ? [date] : []; el.value = formatISO(date); alt.value = date ? date.getDate() + ' ' + options.locale.months.shorthand[date.getMonth()] + ' ' + date.getFullYear() : ''; if (trigger) this.fireChange(); },
            clear(trigger) { this.selectedDates = []; el.value = ''; alt.value = ''; if (trigger) this.fireChange(); },
            fireChange() { options.onChange(); el.emit('change'); el.emit('input'); },
            open() { this.isOpen = true; }, close() { this.isOpen = false; }, set(config) { this.config = { ...this.config, ...config }; },
            destroy() { alt.remove(); el.type = 'date'; delete el._flatpickr; }
        };
        fp.setDate(el.value); el._flatpickr = fp; return fp;
    }
    const controller = create(doc, win, flatpickr); controller.init();
    return { original, alt: original._flatpickr.altInput, fp: original._flatpickr, doc, form, controller, mutate: (...records) => observer(records), timers };
}

test('calendar choice fires exactly one original change and input, preserving canonical form value', () => {
    const { original, fp } = setup(); let changes = 0; let inputs = 0;
    original.addEventListener('change', () => changes++); original.addEventListener('input', () => inputs++);
    fp.setDate(parseDate('2026-10-28'), true);
    assert.equal(original.value, '2026-10-28');
    assert.equal(changes, 1); assert.equal(inputs, 1);
});

test('visible Thai date input keeps ISO form values and Gregorian years', () => {
    const { original, alt, fp } = setup();
    assert.equal(fp.config.locale.months.longhand[9], 'ตุลาคม');
    assert.equal(fp.config.locale.weekdays.longhand[1], 'จันทร์');
    assert.equal(fp.config.locale.firstDayOfWeek, 0, 'Sunday is first and Saturday remains last');
    assert.equal(fp.config.locale.weekdays.longhand[0], 'อาทิตย์');
    assert.equal(fp.config.locale.weekdays.longhand[6], 'เสาร์');
    assert.equal(fp.config.locale.monthAriaLabel, 'เดือน');
    assert.equal(fp.config.locale.yearAriaLabel, 'ปี');
    alt.value = '28 ต.ค. 2026'; alt.emit('input'); alt.emit('blur');
    assert.equal(original.value, '2026-10-28');
    assert.equal(fp.selectedDates[0].getFullYear(), 2026);
    assert.equal(alt.value, '28 ต.ค. 2026');
});

test('programmatic values and resets synchronize the displayed date without extra change events', () => {
    const { original, alt, doc, form, timers } = setup(); let changes = 0;
    original.addEventListener('change', () => changes++);
    original.value = '2024-02-29';
    assert.equal(original.defaultValue, '2024-02-29', 'hidden value reflects into defaultValue like the real browser');
    assert.equal(alt.value, '29 ก.พ. 2024'); assert.equal(changes, 0);
    doc.emit('reset', { target: form }); timers.forEach(fn => fn());
    assert.equal(original.value, '2004-02-18'); assert.equal(alt.value, '18 ก.พ. 2004');
    assert.equal(changes, 0);
});

test('reset restores a display cleared by the native reset even when selected date is unchanged', () => {
    const { original, alt, doc, form, timers } = setup();
    alt.value = '';
    doc.emit('reset', { target: form }); timers.forEach(fn => fn());
    assert.equal(original.value, '2004-02-18'); assert.equal(alt.value, '18 ก.พ. 2004');
});

test('invalid manual dates never submit a previous value and remain invalid after blur', () => {
    const { original, alt, form, doc } = setup();
    alt.value = '31 Feb 2026'; alt.emit('input'); alt.emit('blur');
    assert.equal(original.value, ''); assert.equal(alt.value, '31 Feb 2026');
    assert.match(alt.validationMessage, /วันที่ให้ถูกต้อง/);
    assert.equal(alt.getAttribute('aria-invalid'), 'true');
    assert.equal(doc.emit('submit', { target: form }).defaultPrevented, true);
    alt.value = '29 Feb 2024'; alt.emit('input'); alt.emit('blur');
    assert.equal(original.value, '2024-02-29'); assert.equal(alt.validationMessage, '');
});

test('explicit confirmed employee transitions bypass unrelated date edits while normal saves still validate', () => {
    const { original, alt, form, doc } = setup();
    alt.value = '31 Feb 2026'; alt.emit('input'); alt.emit('blur');
    assert.equal(original.value, '');
    assert.equal(doc.emit('submit', { target: form, submitter: { formNoValidate: false } }).defaultPrevented, true);
    assert.equal(doc.emit('submit', { target: form, submitter: { formNoValidate: true } }).defaultPrevented, false);
    assert.equal(alt.value, '31 Feb 2026', 'The confirmed transition must not rewrite an unrelated edit');
});

test('outside-click auto-parsing cannot clear invalid text or silently swallow a typed change', () => {
    const { original, alt, fp } = setup(); let changes = 0;
    original.addEventListener('change', () => changes++);
    // This is the v4.6.13 documentClick path before the visible input blurs.
    const outsideClick = () => { if (fp.config.allowInput) fp.setDate(alt.value, false); fp.close(); };
    alt.value = '31 Feb 2026'; alt.emit('input'); outsideClick(); alt.emit('blur');
    assert.equal(alt.value, '31 Feb 2026'); assert.match(alt.validationMessage, /วันที่ให้ถูกต้อง/);
    assert.equal(original.value, ''); assert.equal(changes, 0);
    alt.value = '28 Oct 2026'; alt.emit('input'); outsideClick(); alt.emit('blur');
    assert.equal(original.value, '2026-10-28'); assert.equal(changes, 1);
    assert.equal(alt.emit('keydown', { key: 'Backspace' }).stopped, true);
});

test('constraints and readonly state apply to the visible field and block calendar opening', () => {
    const { original, alt, fp, controller, mutate } = setup();
    original.min = '2000-01-01'; original.required = true; original.readOnly = true;
    mutate({ type: 'attributes', target: original });
    controller.open(original);
    assert.equal(alt.readOnly, true); assert.equal(alt.required, true); assert.equal(fp.isOpen, false);
    assert.equal(alt.emit('keydown', { key: 'ArrowDown' }).stopped, true);
    original.readOnly = false; mutate({ type: 'attributes', target: original });
    alt.value = '1 Jan 1999'; alt.emit('input'); alt.emit('blur');
    assert.equal(original.value, ''); assert.match(alt.validationMessage, /ตั้งแต่/);
});

test('removed fields destroy the picker and can be safely initialized again', () => {
    const { original, controller, mutate } = setup();
    original.isConnected = false; mutate({ type: 'childList', addedNodes: [] });
    assert.equal(original._flatpickr, undefined);
    assert.equal(Object.hasOwn(original, 'value'), false);
    original.isConnected = true; controller.enhance(original);
    assert.ok(original._flatpickr);
});
