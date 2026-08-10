// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Best-selling product carousel: arrow buttons scroll the track by one card's
// width at a time. Scroll direction is left as plain +/-step regardless of
// language direction — modern browsers already reverse the scrollLeft axis to
// match reading order under dir="rtl", so this works unmodified for Arabic too.
(function () {
    document.querySelectorAll(".product-carousel").forEach(function (carousel) {
        var track = carousel.querySelector(".product-carousel-track");
        var prevBtn = carousel.querySelector(".product-carousel-prev");
        var nextBtn = carousel.querySelector(".product-carousel-next");
        if (!track || !prevBtn || !nextBtn) {
            return;
        }

        function scrollByStep(direction) {
            var item = track.querySelector(".product-carousel-item");
            var step = item ? item.getBoundingClientRect().width + 24 : 260;
            track.scrollBy({ left: direction * step, behavior: "smooth" });
        }

        prevBtn.addEventListener("click", function () { scrollByStep(-1); });
        nextBtn.addEventListener("click", function () { scrollByStep(1); });
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

// Price-range filter: two overlaid <input type="range"> thumbs sharing one track,
// kept in sync with the visible number boxes and the gold fill between the handles.
// The form submits on release (range "change") or when a number box is edited, so
// there's no separate Apply button.
(function () {
    document.querySelectorAll(".price-range-slider").forEach(function (slider) {
        var form = slider.closest("form");
        var fill = slider.querySelector(".price-range-fill");
        var minRange = slider.querySelector(".price-range-input-min");
        var maxRange = slider.querySelector(".price-range-input-max");
        var minBox = form ? form.querySelector("#minPriceInput") : null;
        var maxBox = form ? form.querySelector("#maxPriceInput") : null;
        if (!fill || !minRange || !maxRange || !minBox || !maxBox) {
            return;
        }

        var bounds = { min: Number(slider.dataset.rangeMin), max: Number(slider.dataset.rangeMax) };

        function clamp(value) {
            return Math.min(Math.max(value, bounds.min), bounds.max);
        }

        function updateFill() {
            var lo = Number(minRange.value);
            var hi = Number(maxRange.value);
            var span = bounds.max - bounds.min || 1;
            fill.style.left = ((lo - bounds.min) / span * 100) + "%";
            fill.style.right = (100 - (hi - bounds.min) / span * 100) + "%";
        }

        function fromRanges() {
            if (Number(minRange.value) > Number(maxRange.value)) {
                minRange.value = maxRange.value;
            }
            minBox.value = minRange.value;
            maxBox.value = maxRange.value;
            updateFill();
        }

        function fromBoxes() {
            var lo = clamp(Number(minBox.value) || bounds.min);
            var hi = clamp(Number(maxBox.value) || bounds.max);
            if (lo > hi) {
                hi = lo;
            }
            minBox.value = lo;
            maxBox.value = hi;
            minRange.value = lo;
            maxRange.value = hi;
            updateFill();
        }

        minRange.addEventListener("input", fromRanges);
        maxRange.addEventListener("input", fromRanges);
        [minRange, maxRange].forEach(function (input) {
            input.addEventListener("change", function () {
                if (form) form.requestSubmit();
            });
        });

        [minBox, maxBox].forEach(function (box) {
            box.addEventListener("change", function () {
                fromBoxes();
                if (form) form.requestSubmit();
            });
        });

        updateFill();
    });
})();

// A price box sitting exactly at its own min/max attribute (e.g. the min box at the cheapest
// item's price floor) is functionally "no filter" — but every filter click resubmits the whole
// form, so an untouched box like that would bake a literal minPrice=0 (or maxPrice=<ceiling>)
// into the URL and keep silently re-submitting it forever after, since the box always renders
// back to that same boundary value. Applies to every submit trigger (checkboxes, sort, the
// slider itself), not just direct edits to the price boxes.
(function () {
    var form = document.getElementById("filterForm");
    var minBox = document.getElementById("minPriceInput");
    var maxBox = document.getElementById("maxPriceInput");
    if (!form || !minBox || !maxBox) {
        return;
    }

    form.addEventListener("submit", function () {
        if (Number(minBox.value) === Number(minBox.min)) minBox.disabled = true;
        if (Number(maxBox.value) === Number(maxBox.max)) maxBox.disabled = true;
    });

    // The browser back/forward cache can restore this exact page with the boxes still
    // disabled from the last submit — re-enable so a follow-up edit works normally.
    window.addEventListener("pageshow", function () {
        minBox.disabled = false;
        maxBox.disabled = false;
    });
})();

// Filter panel: any checkbox/radio/swatch click resubmits the form immediately (no Apply
// button), and the brand list's "Show more" toggles the overflow rows past the first 5.
(function () {
    var form = document.getElementById("filterForm");
    if (!form) {
        return;
    }

    form.querySelectorAll('.filter-body input[type="checkbox"], .filter-body input[type="radio"]').forEach(function (input) {
        input.addEventListener("change", function () {
            form.requestSubmit();
        });
    });

    document.querySelectorAll(".filter-show-more").forEach(function (button) {
        var showMoreText = button.textContent;
        var showLessText = button.dataset.showLess || showMoreText;
        button.addEventListener("click", function () {
            var target = document.getElementById(button.dataset.target);
            if (!target) {
                return;
            }
            var expanded = target.classList.toggle("filter-expanded");
            target.querySelectorAll(".filter-extra").forEach(function (row) {
                row.classList.toggle("filter-extra-visible", expanded);
            });
            button.textContent = expanded ? showLessText : showMoreText;
        });
    });
})();

// Favorite (heart) toggle — same AJAX + toast pattern as product-quick-add.js's cart calls.
// Only rendered as a real button when the shopper is signed in (guests get a plain login
// link instead), so this never has to handle an anonymous request.
(function () {
    function getToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    function updateFavoriteCount(count) {
        document.querySelectorAll(".favorites-count").forEach(function (el) {
            el.textContent = count;
        });
    }

    function showFavoriteToast(message, isError) {
        var container = document.getElementById("quickAddToastContainer");
        if (!container) {
            container = document.createElement("div");
            container.id = "quickAddToastContainer";
            container.className = "toast-container position-fixed bottom-0 end-0 p-3";
            container.style.zIndex = 1080;
            document.body.appendChild(container);
        }

        var toast = document.createElement("div");
        toast.className = "toast align-items-center text-white " + (isError ? "bg-danger" : "bg-success") + " border-0";
        toast.setAttribute("role", "alert");
        var textDiv = document.createElement("div");
        textDiv.className = "d-flex";
        var body = document.createElement("div");
        body.className = "toast-body";
        body.textContent = message;
        var closeBtn = document.createElement("button");
        closeBtn.type = "button";
        closeBtn.className = "btn-close btn-close-white me-2 m-auto";
        closeBtn.setAttribute("data-bs-dismiss", "toast");
        textDiv.appendChild(body);
        textDiv.appendChild(closeBtn);
        toast.appendChild(textDiv);
        container.appendChild(toast);
        var bsToast = new bootstrap.Toast(toast, { delay: 3000 });
        toast.addEventListener("hidden.bs.toast", function () { toast.remove(); });
        bsToast.show();
    }

    document.addEventListener("click", function (e) {
        var trigger = e.target.closest(".js-favorite-toggle");
        if (!trigger) {
            return;
        }
        e.preventDefault();

        var productId = trigger.getAttribute("data-product-id");
        var token = getToken();
        var body = new URLSearchParams();
        body.set("productId", productId);
        if (token) body.set("__RequestVerificationToken", token);

        trigger.disabled = true;
        fetch("/Favorites/Toggle", {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: body
        })
            .then(function (resp) { return resp.json().then(function (data) { return { ok: resp.ok, data: data }; }); })
            .then(function (result) {
                trigger.disabled = false;
                if (!result.ok || !result.data.success) {
                    showFavoriteToast((result.data && result.data.message) || "Couldn't update your favorites.", true);
                    return;
                }

                document.querySelectorAll('.js-favorite-toggle[data-product-id="' + productId + '"]').forEach(function (btn) {
                    btn.classList.toggle("active", result.data.isFavorite);
                    btn.setAttribute("aria-pressed", result.data.isFavorite ? "true" : "false");
                    var icon = btn.querySelector("i");
                    if (icon) {
                        icon.className = "bi " + (result.data.isFavorite ? "bi-heart-fill" : "bi-heart");
                    }
                });
                updateFavoriteCount(result.data.favoriteCount);
                showFavoriteToast(result.data.message, false);
            })
            .catch(function () {
                trigger.disabled = false;
                showFavoriteToast("Couldn't update your favorites. Please try again.", true);
            });
    });
})();

// Compare toggle — same AJAX + toast pattern as the favorite toggle above. Only rendered as a
// real button when signed in; guests get a plain login link instead.
(function () {
    function getToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    function updateCompareCount(count) {
        document.querySelectorAll(".compare-count").forEach(function (el) {
            el.textContent = count;
        });
    }

    function showCompareToast(message, isError) {
        var container = document.getElementById("quickAddToastContainer");
        if (!container) {
            container = document.createElement("div");
            container.id = "quickAddToastContainer";
            container.className = "toast-container position-fixed bottom-0 end-0 p-3";
            container.style.zIndex = 1080;
            document.body.appendChild(container);
        }

        var toast = document.createElement("div");
        toast.className = "toast align-items-center text-white " + (isError ? "bg-danger" : "bg-success") + " border-0";
        toast.setAttribute("role", "alert");
        var textDiv = document.createElement("div");
        textDiv.className = "d-flex";
        var body = document.createElement("div");
        body.className = "toast-body";
        body.textContent = message;
        var closeBtn = document.createElement("button");
        closeBtn.type = "button";
        closeBtn.className = "btn-close btn-close-white me-2 m-auto";
        closeBtn.setAttribute("data-bs-dismiss", "toast");
        textDiv.appendChild(body);
        textDiv.appendChild(closeBtn);
        toast.appendChild(textDiv);
        container.appendChild(toast);
        var bsToast = new bootstrap.Toast(toast, { delay: 3000 });
        toast.addEventListener("hidden.bs.toast", function () { toast.remove(); });
        bsToast.show();
    }

    document.addEventListener("click", function (e) {
        var trigger = e.target.closest(".js-compare-toggle");
        if (!trigger) {
            return;
        }
        e.preventDefault();

        var productId = trigger.getAttribute("data-product-id");
        var token = getToken();
        var body = new URLSearchParams();
        body.set("productId", productId);
        if (token) body.set("__RequestVerificationToken", token);

        trigger.disabled = true;
        fetch("/Compare/Toggle", {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: body
        })
            .then(function (resp) { return resp.json().then(function (data) { return { ok: resp.ok, data: data }; }); })
            .then(function (result) {
                trigger.disabled = false;
                if (!result.ok || !result.data.success) {
                    showCompareToast((result.data && result.data.message) || "Couldn't update your comparison list.", true);
                    return;
                }

                document.querySelectorAll('.js-compare-toggle[data-product-id="' + productId + '"]').forEach(function (btn) {
                    btn.classList.toggle("active", result.data.isInCompare);
                    btn.setAttribute("aria-pressed", result.data.isInCompare ? "true" : "false");
                });
                updateCompareCount(result.data.compareCount);
                showCompareToast(result.data.message, false);
            })
            .catch(function () {
                trigger.disabled = false;
                showCompareToast("Couldn't update your comparison list. Please try again.", true);
            });
    });
})();

// Review star-input — plain click handler (no AJAX; the review form is a normal POST), sets
// the hidden "rating" input and fills stars up to the clicked one, with a hover preview.
(function () {
    var wrap = document.getElementById("reviewStarInput");
    if (!wrap) return;

    var buttons = Array.prototype.slice.call(wrap.querySelectorAll(".pdp-star-input-btn"));
    var hiddenInput = document.getElementById("reviewRatingInput");

    function paint(value) {
        buttons.forEach(function (btn) {
            var isFilled = parseInt(btn.getAttribute("data-value"), 10) <= value;
            var icon = btn.querySelector("i");
            if (icon) {
                icon.className = "bi " + (isFilled ? "bi-star-fill" : "bi-star");
            }
        });
    }

    buttons.forEach(function (btn) {
        var value = parseInt(btn.getAttribute("data-value"), 10);
        btn.addEventListener("click", function () {
            hiddenInput.value = value;
            paint(value);
        });
        btn.addEventListener("mouseenter", function () {
            paint(value);
        });
    });

    wrap.addEventListener("mouseleave", function () {
        paint(parseInt(hiddenInput.value, 10) || 0);
    });
})();
