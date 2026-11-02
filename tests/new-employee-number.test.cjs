'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require.resolve('../OperatorCertificationRecord.Web/Pages/AddUser.cshtml'), 'utf8');
const input = source.match(/<input[^>]*id="EmpCode"[^>]*>/)[0];
const handler = input.match(/oninput="([^"]+)"/)[1];

for (const [typed, expected] of [['0402567', '0402567'], ['0000001', '0000001'], ['12a.34e-5', '12345'], ['123456789', '1234567']]) {
    test(`employee number input keeps only seven digits: ${typed}`, () => {
        const field = { value: typed };
        let preview;
        const context = vm.createContext({ field, syncPreview() { preview = field.value; } });
        vm.runInContext(`(function () { ${handler} }).call(field)`, context);
        assert.equal(field.value, expected);
        assert.equal(preview, expected);
    });
}
