const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function page(name) {
    const source = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/Pages', name + '.cshtml'), 'utf8');
    const start = source.indexOf('    function autoCalculateExpiryDate()');
    const end = source.indexOf(name === 'UpdateSkill' ? '    // submit helper:' : '    // Auto-collapse left sidebar', start);
    const nodes = new Map(), ready = [];
    function node(id) {
        if (!nodes.has(id)) {
            const classes = new Set(), listeners = new Map();
            nodes.set(id, { value: '', style: {}, files: [], disabled: false, required: false, title: '',
                options: [{ text: 'Workshop' }], selectedIndex: 0, getAttribute: () => '',
                classList: { add: (...items) => items.forEach(item => classes.add(item)), remove: (...items) => items.forEach(item => classes.delete(item)),
                    toggle(item, on) { on ? classes.add(item) : classes.delete(item); }, contains: item => classes.has(item) },
                addEventListener(type, listener) { const handlers = listeners.get(type) || []; handlers.push(listener); listeners.set(type, handlers); },
                dispatchEvent(event) { event.preventDefault ||= () => {}; for (const listener of listeners.get(event.type) || []) listener.call(this, event); }
            });
        }
        return nodes.get(id);
    }
    for (const [id, value] of Object.entries({ Skill_FullScore: '100', Skill_ActualScore: '95', Skill_KnowledgeScore: '80', Skill_SkillScore: '90',
        Skill_WorkshopID: 'W01', Skill_ProcessName: 'Process A', Skill_OperatorTraining: 'Periodic', Skill_CertifiedDate: '2026-10-30', Skill_ExpiryDate: '2027-10-30' })) node(id).value = value;
    node('Skill_ProcessName').required = true;
    const context = vm.createContext({ console, document: {
        getElementById: node,
        querySelector(selector) { const match = selector.match(/\[name="([^"]+)"\]/); return match ? node(match[1]) : node(selector); },
        querySelectorAll: () => [],
        addEventListener: (_, callback) => ready.push(callback)
    }, fetch: () => new Promise(() => {}), Event: class { constructor(type) { this.type = type; } },
        DataTransfer: class { constructor() { this.files = []; this.items = { add: file => this.files.push(file) }; } } });
    vm.runInContext(source.slice(start, end), context);
    ready.forEach(callback => callback());
    return { node, save: node(name === 'UpdateSkill' ? 'confirmSaveBtn' : 'saveSkillBtn'),
        choose(file) { node('skillFileInput').files = file ? [file] : []; node('skillFileInput').dispatchEvent({ type: 'change' }); },
        drop(file) { node('uploadZone').dispatchEvent({ type: 'drop', dataTransfer: { files: [file] } }); }
    };
}

for (const name of ['AddSkill', 'UpdateSkill']) {
    test(`${name}: selecting a valid PDF immediately clears the file error and enables Save`, () => {
        const view = page(name);
        assert.equal(view.save.disabled, true);
        view.choose({ name: 'replacement.pdf', type: 'application/pdf' });
        assert.equal(view.save.disabled, false);
        assert.equal(view.node('skillFileError').classList.contains('d-none'), true);
        view.choose(null);
        assert.equal(view.save.disabled, true);
    });
    test(`${name}: dropping a valid PDF revalidates without editing an unrelated score`, () => {
        const view = page(name);
        view.drop({ name: 'replacement.pdf', type: 'application/pdf' });
        assert.equal(view.save.disabled, false);
        assert.equal(view.node('skillFileInput').files.length, 1);
    });
}
