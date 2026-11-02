(function (root, factory) {
    'use strict';
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else api.init(root.document, root);
})(typeof window === 'undefined' ? globalThis : window, function () {
    'use strict';
    const fields = ['Operator', 'Process', 'Skill', 'Handicap', 'OneSkill', 'NoSkill'];
    const departments = {
        'Ta Manufacturing': ['ta', 'pcTa'], 'QA': ['qa', 'pcQa'],
        'SC Manufacturing': ['sc', 'pcSc'], 'Production & Material Planning': ['planning', 'pcPlanning']
    };
    function init(doc, win) {
        const values = fields.map(field => doc.getElementById('kpi' + field));
        if (values.some(value => !value)) return;
        const all = values.map(value => {
            const raw = value.getAttribute('data-all');
            return typeof raw === 'string' && raw.trim() ? Number(raw.replace(/,/g, '')) : NaN;
        });
        let data = {};
        try { data = JSON.parse(doc.getElementById('dashboard-department-kpis')?.textContent || '{}') || {}; }
        catch { /* A missing snapshot is shown as unavailable when selected. */ }
        let selected = 'all';
        function select(department) {
            const supplied = Object.hasOwn(data, department) ? data[department] : null;
            const counts = department === 'all' ? all : fields.map(field => supplied?.[field]);
            const status = doc.getElementById('kpi-filter-status');
            if (counts.some(count => !Number.isSafeInteger(count) || count < 0)) {
                if (status) status.textContent = 'Department totals are unavailable. Please refresh and try again.';
                return;
            }
            if (status) status.textContent = '';
            selected = department;
            values.forEach((value, index) => { value.textContent = counts[index].toLocaleString('en-US'); });
            doc.querySelectorAll('.kpi-dept-btn').forEach(button => {
                const active = button.dataset.department === selected;
                button.classList[active ? 'add' : 'remove']('active');
                button.setAttribute('aria-pressed', String(active));
            });
            Object.values(departments).forEach(([key, id]) => {
                doc.getElementById(id)?.classList[key === selected ? 'add' : 'remove']('active-dept');
            });
        }
        win.filterKpiByDept = department => select(department !== 'all' && department === selected ? 'all' : department);
        win.handleProcessClick = name => {
            const department = Object.hasOwn(departments, name) ? departments[name][0] : 'all';
            win.filterKpiByDept(department);
        };
        select('all');
    }
    return { init };
});
