const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const webRoot = path.join(root, 'OperatorCertificationRecord.Web/wwwroot');
const cssPath = path.join(webRoot, 'lib/bootstrap-icons/font/bootstrap-icons.css');

for (const page of [
    'OperatorCertificationRecord.Web/Pages/Shared/_Layout.cshtml',
    'OperatorCertificationRecord.Web/Pages/Login.cshtml',
    'dashboard.html'
]) {
    test(`${page}: Bootstrap Icons loads from local assets`, () => {
        const source = fs.readFileSync(path.join(root, page), 'utf8');
        const links = [...source.matchAll(/<link\b[^>]*\bhref=["']([^"']*bootstrap-icons\.css(?:\?[^"']*)?)["'][^>]*>/gi)];
        assert.ok(links.length > 0, 'Bootstrap Icons stylesheet link must exist');
        for (const [, href] of links) {
            assert.doesNotMatch(href, /^(?:[a-z][a-z\d+.-]*:)?\/\//i, 'Icon links must not request a third-party host');
            const asset = href.split('?')[0];
            const resolved = asset.startsWith('~/')
                ? path.join(webRoot, asset.slice(2))
                : path.resolve(path.dirname(path.join(root, page)), asset);
            assert.equal(resolved, cssPath, 'Stylesheet link must resolve to the bundled icon CSS');
        }
    });
}

test('bundled Bootstrap Icons CSS loads valid local WOFF and WOFF2 fonts', () => {
    const css = fs.readFileSync(cssPath, 'utf8');
    assert.doesNotMatch(css, /@import\b/i, 'Icon CSS must not import external stylesheets');
    const urls = [...css.matchAll(/url\(\s*["']?([^\s"')]+)["']?\s*\)/gi)].map(match => match[1]);
    assert.equal(urls.length, 2, 'Both supported webfont formats must be bundled');
    const signatures = new Map([['.woff', 'wOFF'], ['.woff2', 'wOF2']]);
    for (const url of urls) {
        assert.doesNotMatch(url, /^(?:[a-z][a-z\d+.-]*:|\/)/i, 'Font URLs must resolve beside the bundled CSS');
        const fontPath = path.resolve(path.dirname(cssPath), url.split(/[?#]/)[0]);
        const extension = path.extname(fontPath);
        assert.ok(signatures.has(extension), `Unexpected font format: ${extension}`);
        const font = fs.readFileSync(fontPath);
        assert.equal(font.toString('ascii', 0, 4), signatures.get(extension), 'Font file must have a valid format header');
        signatures.delete(extension);
    }
    assert.equal(signatures.size, 0, 'Both WOFF and WOFF2 formats must be available');
});
