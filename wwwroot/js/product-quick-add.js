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
    var priceDisplayEl = document.getElementById("quickAddPriceDisplay");
    var errorEl = document.getElementById("quickAddError");
    var submitBtn = document.getElementById("quickAddSubmitBtn");
    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');

    var qtyValueEl = document.getElementById("quickAddQtyValue");
    var qtyMinusBtn = document.getElementById("quickAddQtyMinus");
    var qtyPlusBtn = document.getElementById("quickAddQtyPlus");

    var selectPromptText = document.getElementById("quickAddSelectPromptText").textContent;
    var unavailableText = document.getElementById("quickAddUnavailableText").textContent;
    var outOfStockText = document.getElementById("quickAddOutOfStockText").textContent;

    var currentProduct = null;
    var selectedColor = null;
    var selectedSize = null;
    var selectedQty = 1;

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

    function findVariant(colorName, sizeLabel) {
        if (!currentProduct.variants) {
            return null;
        }
        var hasColors = currentProduct.colors && currentProduct.colors.length > 0;
        var hasSizes = currentProduct.sizes && currentProduct.sizes.length > 0;

        return currentProduct.variants.find(function (v) {
            var colorMatch = !hasColors || v.color === colorName;
            var sizeMatch = !hasSizes || v.size === sizeLabel;
            return colorMatch && sizeMatch;
        }) || null;
    }

    function formatTotal(unitPrice, qty) {
        // unitPrice comes pre-formatted from the server (e.g. "AED 30.48"), so we pull the
        // numeric part out, multiply, and reuse the same currency prefix/suffix.
        var match = unitPrice.match(/^([^\d]*)([\d.,]+)([^\d]*)$/);
        if (!match) {
            return unitPrice;
        }
        var prefix = match[1];
        var numeric = parseFloat(match[2].replace(/,/g, ""));
        var suffix = match[3];
        var total = (numeric * qty).toFixed(2);
        return prefix + total + suffix;
    }

    function clampQtyToStock(variant) {
        if (!variant) {
            return;
        }
        if (selectedQty > variant.stock) {
            selectedQty = variant.stock > 0 ? variant.stock : 1;
        }
        if (selectedQty < 1) {
            selectedQty = 1;
        }
        qtyValueEl.textContent = selectedQty;
    }

    function updatePriceAndAvailability() {
        var hasColors = currentProduct.colors && currentProduct.colors.length > 0;
        var hasSizes = currentProduct.sizes && currentProduct.sizes.length > 0;

        if ((hasColors && !selectedColor) || (hasSizes && !selectedSize)) {
            priceDisplayEl.innerHTML = "";
            errorEl.textContent = selectPromptText;
            errorEl.classList.add("visible");
            submitBtn.disabled = true;
            return;
        }

        var variant = findVariant(selectedColor, selectedSize);

        if (!variant || variant.active === false) {
            priceDisplayEl.innerHTML = "";
            errorEl.textContent = unavailableText;
            errorEl.classList.add("visible");
            submitBtn.disabled = true;
            return;
        }

        if (variant.stock <= 0) {
            priceDisplayEl.innerHTML = "";
            errorEl.textContent = outOfStockText;
            errorEl.classList.add("visible");
            submitBtn.disabled = true;
            return;
        }

        clampQtyToStock(variant);

        errorEl.classList.remove("visible");
        submitBtn.disabled = false;

        if (variant.listPriceFormatted) {
            priceDisplayEl.innerHTML =
                '<span class="price-was">' + escapeHtml(formatTotal(variant.listPriceFormatted, selectedQty)) + '</span> ' +
                '<span class="price price-sale">' + escapeHtml(formatTotal(variant.priceFormatted, selectedQty)) + '</span>';
        } else {
            priceDisplayEl.innerHTML = '<span class="price">' + escapeHtml(formatTotal(variant.priceFormatted, selectedQty)) + '</span>';
        }
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
            label.className = "color-swatch";
            label.style.cssText = style;
            label.title = color.name;
            label.innerHTML = '<input type="radio" name="quickAddColor" value="' + escapeHtml(color.name) + '" ' + (index === 0 ? "checked" : "") + ' />';
            label.addEventListener("click", function () {
                selectedColor = color.name;
                colorLabelEl.textContent = color.name;
                updatePriceAndAvailability();
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
                selectedSize = size.label;
            }

            var label = document.createElement("label");
            label.className = "size-pill";
            label.innerHTML = '<input type="radio" name="quickAddSize" value="' + escapeHtml(size.label) + '" ' + (index === 0 ? "checked" : "") + ' /><span>' + escapeHtml(size.label) + '</span>';
            label.addEventListener("click", function () {
                selectedSize = size.label;
                updatePriceAndAvailability();
            });
            sizeOptionsEl.appendChild(label);
        });
    }

    function addToCart(productId, color, size, qty, onDone) {
        var body = new URLSearchParams();
        body.set("productId", productId);
        body.set("quantity", String(qty));
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

    qtyMinusBtn.addEventListener("click", function () {
        if (selectedQty > 1) {
            selectedQty--;
            qtyValueEl.textContent = selectedQty;
            updatePriceAndAvailability();
        }
    });

    qtyPlusBtn.addEventListener("click", function () {
        var variant = findVariant(selectedColor, selectedSize);
        if (!variant) {
            return;
        }
        if (selectedQty < variant.stock) {
            selectedQty++;
            qtyValueEl.textContent = selectedQty;
            updatePriceAndAvailability();
        }
    });

    document.addEventListener("click", function (e) {
        var trigger = e.target.closest(".js-quick-add");
        if (!trigger) {
            return;
        }
        e.preventDefault();

        currentProduct = JSON.parse(trigger.getAttribute("data-product"));
        selectedColor = null;
        selectedSize = null;
        selectedQty = 1;
        qtyValueEl.textContent = 1;
        errorEl.classList.remove("visible");
        priceDisplayEl.innerHTML = "";

        var hasColors = currentProduct.colors && currentProduct.colors.length > 0;
        var hasSizes = currentProduct.sizes && currentProduct.sizes.length > 0;

        if (!hasColors && !hasSizes) {
            trigger.disabled = true;
            addToCart(currentProduct.id, null, null, 1, function () { trigger.disabled = false; });
            return;
        }

        productNameEl.textContent = currentProduct.name;
        renderColorOptions();
        renderSizeOptions();
        updatePriceAndAvailability();
        bsModal.show();
    });

    submitBtn.addEventListener("click", function () {
        var variant = findVariant(selectedColor, selectedSize);
        if (!variant || variant.stock <= 0) {
            return;
        }

        submitBtn.disabled = true;
        submitBtn.textContent = "Adding…";
        addToCart(currentProduct.id, selectedColor, selectedSize, selectedQty, function () {
            submitBtn.disabled = false;
            submitBtn.textContent = "Add To Basket";
            bsModal.hide();
        });
    });

    document.addEventListener("click", function (e) {
        var trigger = e.target.closest(".js-inline-add");
        if (!trigger) {
            return;
        }

        var productId = trigger.getAttribute("data-product-id");
        var originalText = trigger.textContent;
        trigger.disabled = true;
        trigger.textContent = "Adding…";
        addToCart(productId, null, null, 1, function () {
            trigger.disabled = false;
            trigger.textContent = originalText;
        });
    });
})();
