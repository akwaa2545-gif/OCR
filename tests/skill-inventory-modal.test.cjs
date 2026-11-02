const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
    '../OperatorCertificationRecord.Web/Pages/Shared/_SkillInventoryDownloadModal.cshtml'), 'utf8');
const script = /<script>([\s\S]*?)<\/script>/.exec(source)[1];

function fixture(response) {
    const nodes = new Map();
    const node = id => {
        if (!nodes.has(id)) {
            const classes = new Set(id === 'skInvDownloadBtn' ? ['dl-pending'] : []);
            nodes.set(id, {
                id, value: id === 'skInvFormatModal' ? 'xlsx' : '', checked: false,
                disabled: false, textContent: '', style: { removeProperty(key) { delete this[key]; } },
                listeners: {}, classList: {
                    add: value => classes.add(value), remove: value => classes.delete(value),
                    contains: value => classes.has(value)
                },
                addEventListener(name, callback) { this.listeners[name] = callback; }
            });
        }
        return nodes.get(id);
    };
    const requests = [];
    const context = {
        document: { getElementById: node },
        location: { href: '' },
        console: { error() {} },
        setTimeout, clearTimeout,
        fetch: async url => { requests.push(url); return response; }
    };
    context.window = context;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/wwwroot/js/download-preview.js'), 'utf8'), context);
    vm.runInContext(script, context, { filename: '_SkillInventoryDownloadModal.cshtml' });
    const trigger = async (id, event) => {
        const element = node(id);
        element.listeners[event].call(element, { preventDefault() {} });
        // Inline event wrappers intentionally do not return their async task.
        await new Promise(setImmediate);
    };
    return { node, requests, context, trigger };
}

test('skill inventory modal inline JavaScript parses', () => {
    assert.doesNotThrow(() => new vm.Script(script));
});

test('All Departments previews count and allows an Excel download', async () => {
    const page = fixture({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ count: 42 }) });
    const allDepartments = page.node('skInvAllDeptModal');
    allDepartments.checked = true;
    page.node('skInvSectionModal').value = 'Old section';
    await page.trigger('skInvAllDeptModal', 'change');

    await new Promise(resolve => setTimeout(resolve, 350));
    assert.deepEqual(page.requests, ['/api/download/skillinventory/count']);
    assert.equal(page.node('skInvCountBadge').textContent, '42');
    assert.equal(page.node('skInvDownloadBtn').classList.contains('dl-pending'), false);
    assert.equal(page.node('skInvSectionModal').disabled, true);
    assert.equal(page.node('skInvSectionModal').value, '');
    assert.equal(page.node('skInvLoading').style.display, 'none');

    await page.trigger('skInvDownloadBtn', 'click');
    assert.equal(page.context.location.href, '/Download/SkillInventory?format=xlsx');
    assert.equal(page.requests.length, 1);
});

test('failed preview keeps download pending and cannot navigate', async () => {
    const page = fixture({ ok: false, status: 503 });
    page.node('skInvAllDeptModal').checked = true;
    await page.trigger('skInvAllDeptModal', 'change');

    await new Promise(resolve => setTimeout(resolve, 350));
    assert.equal(page.node('skInvCountBadge').textContent, 'Err');
    assert.equal(page.node('skInvDownloadBtn').classList.contains('dl-pending'), true);
    assert.equal(page.node('skInvPreviewBtn').disabled, false);
    assert.equal(page.node('skInvLoading').style.display, 'none');

    await page.trigger('skInvDownloadBtn', 'click');
    assert.equal(page.node('skInvDownloadBtn').classList.contains('dl-pending'), true);
    assert.equal(page.context.location.href, '');
    assert.equal(page.requests.length, 2);
});
