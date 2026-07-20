// Adds a shadow to the sticky header once it's actually pinned to the top of the viewport,
// so it doesn't look glued to the page when scrolled all the way up.
(function () {
    "use strict";

    var header = document.querySelector(".site-header-sticky");
    if (!header || !("IntersectionObserver" in window)) {
        return;
    }

    var sentinel = document.createElement("div");
    sentinel.style.position = "absolute";
    sentinel.style.top = "0";
    sentinel.style.height = "1px";
    sentinel.style.width = "1px";
    header.parentNode.insertBefore(sentinel, header);

    var observer = new IntersectionObserver(function (entries) {
        header.classList.toggle("is-stuck", !entries[0].isIntersecting);
    }, { threshold: 0 });
    observer.observe(sentinel);
})();
