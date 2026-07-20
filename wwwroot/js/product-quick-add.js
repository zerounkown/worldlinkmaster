// Quick-add-to-cart modal, triggered from product cards anywhere on the site (grid, home
// page, "You may also like"). Products with no color/size variants skip the modal entirely
// and add straight to cart.
(function () {
    "use strict";

    var modalEl = document.getElementById("quickAddModal");
    if (!modalEl) {
        return;
    }

    var bsModal = new bootstrap.Modal(modalEl);
    var productNameEl = document.getElementById("quickAddProductName");
    var colorSection = document.getElementById("quickAddColorSection");
    var colorOptionsEl = document.getElementById("quickAddColorOptions");
    var colorLabelEl = document.getElementById("quickAddColorLabel");
    var sizeSection = document.getElementById("quickAddSizeSection");
    var sizeOptionsEl = document.getElementById("quickAddSizeOptions");
    var errorEl = document.getElementById("quickAddError");
    var submitBtn = document.getElementById("quickAddSubmitBtn");
    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');

    var currentProduct = null;
    var selectedColor = null;
    var selectedSize = null;

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.textContent = text;
        return div.innerHTML;
    }

    function showToast(message, isError) {
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
        toast.innerHTML =
            '<div class="d-flex">' +
            '<div class="toast-body">' + escapeHtml(message) + '</div>' +
            '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>' +
            '</div>';
        container.appendChild(toast);
        var bsToast = new bootstrap.Toast(toast, { delay: 3500 });
        toast.addEventListener("hidden.bs.toast", function () { toast.remove(); });
        bsToast.show();
    }

    function updateCartCount(count) {
        document.querySelectorAll(".cart-count").forEach(function (el) {
            el.textContent = count;
        });
    }

    function renderColorOptions() {
        colorOptionsEl.innerHTML = "";
        if (!currentProduct.colors || currentProduct.colors.length === 0) {
            colorSection.style.display = "none";
            selectedColor = null;
            return;
        }

        colorSection.style.display = "";
        currentProduct.colors.forEach(function (color, index) {
            if (index === 0) {
                selectedColor = color.name;
                colorLabelEl.textContent = color.name;
            }

            var style = "background-color: " + color.hex + ";";
            if (color.image) {
                style += " background-image: url('" + color.image + "');";
            }

            var label = document.createElement("label");
            label.className = "color-swatch" + (index === 0 ? "" : "");
            label.style.cssText = style;
            label.title = color.name;
            label.innerHTML = '<input type="radio" name="quickAddColor" value="' + escapeHtml(color.name) + '" ' + (index === 0 ? "checked" : "") + ' />';
            label.addEventListener("click", function () {
                selectedColor = color.name;
                colorLabelEl.textContent = color.name;
            });
            colorOptionsEl.appendChild(label);
        });
    }

    function renderSizeOptions() {
        sizeOptionsEl.innerHTML = "";
        if (!currentProduct.sizes || currentProduct.sizes.length === 0) {
            sizeSection.style.display = "none";
            selectedSize = null;
            return;
        }

        sizeSection.style.display = "";
        currentProduct.sizes.forEach(function (size, index) {
            if (index === 0) {
                selectedSize = size;
            }

            var label = document.createElement("label");
            label.className = "size-pill";
            label.innerHTML = '<input type="radio" name="quickAddSize" value="' + escapeHtml(size) + '" ' + (index === 0 ? "checked" : "") + ' /><span>' + escapeHtml(size) + '</span>';
            label.addEventListener("click", function () {
                selectedSize = size;
            });
            sizeOptionsEl.appendChild(label);
        });
    }

    function addToCart(productId, color, size, onDone) {
        var body = new URLSearchParams();
        body.set("productId", productId);
        body.set("quantity", "1");
        if (color) body.set("color", color);
        if (size) body.set("size", size);
        if (tokenInput) body.set("__RequestVerificationToken", tokenInput.value);

        fetch("/Cart/Add", {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: body
        })
            .then(function (resp) {
                return resp.json().then(function (data) { return { ok: resp.ok, data: data }; });
            })
            .then(function (result) {
                if (result.ok && result.data.success) {
                    updateCartCount(result.data.cartCount);
                    showToast(result.data.message, false);
                } else {
                    showToast((result.data && result.data.message) || "Couldn't add that to your cart.", true);
                }
                if (onDone) onDone();
            })
            .catch(function () {
                showToast("Couldn't add that to your cart. Please try again.", true);
                if (onDone) onDone();
            });
    }

    document.addEventListener("click", function (e) {
        var trigger = e.target.closest(".js-quick-add");
        if (!trigger) {
            return;
        }
        e.preventDefault();

        currentProduct = JSON.parse(trigger.getAttribute("data-product"));
        selectedColor = null;
        selectedSize = null;
        errorEl.classList.remove("visible");

        var hasColors = currentProduct.colors && currentProduct.colors.length > 0;
        var hasSizes = currentProduct.sizes && currentProduct.sizes.length > 0;

        if (!hasColors && !hasSizes) {
            // Nothing to choose — add it straight away, no modal needed.
            trigger.disabled = true;
            addToCart(currentProduct.id, null, null, function () { trigger.disabled = false; });
            return;
        }

        productNameEl.textContent = currentProduct.name;
        renderColorOptions();
        renderSizeOptions();
        bsModal.show();
    });

    submitBtn.addEventListener("click", function () {
        var hasColors = currentProduct.colors && currentProduct.colors.length > 0;
        var hasSizes = currentProduct.sizes && currentProduct.sizes.length > 0;

        if ((hasColors && !selectedColor) || (hasSizes && !selectedSize)) {
            errorEl.classList.add("visible");
            return;
        }
        errorEl.classList.remove("visible");

        submitBtn.disabled = true;
        submitBtn.textContent = "Adding…";
        addToCart(currentProduct.id, selectedColor, selectedSize, function () {
            submitBtn.disabled = false;
            submitBtn.textContent = "Add To Basket";
            bsModal.hide();
        });
    });

    // Product card "Add To Cart" button — reads the card's own variant <select> (if the
    // product has colors/sizes) rather than opening the modal, matching the on-card
    // dropdown pattern used across the shop grid.
    document.addEventListener("click", function (e) {
        var trigger = e.target.closest(".js-inline-add");
        if (!trigger) {
            return;
        }

        var productId = trigger.getAttribute("data-product-id");
        var card = trigger.closest(".product-card");
        var variantSelect = card ? card.querySelector(".product-card-variant") : null;
        var color = null;
        var size = null;

        if (variantSelect && variantSelect.value) {
            var parts = variantSelect.value.split("|");
            color = parts[0] || null;
            size = parts[1] || null;
        }

        var originalText = trigger.textContent;
        trigger.disabled = true;
        trigger.textContent = "Adding…";
        addToCart(productId, color, size, function () {
            trigger.disabled = false;
            trigger.textContent = originalText;
        });
    });
})();
