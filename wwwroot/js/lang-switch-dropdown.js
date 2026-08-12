// Header language switcher (English/Arabic) — same click-to-open pattern as the currency
// switcher next to it, so the two paired dropdowns behave identically.
(function () {
    "use strict";

    var toggle = document.getElementById("langSwitchToggle");
    var panel = document.getElementById("langSwitchPanel");
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
