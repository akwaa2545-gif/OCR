const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ReportDateRanges = require('../OperatorCertificationRecord.Web/wwwroot/js/report-date-ranges.js');

const reports = [
    ['_SkillInventoryDownloadModal.cshtml', 'skillInventoryModal', 'skInvCountBadge', 'skInvDownloadBtn', 'skInvSectionModal'],
    ['_ExpiryDownloadModal.cshtml', 'expiryModal', 'exCountBadgeModal', 'exDownloadBtnModal', 'exStartModal'],
    ['_DisqualificationDownloadModal.cshtml', 'disqualificationModal', 'dqCountBadgeModal', 'downloadBtnModal', 'dqStartModal'],
    ['_ObsoletedDownloadModal.cshtml', 'obsoletedModal', 'obCountBadge', 'obDownloadBtn', 'obStartModal'],
    ['_ResignDownloadModal.cshtml', 'resignModal', 'rsCountBadgeModal', 'rsDownloadBtnModal', 'rsStartModal'],
    ['_OperatorNoSkillDownloadModal.cshtml', 'operatorNoSkillModal', 'onsCountBadgeModal', 'onsDownloadBtnModal', 'onsStartModal'],
    ['_SectionDownloadModal.cshtml', 'sectionDlModal', 'secDlCountBadge', 'secDlDownloadBtn', 'secDlSectModal'],
    ['DownloadSection.cshtml', 'skillInventoryModal', 'skillCountBadge', 'downloadSkillBtn', 'skillSectionSelect', 'inventory'],
    ['DownloadSection.cshtml', 'operatorNoSkillModal', 'onsCountBadge', 'downloadOnsBtn', 'onsStart', 'noskill']
];
const json = value => ({ ok: true, headers: { get: () => 'application/json' }, json: async () => value });
const tick = () => new Promise(setImmediate);

function fixture(report, options = {}) {
    const source = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/Pages', report[5] ? '' : 'Shared', report[0]), 'utf8');
    const script = report[5] ? source.slice(source.indexOf(report[5] === 'inventory' ? '// Skill Inventory modal behavior' : '// Operator No-skill modal behavior'), source.indexOf(report[5] === 'inventory' ? '// preset helper used by skill preset buttons' : '// relocate modals', source.indexOf('// Skill Inventory modal behavior'))) : /<script>([\s\S]*?)<\/script>/.exec(source)[1];
    const nodes = new Map(), timers = new Map(), requests = [];
    let timerId = 0;
    const node = id => {
        if (!nodes.has(id)) {
            const classes = new Set(id === report[3] ? ['dl-pending'] : []);
            const listeners = new Map();
            let markup = '';
            nodes.set(id, {
                id, value: id.includes('Format') ? 'xlsx' : '', checked: false, disabled: false, textContent: '',
                style: { removeProperty(key) { delete this[key]; } },
                classList: { add: c => classes.add(c), remove: c => classes.delete(c), contains: c => classes.has(c), toggle(c, on) { on ? classes.add(c) : classes.delete(c); } },
                get innerHTML() { return markup; }, set innerHTML(value) { markup = value; this.value = ''; },
                addEventListener(event, callback) { if (!listeners.has(event)) listeners.set(event, []); if (!listeners.get(event).includes(callback)) listeners.get(event).push(callback); },
                querySelectorAll() { return []; },
                dispatchEvent(event) { this.trigger(event.type); },
                async trigger(event) { for (const callback of listeners.get(event) || []) await callback.call(this, { preventDefault() {} }); await tick(); }
            });
        }
        return nodes.get(id);
    };
    const context = {
        document: { getElementById: node }, ReportDateRanges, Event: class Event { constructor(type) { this.type = type; } },
        console: { error() {}, warn() {}, debug() {} },
        location: { href: '' }, setTimeout(callback) { timers.set(++timerId, callback); return timerId; }, clearTimeout(id) { timers.delete(id); },
        fetch: async url => {
            requests.push(url);
            if (url.includes('/meta/')) return options.metaFailure ? { ok: false, status: 503 } : json({ sections: ['Inspection'], departments: ['QA'] });
            return options.count ? options.count(url) : json({ count: 42 });
        }
    };
    context.window = context;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/wwwroot/js/download-preview.js'), 'utf8'), context);
    vm.runInContext(script, context, { filename: report[0] });
    async function flush() { const queued = [...timers.values()]; timers.clear(); queued.forEach(callback => callback()); await tick(); await tick(); }
    return { node, requests, context, flush, counts: () => requests.filter(url => url.includes('/count')), open: () => node(report[1]).trigger('shown.bs.modal'), close: () => node(report[1]).trigger('hidden.bs.modal') };
}

