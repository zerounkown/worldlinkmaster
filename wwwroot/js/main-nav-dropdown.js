// Standard mega-menu pattern: hovering a nav item ("New Arrivals"/"Shop"/"Categories") opens
// its panel automatically, and clicking the trigger link navigates normally (it's a real
// <a href>, not just a toggle). Closing on mouseleave is delayed briefly rather than instant —
// without that delay, moving the cursor from the trigger down into the panel would cross the
// visual gap between them (the panel's margin-top) and close the menu before the cursor ever
// reaches it, since that gap is outside both elements' boxes for an instant. The delay gives
// the follow-up mouseenter (fired once the cursor reaches the panel, a DOM descendant of the
// same .main-nav-dropdown wrapper) time to cancel the pending close.
(function () {
    "use strict";

    var dropdowns = document.querySelectorAll(".main-nav-dropdown");
    if (dropdowns.length === 0) {
        return;
    }

    var CLOSE_DELAY_MS = 200;
    var closeTimer = null;

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

    function scheduleClose() {
        clearTimeout(closeTimer);
        closeTimer = setTimeout(closeAll, CLOSE_DELAY_MS);
    }

    function openNow(dropdown) {
        clearTimeout(closeTimer);
        dropdowns.forEach(function (d) {
            d.classList.toggle("open", d === dropdown);
        });
    }

    dropdowns.forEach(function (dropdown) {
        dropdown.addEventListener("mouseenter", function () {
            openNow(dropdown);
        });
        dropdown.addEventListener("mouseleave", scheduleClose);
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
