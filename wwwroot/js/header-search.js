document.addEventListener('DOMContentLoaded', function () {
    var toggle = document.getElementById('headerSearchToggle');
    var panel = document.getElementById('headerSearchPanel');
    var closeBtn = document.getElementById('headerSearchClose');
    if (!toggle || !panel) return;

    function openPanel() {
        panel.classList.add('open');
        toggle.setAttribute('aria-expanded', 'true');
        var input = panel.querySelector('.header-search-input');
        if (input) input.focus();
    }

    function closePanel() {
        panel.classList.remove('open');
        toggle.setAttribute('aria-expanded', 'false');
    }

    toggle.addEventListener('click', function () {
        if (panel.classList.contains('open')) {
            closePanel();
        } else {
            openPanel();
        }
    });

    if (closeBtn) {
        closeBtn.addEventListener('click', closePanel);
    }

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') closePanel();
    });
});