for (const report of reports) {
    test(`${report[1]} automatically previews valid filters without downloading`, async () => {
        const page = fixture(report);
        await page.open();
        if (report[1] === 'sectionDlModal') {
            assert.equal(page.counts().length, 0, 'Section remains required');
            page.node(report[4]).value = 'Inspection';
            await page.node(report[4]).trigger('change');
        }
        await page.flush();
        assert.equal(page.counts().length, 1);
        assert.equal(page.node(report[2]).textContent, '42');
        assert.equal(page.node(report[3]).classList.contains('dl-pending'), false);
        assert.equal(page.context.location.href, '');
    });
}

for (const report of reports.filter(report => report[1] !== 'sectionDlModal')) {
    test(`${report[1]} previews even when filter metadata is unavailable`, async () => {
        const page = fixture(report, { metaFailure: true });
        await page.open(); await page.flush();
        assert.equal(page.counts().length, 1);
        assert.equal(page.node(report[2]).textContent, '42');
    });
    test(`${report[1]} refreshes once on reopening and cancels a closed debounce`, async () => {
        const page = fixture(report);
        await page.open(); await page.close(); await page.flush();
        assert.equal(page.counts().length, 0);
        await page.open(); await page.flush();
        assert.equal(page.counts().length, 1);
        assert.equal(page.context.location.href, '');
    });
}

test('filter changes invalidate a previous count immediately and ignore its delayed response', async () => {
    let finish;
    const page = fixture(reports[1], { count: () => new Promise(resolve => { finish = resolve; }) });
    await page.open(); await page.flush();
    page.node('exStartModal').value = '2026-02-01';
    await page.node('exStartModal').trigger('input');
    assert.equal(page.node('exDownloadBtnModal').classList.contains('dl-pending'), true);
    finish(json({ count: 99 })); await tick(); await tick();
    assert.notEqual(page.node('exCountBadgeModal').textContent, '99');
    assert.equal(page.node('exDownloadBtnModal').classList.contains('dl-pending'), true);
    await page.flush(); finish(json({ count: 3 })); await tick(); await tick();
    assert.equal(page.node('exCountBadgeModal').textContent, '3');
});

test('failed automatic count keeps downloading pending', async () => {
    const page = fixture(reports[1], { count: () => ({ ok: false, status: 503 }) });
    await page.open(); await page.flush();
    assert.equal(page.node('exCountBadgeModal').textContent, 'Err');
    assert.equal(page.node('exDownloadBtnModal').classList.contains('dl-pending'), true);
    assert.equal(page.context.location.href, '');
});

for (const [index, endId, errorId, previewId] of [
    [1, 'exEndModal', 'exDateErr', 'exPreviewCountModal'],
    [2, 'dqEndModal', 'dqDateErr', 'previewCountModal'],
    [3, 'obEndModal', 'obDateErr', 'obPreviewBtn'],
    [4, 'rsEndModal', 'rsDateErr', 'rsPreviewCountModal']
]) {
    test(`${reports[index][1]} enables Download after validation and a new successful count`, async () => {
        const report = reports[index], page = fixture(report);
        await page.open(); await page.flush();
        page.node(report[4]).value = page.node(endId).value;
        await page.node(report[4]).trigger('input');
        page.context.validateDateRange(report[4], endId, errorId, previewId, report[3]);
        assert.equal(page.node(report[3]).disabled, true);
        await page.flush();
        assert.equal(page.node(report[3]).classList.contains('dl-pending'), false);
        assert.equal(page.node(report[3]).disabled, false);
    });
}

for (const report of reports.slice(7)) {
    test(`${report[5]} page modal invalidates after filter changes and enables its real download button`, async () => {
        const page = fixture(report);
        await page.open(); await page.flush();
        assert.equal(page.node(report[3]).disabled, false);
        if (report[5] === 'inventory') {
            page.node('skillAllDepartments').checked = false;
            await page.node('skillAllDepartments').trigger('change');
            assert.equal(page.node(report[3]).disabled, true);
            page.node('skillSectionSelect').value = 'Inspection';
            await page.node('skillSectionSelect').trigger('change');
        } else {
            page.context.setOnsPreset(0);
            assert.equal(page.node('onsStart').value, '');
            assert.equal(page.node('onsEnd').value, '');
            assert.equal(page.node(report[3]).disabled, true);
        }
        await page.flush();
        assert.equal(page.counts().length, 2);
        assert.equal(page.node(report[3]).disabled, false);
        if (report[5] === 'noskill') {
            assert.equal(new URL(page.counts().at(-1), 'http://localhost').searchParams.has('start'), false);
        }
        assert.equal(page.context.location.href, '');
    });
}
