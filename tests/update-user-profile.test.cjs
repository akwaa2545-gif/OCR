const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const page = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/Pages/UpdateUser.cshtml'), 'utf8');
const sections = page.slice(page.indexOf('    let sectionRequest ='), page.indexOf('    async function loadProcesses('));
const submit = page.slice(page.indexOf('    async function submitUpdate('), page.indexOf('    function showPromotionModal('));

function setup(fetch) {
    const elements = {
        DeptID: { value: 'D1' },
        SectID: { value: 'S2', innerHTML: '<option value="S2">Existing section</option>',
            dataset: { department: 'D1' }, options: [], add(option) { this.options.push(option); }, getAttribute: () => 'S1' },
        formActionInput: { value: '' },
        updateUserForm: { reportValidity: () => false, submit: () => { throw new Error('Invalid form submitted'); } }
    };
    const context = vm.createContext({ document: { getElementById: id => elements[id] }, fetch,
        Option: function(text, value) { this.text = text; this.value = value; },
        Toast: { info() {} }, setTimeout: fn => fn() });
    vm.runInContext(sections + submit, context);
    return { elements, context };
}

test('profile save does not submit invalid required fields', async () => {
    const { context } = setup();
    await vm.runInContext("submitUpdate('update')", context);
});

test('initial section load preserves the rendered current value without a request', async () => {
    const { context, elements } = setup(() => { throw new Error('Unexpected reload'); });
    await vm.runInContext('loadSections()', context);
    assert.equal(elements.SectID.value, 'S2');
    assert.match(elements.SectID.innerHTML, /Existing section/);
});

test('same-department workshop change does not reset a newly selected section', async () => {
    const { context, elements } = setup(() => { throw new Error('Unexpected reload'); });
    elements.SectID.value = 'S3';
    await vm.runInContext('loadSections()', context);
    assert.equal(elements.SectID.value, 'S3');
    assert.match(elements.SectID.innerHTML, /Existing section/);
});

test('valid profile save submits the update action', async () => {
    const { context, elements } = setup();
    let submitted = 0;
    elements.updateUserForm.reportValidity = () => true;
    elements.updateUserForm.submit = () => submitted++;
    await vm.runInContext("submitUpdate('update')", context);
    assert.equal(submitted, 1);
    assert.equal(elements.formActionInput.value, 'update');
});

test('save is blocked while section options load', async () => {
    const { context, elements } = setup();
    elements.SectID.disabled = true;
    elements.updateUserForm.reportValidity = () => true;
    await vm.runInContext("submitUpdate('update')", context);
    assert.equal(elements.formActionInput.value, '');
});

test('department change clears old section and safely builds new options', async () => {
    const { context, elements } = setup(async () => ({ ok: true, json: async () => [{ SectID: 'S4', SectName: '<b>Section</b>' }] }));
    elements.DeptID.value = 'D2';
    await vm.runInContext('loadSections()', context);
    assert.equal(elements.SectID.value, '');
    assert.equal(elements.SectID.options[0].text, '<b>Section</b>');
    assert.equal(elements.SectID.disabled, false);
});

test('failed section request permits retry without restoring stale selection', async () => {
    const { context, elements } = setup(async () => ({ ok: false }));
    elements.DeptID.value = 'D2';
    await vm.runInContext('loadSections()', context);
    assert.equal(elements.SectID.value, '');
    assert.equal(elements.SectID.disabled, false);
    assert.equal(elements.SectID.dataset.department, undefined);
    assert.match(elements.SectID.innerHTML, /Could not load/);
});

test('late response from previous department cannot overwrite latest options', async () => {
    const pending = [];
    const { context, elements } = setup(() => new Promise(resolve => pending.push(resolve)));
    elements.DeptID.value = 'D2';
    const old = vm.runInContext('loadSections()', context);
    elements.DeptID.value = 'D3';
    const latest = vm.runInContext('loadSections()', context);
    pending[1]({ ok: true, json: async () => [{ SectID: 'S3', SectName: 'Newest' }] });
    await latest;
    pending[0]({ ok: true, json: async () => [{ SectID: 'S2', SectName: 'Stale' }] });
    await old;
    assert.deepEqual(elements.SectID.options.map(x => x.value), ['S3']);
});

test('section request can be retried after failure', async () => {
    let calls = 0;
    const { context, elements } = setup(async () => (++calls === 1 ? { ok: false } :
        { ok: true, json: async () => [{ SectID: 'S4', SectName: 'Recovered' }] }));
    elements.DeptID.value = 'D2';
    await vm.runInContext('loadSections()', context);
    await vm.runInContext('loadSections()', context);
    assert.equal(calls, 2);
    assert.equal(elements.SectID.options[0].value, 'S4');
});

test('clearing department invalidates an outstanding section response', async () => {
    let resolve;
    const { context, elements } = setup(() => new Promise(done => { resolve = done; }));
    elements.DeptID.value = 'D2';
    const pending = vm.runInContext('loadSections()', context);
    elements.DeptID.value = '';
    await vm.runInContext('loadSections()', context);
    resolve({ ok: true, json: async () => [{ SectID: 'S4', SectName: 'Stale' }] });
    await pending;
    assert.equal(elements.SectID.options.length, 0);
    assert.equal(elements.SectID.value, '');
    assert.equal(elements.SectID.disabled, false);
});
