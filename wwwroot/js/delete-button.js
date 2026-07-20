// Plays a short "trash can closing" animation on .btn-delete-animated buttons, then submits
// the button's form for real. Delegated to document so it works for every instance on the
// page (Cart, Admin/Merchant delete confirmations) without per-button wiring.
(function () {
    var ANIMATION_MS = 750;

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.btn-delete-animated');
        if (!btn || btn.classList.contains('is-deleting')) {
            return;
        }

        var form = btn.closest('form');
        if (!form) {
            return;
        }

        e.preventDefault();
        btn.classList.add('is-deleting');
        btn.disabled = true;

        window.setTimeout(function () {
            form.submit();
        }, ANIMATION_MS);
    });
})();
