const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const ranges = require('../OperatorCertificationRecord.Web/wwwroot/js/report-date-ranges.js');

test('current year range uses local calendar dates including New Year and leap days', () => {
    assert.deepEqual(ranges.currentYear(new Date(2026, 0, 1, 0, 5)), { start: '2026-01-01', end: '2026-01-01' });
    assert.deepEqual(ranges.currentYear(new Date(2024, 1, 29, 0, 5)), { start: '2024-01-01', end: '2024-02-29' });
});
test('initialization preserves a manually changed range when a report is reopened', () => {
    const start = { value: '' }, end = { value: '' };
    ranges.initialize(start, end);
    start.value = '2022-03-04'; end.value = '2022-05-06';
    ranges.initialize(start, end);
    assert.equal(start.value, '2022-03-04'); assert.equal(end.value, '2022-05-06');
    ranges.apply(start, end, 0);
    assert.equal(start.value, '2000-01-01'); assert.equal(end.value, '2099-12-31');
});
function runModals(names) {
    const elements = new Map(); const groups = new Map();
    const element = id => {
        if (!elements.has(id)) elements.set(id, { id, value: '', style: { removeProperty() {} }, listeners: {},
            classList: { add() {}, remove() {}, toggle() {} }, addEventListener(name, fn) { this.listeners[name] = fn; },
            querySelectorAll() { return groups.get(id) || []; }, querySelector() { return null; }, closest() { return null; },
            getAttribute() { return null; }, appendChild() {} });
        return elements.get(id);
    };
    const globalButtons = [];
    const document = { getElementById: element, addEventListener() {}, querySelectorAll() { return globalButtons; } };
    const context = { document, ReportDateRanges: ranges, console, setTimeout() { return 1; }, clearTimeout() {}, URLSearchParams,
        fetch() { throw new Error('No network expected while initializing report defaults'); } };
    context.window = context; vm.createContext(context);
    for (const name of names) {
        const source = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/Pages/Shared/', name), 'utf8');
        const id = /class="modal[^\n]*?id="([^"]+)"/.exec(source)[1];
        const owner = element(id);
        const buttons = [...source.matchAll(/<button[^>]*data-days="([^"]+)"[^>]*>/g)].map(match => {
            let active = /class="[^"]*\bactive\b/.test(match[0]);
            return { getAttribute: () => match[1], get active() { return active; },
                classList: { toggle(_, value) { active = value; }, add() { active = true; }, remove() { active = false; } },
                addEventListener() {}, closest: () => owner };
        });
        groups.set(id, buttons); globalButtons.push(...buttons);
        const ids = [...source.matchAll(/const\s+(startEl|endEl)\s*=\s*document.getElementById\('([^']+)'\)/g)];
        for (const [, , fieldId] of ids) element(fieldId).closest = () => owner;
        const script = /<script>([\s\S]*?)<\/script>/.exec(source)[1];
        vm.runInContext(script, context, { filename: name });
    }
    return { elements, groups, context };
}
test('actual report modal scripts default to this year and keep preset state isolated', () => {
    const fixture = runModals(['_DisqualificationDownloadModal.cshtml', '_ExpiryDownloadModal.cshtml', '_OperatorNoSkillDownloadModal.cshtml', '_ObsoletedDownloadModal.cshtml', '_ResignDownloadModal.cshtml', '_SectionDownloadModal.cshtml']);
    const expected = ranges.currentYear();
    for (const prefix of ['dq','ex','ons','ob','rs','secDl']) {
        assert.equal(fixture.elements.get(prefix + 'StartModal').value, expected.start, prefix);
        assert.equal(fixture.elements.get(prefix + 'EndModal').value, expected.end, prefix);
    }
    fixture.context.setPresetModal(0);
    const expiry = fixture.groups.get('expiryModal');
    assert.equal(expiry.find(b => b.getAttribute() === 'year').active, true);
    assert.equal(expiry.find(b => b.getAttribute() === '0').active, false);
    assert.equal(fixture.elements.get('dqStartModal').value, '2000-01-01');
});

test('DownloadSection initialization and reopening keep an explicitly chosen range', () => {
    const source = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/Pages/DownloadSection.cshtml'), 'utf8');
    const startAt = source.indexOf('function setOnsPreset(days){');
    const endAt = source.indexOf('// relocate modals', startAt);
    const nodes = new Map();
    const node = id => {
        if (!nodes.has(id)) nodes.set(id, { value: '', textContent: '', listeners: {}, querySelectorAll: () => [],
            addEventListener(name, fn) { this.listeners[name] = fn; }, classList: { add() {} } });
        return nodes.get(id);
    };
    const context = { document: { getElementById: node }, ReportDateRanges: ranges };
    vm.createContext(context); vm.runInContext(source.slice(startAt, endAt), context);
    assert.equal(node('onsStart').value, ranges.currentYear().start);
    node('onsStart').value = '2023-05-01'; node('onsEnd').value = '2023-06-01';
    node('operatorNoSkillModal').listeners['show.bs.modal']();
    assert.equal(node('onsStart').value, '2023-05-01');
    assert.equal(node('onsEnd').value, '2023-06-01');
    context.setOnsPreset('year');
    assert.equal(node('onsStart').value, ranges.currentYear().start);
    context.setOnsPreset(0);
    assert.equal(node('onsStart').value, ''); assert.equal(node('onsEnd').value, '');
});
