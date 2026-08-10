(function () {
    var toggle = document.getElementById("whatsappWidgetToggle");
    var panel = document.getElementById("whatsappWidgetPanel");
    var close = document.getElementById("whatsappWidgetClose");
    var widget = document.getElementById("whatsappWidget");

    if (!toggle || !panel || !widget) {
        return;
    }

    function openPanel() {
        panel.hidden = false;
        toggle.setAttribute("aria-expanded", "true");
    }

    function closePanel() {
        panel.hidden = true;
        toggle.setAttribute("aria-expanded", "false");
    }

    toggle.addEventListener("click", function () {
        if (panel.hidden) {
            openPanel();
        } else {
            closePanel();
        }
    });

    if (close) {
        close.addEventListener("click", closePanel);
    }

    document.addEventListener("click", function (e) {
        if (!panel.hidden && !widget.contains(e.target)) {
            closePanel();
        }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && !panel.hidden) {
            closePanel();
        }
    });
})();
