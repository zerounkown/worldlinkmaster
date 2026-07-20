// The "Shop" mega-menu opens on click and stays open (fixed) until the trigger is clicked
// again or the user clicks outside it — clicking a link inside just navigates away normally.
(function () {
    "use strict";

    var dropdowns = document.querySelectorAll(".main-nav-dropdown");
    if (dropdowns.length === 0) {
        return;
    }

    function closeAll() {
        dropdowns.forEach(function (d) {
            d.classList.remove("open");
            // The CSS also shows the panel on :focus-within, so a lingering focus (e.g. the
            // toggle link itself right after being clicked) would keep it visible even after
            // the "open" class is removed — clear focus so closing actually closes it.
            if (document.activeElement && d.contains(document.activeElement)) {
                document.activeElement.blur();
            }
        });
    }

    dropdowns.forEach(function (dropdown) {
        var toggle = dropdown.querySelector(".main-nav-link");
        if (!toggle) {
            return;
        }

        toggle.addEventListener("click", function (e) {
            e.preventDefault();
            var wasOpen = dropdown.classList.contains("open");
            closeAll();
            if (!wasOpen) {
                dropdown.classList.add("open");
            }
        });
    });

    document.addEventListener("click", function (e) {
        if (!e.target.closest(".main-nav-dropdown")) {
            closeAll();
        }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
            closeAll();
        }
    });
})();
