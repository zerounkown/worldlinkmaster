// Header currency switcher (AED/USD) — click to open, click outside or pick an option to close.
(function () {
    "use strict";

    var toggle = document.getElementById("currencySwitchToggle");
    var panel = document.getElementById("currencySwitchPanel");
    if (!toggle || !panel) {
        return;
    }

    toggle.addEventListener("click", function (e) {
        e.stopPropagation();
        var isOpen = panel.classList.toggle("open");
        toggle.setAttribute("aria-expanded", isOpen ? "true" : "false");
    });

    document.addEventListener("click", function (e) {
        if (!e.target.closest(".currency-switch")) {
            panel.classList.remove("open");
            toggle.setAttribute("aria-expanded", "false");
        }
    });
})();
