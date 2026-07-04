// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Product listing cards: hovering (or tapping, on touch devices) a color swatch
// previews that color's photo on the card image, so customers can compare colors
// without opening the product page.
(function () {
    document.querySelectorAll(".product-card").forEach(function (card) {
        var img = card.querySelector(".card-product-image");
        var dots = card.querySelectorAll(".card-color-dot");
        if (!img || dots.length === 0) {
            return;
        }

        var defaultSrc = img.getAttribute("data-default-image") || img.getAttribute("src");

        dots.forEach(function (dot) {
            var colorImage = dot.getAttribute("data-image");
            if (!colorImage) {
                return;
            }

            dot.addEventListener("mouseenter", function () {
                img.setAttribute("src", colorImage);
            });

            dot.addEventListener("mouseleave", function () {
                img.setAttribute("src", defaultSrc);
            });

            // Touch devices: tap a swatch to preview, tap elsewhere to reset.
            dot.addEventListener("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                img.setAttribute("src", colorImage);
                dots.forEach(function (d) { d.classList.remove("active"); });
                dot.classList.add("active");
            });
        });
    });
})();

// Reusable 3D tilt: the element rotates toward the cursor for a "pop off the
// page" feel, and settles back flat when the mouse leaves.
function enable3dTilt(selector, maxTilt, lift) {
    document.querySelectorAll(selector).forEach(function (el) {
        el.addEventListener("mousemove", function (e) {
            var rect = el.getBoundingClientRect();
            var x = (e.clientX - rect.left) / rect.width;
            var y = (e.clientY - rect.top) / rect.height;
            var rotateY = (x - 0.5) * maxTilt * 2;
            var rotateX = (0.5 - y) * maxTilt * 2;
            el.style.transform = "perspective(1000px) rotateX(" + rotateX + "deg) rotateY(" + rotateY + "deg)" + (lift ? " translateY(" + lift + "px)" : "");
        });

        el.addEventListener("mouseleave", function () {
            el.style.transform = "";
        });
    });
}

// Category tiles pop up toward the cursor.
enable3dTilt(".category-tile-inner", 10, -10);

// Product images (home, catalog, related products) tilt on hover so browsing
// feels more tactile and the picture "reacts" to the customer.
enable3dTilt(".product-card-tilt", 8, 0);

// Category preview cards (inside the side nav) tilt on hover, same 3D treatment.
enable3dTilt(".category-preview-card", 12, -6);

// Side nav category previews: hovering a category (or tapping its chevron on
// touch devices) reveals a handful of its products so customers get a peek
// before clicking in. Only one preview stays open at a time.
(function () {
    var items = document.querySelectorAll(".side-nav-item");
    if (items.length === 0) {
        return;
    }

    function closeAll(except) {
        items.forEach(function (item) {
            if (item === except) {
                return;
            }
            item.classList.remove("preview-open");
            var otherToggle = item.querySelector(".side-nav-preview-toggle");
            if (otherToggle) {
                otherToggle.setAttribute("aria-expanded", "false");
            }
        });
    }

    items.forEach(function (item) {
        var toggle = item.querySelector(".side-nav-preview-toggle");
        if (!toggle) {
            return;
        }

        item.addEventListener("mouseenter", function () {
            closeAll(item);
            item.classList.add("preview-open");
            toggle.setAttribute("aria-expanded", "true");
        });

        item.addEventListener("mouseleave", function () {
            item.classList.remove("preview-open");
            toggle.setAttribute("aria-expanded", "false");
        });

        toggle.addEventListener("click", function (e) {
            e.preventDefault();
            e.stopPropagation();
            var isOpen = item.classList.contains("preview-open");
            closeAll(item);
            item.classList.toggle("preview-open", !isOpen);
            toggle.setAttribute("aria-expanded", String(!isOpen));
        });
    });
})();

// Side nav (off-canvas category drawer): toggle button opens it, the close
// button / overlay click / Escape key all close it.
(function () {
    var toggle = document.getElementById("sideNavToggle");
    var sideNav = document.getElementById("sideNav");
    var overlay = document.getElementById("sideNavOverlay");
    var closeBtn = document.getElementById("sideNavClose");

    if (!toggle || !sideNav || !overlay) {
        return;
    }

    function openNav() {
        sideNav.classList.add("open");
        overlay.classList.add("open");
        sideNav.setAttribute("aria-hidden", "false");
        toggle.setAttribute("aria-expanded", "true");
        document.body.style.overflow = "hidden";
    }

    function closeNav() {
        sideNav.classList.remove("open");
        overlay.classList.remove("open");
        sideNav.setAttribute("aria-hidden", "true");
        toggle.setAttribute("aria-expanded", "false");
        document.body.style.overflow = "";
    }

    toggle.addEventListener("click", openNav);
    overlay.addEventListener("click", closeNav);
    if (closeBtn) {
        closeBtn.addEventListener("click", closeNav);
    }
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
            closeNav();
        }
    });
})();

// Floating nav controls: browser back/forward, and jump straight to the top
// of the page. The "scroll to top" button only shows once you've scrolled down.
(function () {
    var backBtn = document.getElementById("navBackBtn");
    var forwardBtn = document.getElementById("navForwardBtn");
    var topBtn = document.getElementById("scrollTopBtn");

    if (!backBtn || !forwardBtn || !topBtn) {
        return;
    }

    backBtn.addEventListener("click", function () {
        history.back();
    });

    forwardBtn.addEventListener("click", function () {
        history.forward();
    });

    topBtn.addEventListener("click", function () {
        window.scrollTo({ top: 0, behavior: "smooth" });
    });

    function toggleTopButton() {
        if (window.scrollY > 300) {
            topBtn.classList.add("visible");
        } else {
            topBtn.classList.remove("visible");
        }
    }

    window.addEventListener("scroll", toggleTopButton, { passive: true });
    toggleTopButton();
})();
