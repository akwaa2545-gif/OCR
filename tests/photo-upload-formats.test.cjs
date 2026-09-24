const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function harness(page) {
    const source = fs.readFileSync(path.join(__dirname, '../OperatorCertificationRecord.Web/Pages', page + '.cshtml'), 'utf8');
    const start = source.indexOf('    /* ===== Crop Modal Logic ===== */');
    const end = source.indexOf(page === 'AddUser' ? '    const THAI_RE' : '    function updateThaiPrefix', start);
    const nodes = new Map();
    const alerts = [], callbacks = [], pending = [];
    function node(id) {
        if (!nodes.has(id)) {
            const element = { style: {}, files: [], classList: { remove() {} }, addEventListener(name, callback) { this[name] = callback; } };
            Object.defineProperty(element, 'value', { set(value) { if (value === '') this.files = []; } });
            nodes.set(id, element);
        }
        return nodes.get(id);
    }
    const context = vm.createContext({
        document: { getElementById: node, querySelectorAll: () => [], addEventListener: (_, callback) => callbacks.push(callback) },
        URL: { createObjectURL: () => 'blob:photo', revokeObjectURL() {} },
        alert: message => alerts.push(message), syncPreview() {},
        File: class { constructor(parts, name, options) { this.name = name; this.type = options.type; this.parts = parts; } },
        DataTransfer: class { constructor() { this.files = []; this.items = { add: file => this.files.push(file) }; } },
        Cropper: class { destroy() {} getCroppedCanvas() { return { toBlob: callback => pending.push(callback) }; } }
    });
    vm.runInContext(source.slice(start, end), context);
    callbacks.forEach(callback => callback());
    const select = (name, type = '', size = 1024) => {
        node('PhotoFile').files = [{ name, type, size }];
        context.previewPhoto({ target: node('PhotoFile') });
    };
    return { context, node, alerts, pending, select, source };
}

for (const page of ['AddUser', 'UpdateUser']) {
    for (const [extension, type] of [['jpg', 'image/jpeg'], ['jpeg', 'image/jpeg'], ['PNG', 'image/png'], ['gif', 'image/gif'], ['bmp', 'image/bmp'], ['webp', 'image/webp']]) {
        test(`${page}: ${extension} import is withheld until confirmed and normalized to JPEG`, () => {
            const h = harness(page);
            h.select('employee.' + extension, type);
            assert.equal(h.node('PhotoFile').files.length, 0);
            h.node('cropImage').onload();
            h.node('cropConfirmBtn').click();
            h.pending.shift()({ type: 'image/jpeg', size: 20 });
            assert.equal(h.node('PhotoFile').files[0].name, 'employee.jpg');
            assert.equal(h.node('PhotoFile').files[0].type, 'image/jpeg');
        });
    }
    test(`${page}: unsupported and oversized files rejected`, () => {
        for (const [name, type, size] of [['x.svg', 'image/svg+xml', 10], ['x.exe', 'image/jpeg', 10], ['x.png', 'text/plain', 10], ['x.png', 'image/png', 5 * 1024 * 1024 + 1]]) {
            const h = harness(page); h.select(name, type, size);
            assert.equal(h.node('PhotoFile').files.length, 0);
            assert.equal(h.alerts.length, 1);
        }
    });
    test(`${page}: decoding failure and empty encoding fail without uploading`, () => {
        const h = harness(page); h.select('x.png'); h.node('cropImage').onerror();
        assert.equal(h.alerts.length, 1);
        h.select('x.png'); h.node('cropImage').onload(); h.node('cropConfirmBtn').click(); h.pending.shift()(null);
        assert.equal(h.node('PhotoFile').files.length, 0);
        assert.equal(h.alerts.length, 2);
    });
    test(`${page}: canceled asynchronous encoding cannot upload old selection`, () => {
        const h = harness(page); h.select('x.png'); h.node('cropImage').onload(); h.node('cropConfirmBtn').click();
        h.context.closeCropModal(); h.pending.shift()({ type: 'image/jpeg', size: 20 });
        assert.equal(h.node('PhotoFile').files.length, 0);
    });
}
