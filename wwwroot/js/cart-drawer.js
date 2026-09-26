// Side cart drawer: opened after a successful AJAX Add to Cart on the product detail page (see
// the PDP-specific block at the bottom of this file). The drawer's open/close mechanics mirror
// the .side-nav pattern in site.js (present on every page since the shell lives in _Layout.cshtml),
// while its content is always freshly fetched from Cart/Drawer -- there's no reason to keep a
// second, hand-rolled copy of the cart's totals/stock logic in JS when the server already has it.
(function () {
    "use strict";

    var drawer = document.getElementById("cartDrawer");
    var overlay = document.getElementById("cartDrawerOverlay");
    var closeBtn = document.getElementById("cartDrawerClose");
    var body = document.getElementById("cartDrawerBody");

    if (!drawer || !overlay || !body) {
        return;
    }

    function getToken() {
        var input = drawer.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : "";
    }

    function updateCartCount(count) {
        document.querySelectorAll(".cart-count").forEach(function (el) {
            el.textContent = count;
        });
    }

    function openDrawer() {
        drawer.classList.add("open");
        overlay.classList.add("open");
        drawer.setAttribute("aria-hidden", "false");
        document.body.style.overflow = "hidden";
    }

    function closeDrawer() {
        drawer.classList.remove("open");
        overlay.classList.remove("open");
        drawer.setAttribute("aria-hidden", "true");
        document.body.style.overflow = "";
    }

    overlay.addEventListener("click", closeDrawer);
    if (closeBtn) {
        closeBtn.addEventListener("click", closeDrawer);
    }
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && drawer.classList.contains("open")) {
            closeDrawer();
        }
    });

    // One in-flight request at a time -- a second click while the drawer is mid-refresh would
    // otherwise race the DOM rebuild wireDrawerRows() just did.
    var drawerBusy = false;

    function refreshDrawerBody() {
        var scrollTop = body.scrollTop;
        return fetch("/Cart/Drawer", { credentials: "same-origin" })
            .then(function (resp) { return resp.ok ? resp.text() : null; })
            .then(function (html) {
                if (html != null) {
                    body.innerHTML = html;
                    body.scrollTop = scrollTop;
                    wireDrawerRows();
                }
            });
    }

    function postAjax(url, params) {
        params.set("__RequestVerificationToken", getToken());
        return fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: params
        }).then(function (resp) { return resp.json(); });
    }

    function wireDrawerRows() {
        body.querySelectorAll(".cart-drawer-row").forEach(function (row) {
            var qtyEl = row.querySelector(".cart-qty-value");
            var minusBtn = row.querySelector(".cart-qty-minus");
            var plusBtn = row.querySelector(".cart-qty-plus");
            var removeBtn = row.querySelector(".cart-drawer-row-remove");
            var stock = parseInt(row.getAttribute("data-stock"), 10) || 0;

            function lineParams() {
                var params = new URLSearchParams();
                params.set("productId", row.getAttribute("data-product-id"));
                params.set("color", row.getAttribute("data-color") || "");
                params.set("size", row.getAttribute("data-size") || "");
                return params;
            }

            function afterChange(promise) {
                drawerBusy = true;
                promise
                    .then(function (data) {
                        if (data && data.success) {
                            updateCartCount(data.itemCount);
                        }
                        return refreshDrawerBody();
                    })
                    .catch(function () { })
                    .finally(function () { drawerBusy = false; });
            }

            if (minusBtn && qtyEl) {
                minusBtn.addEventListener("click", function () {
                    if (drawerBusy) return;
                    var qty = parseInt(qtyEl.textContent, 10) || 1;
                    if (qty <= 1) return;
                    var params = lineParams();
                    params.set("quantity", qty - 1);
                    afterChange(postAjax("/Cart/UpdateQuantityAjax", params));
                });
            }

            if (plusBtn && qtyEl) {
                plusBtn.addEventListener("click", function () {
                    if (drawerBusy) return;
                    var qty = parseInt(qtyEl.textContent, 10) || 1;
                    if (stock > 0 && qty >= stock) return;
                    var params = lineParams();
                    params.set("quantity", qty + 1);
                    afterChange(postAjax("/Cart/UpdateQuantityAjax", params));
                });
            }

            if (removeBtn) {
                removeBtn.addEventListener("click", function () {
                    if (drawerBusy) return;
                    afterChange(postAjax("/Cart/Remove", lineParams()));
                });
            }
        });
    }

    // --- Product detail page: intercept "Add to Cart" only, leave "Buy Now" untouched ---
    var addToCartForm = document.getElementById("pdpAddToCartForm");
    if (addToCartForm) {
        var addToCartBtn = document.getElementById("pdpAddToCartBtn");
        var errorEl = document.getElementById("pdpCartError");

        if (addToCartBtn) {
            addToCartBtn.addEventListener("click", function (e) {
                e.preventDefault();
                if (errorEl) {
                    errorEl.textContent = "";
                    errorEl.classList.remove("visible");
                }

                var formData = new FormData(addToCartForm);
                fetch(addToCartForm.getAttribute("action") || "/Cart/Add", {
                    method: "POST",
                    credentials: "same-origin",
                    headers: { "X-Requested-With": "XMLHttpRequest" },
                    body: formData
                })
                    .then(function (resp) {
                        return resp.json().then(function (data) { return { ok: resp.ok, data: data }; });
                    })
                    .then(function (result) {
                        if (!result.ok || !result.data || !result.data.success) {
                            var message = (result.data && result.data.message) || (errorEl && errorEl.getAttribute("data-generic-error")) || "";
                            if (errorEl && message) {
                                errorEl.textContent = message;
                                errorEl.classList.add("visible");
                            }
                            return;
                        }
                        updateCartCount(result.data.cartCount);
                        refreshDrawerBody().then(openDrawer);
                    })
                    .catch(function () {
                        if (errorEl) {
                            errorEl.textContent = errorEl.getAttribute("data-generic-error") || "";
                            errorEl.classList.add("visible");
                        }
                    });
            });
        }
    }
})();
