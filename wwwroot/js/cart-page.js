(function () {
    "use strict";

    var rows = document.querySelectorAll(".cart-line-row");
    if (rows.length === 0) {
        return;
    }

    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var updateUrl = "/Cart/UpdateQuantityAjax";

    var freeShippingBar = document.getElementById("freeShippingBar");
    var freeShippingBarFill = document.getElementById("freeShippingBarFill");
    var freeShippingBarStatus = document.getElementById("freeShippingBarStatus");
    var freeShippingThreshold = freeShippingBar ? parseFloat(freeShippingBar.getAttribute("data-threshold")) : 0;
    var freeShippingSymbol = freeShippingBar ? freeShippingBar.getAttribute("data-currency-symbol") : "";

    var subtotalEl = document.getElementById("cartSubtotal");
    var shippingEl = document.getElementById("cartShippingCost");
    var totalEl = document.getElementById("cartTotal");
    var couponDiscountLine = document.getElementById("cartCouponDiscountLine");
    var couponDiscountAmountEl = document.getElementById("cartCouponDiscountAmount");
    var youSavedEl = document.getElementById("cartYouSaved");
    var youSavedAmountEl = document.getElementById("cartYouSavedAmount");

    function formatMoney(symbol, amount) {
        return symbol + amount.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function updateCartCount(count) {
        document.querySelectorAll(".cart-count").forEach(function (el) {
            el.textContent = count;
        });
    }

    function applyServerTotals(data) {
        if (subtotalEl) subtotalEl.textContent = formatMoney(subtotalEl.getAttribute("data-currency-symbol") || "", data.subtotal);
        if (totalEl) totalEl.textContent = formatMoney(totalEl.getAttribute("data-currency-symbol") || "", data.total);

        if (shippingEl) {
            if (data.shippingCost === 0) {
                shippingEl.textContent = shippingEl.getAttribute("data-free-label") || "FREE";
                shippingEl.classList.add("text-success", "fw-bold");
            } else {
                shippingEl.textContent = formatMoney(shippingEl.getAttribute("data-currency-symbol") || "", data.shippingCost);
                shippingEl.classList.remove("text-success", "fw-bold");
            }
        }

        if (couponDiscountAmountEl) {
            couponDiscountAmountEl.textContent = formatMoney(couponDiscountAmountEl.getAttribute("data-currency-symbol") || "", data.couponDiscountAmount);
        }
        if (couponDiscountLine) {
            couponDiscountLine.style.display = data.couponDiscountAmount > 0 ? "" : "none";
        }
        if (youSavedEl && youSavedAmountEl) {
            youSavedEl.style.display = data.couponDiscountAmount > 0 ? "" : "none";
            youSavedAmountEl.textContent = formatMoney(youSavedAmountEl.getAttribute("data-currency-symbol") || "", data.couponDiscountAmount);
        }

        if (freeShippingBarFill) {
            var pct = freeShippingThreshold > 0 ? Math.min(100, (data.subtotal / freeShippingThreshold) * 100) : 0;
            freeShippingBarFill.style.width = pct + "%";
        }
        if (freeShippingBarStatus) {
            if (data.qualifiesForFreeShipping) {
                freeShippingBarStatus.classList.add("free-shipping-qualified");
                freeShippingBarStatus.innerHTML = '<i class="bi bi-check-circle-fill"></i> ' + (freeShippingBarStatus.getAttribute("data-qualified-text") || "You are eligible for free shipping!");
            } else {
                freeShippingBarStatus.classList.remove("free-shipping-qualified");
                var awayText = (freeShippingBarStatus.getAttribute("data-away-text") || "{0} away from free shipping").replace("{0}", formatMoney(freeShippingSymbol, data.amountAwayFromFreeShipping));
                freeShippingBarStatus.textContent = awayText;
            }
        }

        updateCartCount(data.itemCount);
    }

    function sendUpdate(row) {
        var body = new URLSearchParams();
        body.set("productId", row.getAttribute("data-product-id"));
        body.set("color", row.getAttribute("data-color") || "");
        body.set("size", row.getAttribute("data-size") || "");
        body.set("quantity", row.querySelector(".cart-qty-value").textContent);
        if (tokenInput) body.set("__RequestVerificationToken", tokenInput.value);

        fetch(updateUrl, {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: body
        })
            .then(function (resp) { return resp.json(); })
            .then(function (data) {
                if (data && data.success) {
                    applyServerTotals(data);
                }
            })
            .catch(function () { /* leave the optimistic client-side values in place */ });
    }

    rows.forEach(function (row) {
        var qtyValueEl = row.querySelector(".cart-qty-value");
        var minusBtn = row.querySelector(".cart-qty-minus");
        var plusBtn = row.querySelector(".cart-qty-plus");
        var lineTotalEl = row.querySelector(".cart-line-total");
        var unitPrice = parseFloat(row.getAttribute("data-unit-price"));
        var stock = parseInt(row.getAttribute("data-stock"), 10) || 0;

        function renderLineTotal() {
            var qty = parseInt(qtyValueEl.textContent, 10) || 1;
            var total = unitPrice * qty;
            lineTotalEl.textContent = formatMoney(lineTotalEl.getAttribute("data-currency-symbol") || "", total);
        }

        minusBtn.addEventListener("click", function () {
            var qty = parseInt(qtyValueEl.textContent, 10) || 1;
            if (qty <= 1) return;
            qtyValueEl.textContent = qty - 1;
            renderLineTotal();
            sendUpdate(row);
        });

        plusBtn.addEventListener("click", function () {
            var qty = parseInt(qtyValueEl.textContent, 10) || 1;
            if (stock > 0 && qty >= stock) return;
            qtyValueEl.textContent = qty + 1;
            renderLineTotal();
            sendUpdate(row);
        });
    });
})();
