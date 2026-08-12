(function () {
    "use strict";

    var config = window.checkoutConfig;
    if (!config) {
        return;
    }

    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var wizardRoot = document.getElementById("checkout-wizard-root");
    var currentUserEmail = wizardRoot ? wizardRoot.dataset.userEmail : null;
    var stepper = document.getElementById("checkoutStepper");
    var panelShipping = document.getElementById("checkoutPanelShipping");
    var panelPayment = document.getElementById("checkoutPanelPayment");
    var panelReview = document.getElementById("checkoutPanelReview");

    var shippingContinueBtn = document.getElementById("shippingContinueBtn");
    var shippingError = document.getElementById("shippingError");

    var otpModalEl = document.getElementById("checkoutOtpModal");
    var otpModal = new bootstrap.Modal(otpModalEl);
    var otpEmailLabel = document.getElementById("otpEmailLabel");
    var otpCodeInput = document.getElementById("otpCodeInput");
    var otpError = document.getElementById("otpError");
    var otpVerifyBtn = document.getElementById("otpVerifyBtn");
    var otpResendBtn = document.getElementById("otpResendBtn");

    var paymentContinueBtn = document.getElementById("paymentContinueBtn");
    var paymentError = document.getElementById("paymentError");

    var reviewShippingRecap = document.getElementById("reviewShippingRecap");
    var reviewBackBtn = document.getElementById("reviewBackBtn");
    var reviewError = document.getElementById("reviewError");
    var placeOrderBtn = document.getElementById("placeOrderBtn");

    var shippingModel = null;
    var stripe = null;
    var elements = null;

    function postJson(url, body) {
        return fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "X-Requested-With": "XMLHttpRequest",
                "X-CSRF-TOKEN": tokenInput ? tokenInput.value : ""
            },
            body: JSON.stringify(body || {})
        }).then(function (resp) {
            return resp.json().then(function (data) { return { ok: resp.ok, data: data }; });
        });
    }

    function showStep(n) {
        panelShipping.classList.toggle("d-none", n !== 1);
        panelPayment.classList.toggle("d-none", n !== 2);
        panelReview.classList.toggle("d-none", n !== 3);

        stepper.querySelectorAll(".checkout-step").forEach(function (li) {
            var step = parseInt(li.getAttribute("data-step"), 10);
            li.classList.toggle("active", step === n);
            li.classList.toggle("completed", step < n);
        });
    }

    function setError(el, message) {
        if (message) {
            el.textContent = message;
            el.classList.add("visible");
        } else {
            el.textContent = "";
            el.classList.remove("visible");
        }
    }

    function collectShippingModel() {
        return {
            shippingName: document.getElementById("ShippingName").value,
            shippingAddress: document.getElementById("ShippingAddress").value,
            shippingCity: document.getElementById("ShippingCity").value,
            shippingState: document.getElementById("ShippingState").value,
            shippingZip: document.getElementById("ShippingZip").value,
            shippingPhone: document.getElementById("ShippingPhone").value
        };
    }

    function renderReviewRecap(model) {
        var parts = [model.shippingName, model.shippingAddress,
            [model.shippingCity, model.shippingState, model.shippingZip].filter(Boolean).join(", "),
            model.shippingPhone];
        reviewShippingRecap.innerHTML = parts.filter(Boolean).map(function (p) {
            return "<div>" + p.replace(/</g, "&lt;") + "</div>";
        }).join("");
    }

    function startResendCooldown(seconds) {
        var remaining = seconds;
        otpResendBtn.disabled = true;
        var label = otpResendBtn.textContent.replace(/\s*\(\d+s\)$/, "");
        var baseLabel = label;
        var timer = setInterval(function () {
            remaining--;
            if (remaining <= 0) {
                clearInterval(timer);
                otpResendBtn.disabled = false;
                otpResendBtn.textContent = baseLabel;
            } else {
                otpResendBtn.textContent = baseLabel + " (" + remaining + "s)";
            }
        }, 1000);
    }

    async function initStripePaymentElement() {
        setError(paymentError, null);
        var result = await postJson(config.createPaymentIntentUrl, {});
        if (!result.ok || !result.data.success) {
            setError(paymentError, (result.data && result.data.message) || "Couldn't start payment. Please try again.");
            return false;
        }

        stripe = Stripe(result.data.publishableKey || config.publishableKey);
        elements = stripe.elements({ clientSecret: result.data.clientSecret });

        // Explicitly pin the Payment Element to this logged-in account's email so Stripe Link
        // doesn't autofill a saved card from whatever email it last recognized on this browser —
        // that email can belong to a completely different site user. There's no separate Link
        // Authentication Element in use here — Link's email field is part of the unified Payment
        // Element's own UI — so it's suppressed via `fields.billingDetails.email: 'never'` rather
        // than left editable, since we already have the real email from the authenticated
        // session and don't want the customer able to switch it to someone else's mid-checkout.
        var paymentElementOptions = currentUserEmail
            ? { defaultValues: { billingDetails: { email: currentUserEmail } }, fields: { billingDetails: { email: "never" } } }
            : {};
        var paymentElement = elements.create("payment", paymentElementOptions);
        paymentElement.mount("#stripe-payment-element");
        return true;
    }

    shippingContinueBtn.addEventListener("click", function () {
        setError(shippingError, null);
        var model = collectShippingModel();

        shippingContinueBtn.disabled = true;
        var originalText = shippingContinueBtn.innerHTML;
        shippingContinueBtn.innerHTML = "…";

        postJson(config.sendOtpUrl, model).then(function (result) {
            shippingContinueBtn.disabled = false;
            shippingContinueBtn.innerHTML = originalText;

            if (!result.ok || !result.data.success) {
                setError(shippingError, (result.data && result.data.message) || "Couldn't send a verification code. Please check your details.");
                return;
            }

            shippingModel = model;
            renderReviewRecap(model);
            otpEmailLabel.textContent = result.data.email;
            otpCodeInput.value = "";
            setError(otpError, null);
            if (result.data.devOtpCode) {
                otpCodeInput.value = result.data.devOtpCode;
                otpCodeInput.placeholder = "DEV: " + result.data.devOtpCode;
            }
            otpModal.show();
            startResendCooldown(30);
        }).catch(function () {
            shippingContinueBtn.disabled = false;
            shippingContinueBtn.innerHTML = originalText;
            setError(shippingError, "Something went wrong. Please try again.");
        });
    });

    otpVerifyBtn.addEventListener("click", function () {
        setError(otpError, null);
        var code = otpCodeInput.value.trim();
        if (!code) {
            setError(otpError, "Enter the 6-digit code.");
            return;
        }

        otpVerifyBtn.disabled = true;
        postJson(config.verifyOtpUrl, { code: code }).then(async function (result) {
            otpVerifyBtn.disabled = false;

            if (!result.ok || !result.data.success) {
                setError(otpError, (result.data && result.data.message) || "That code isn't correct.");
                if (result.data && result.data.expired) {
                    otpModal.hide();
                }
                return;
            }

            otpModal.hide();
            showStep(2);
            var mounted = await initStripePaymentElement();
            if (!mounted) {
                showStep(1);
            }
        }).catch(function () {
            otpVerifyBtn.disabled = false;
            setError(otpError, "Something went wrong. Please try again.");
        });
    });

    otpResendBtn.addEventListener("click", function () {
        if (otpResendBtn.disabled) return;
        postJson(config.resendOtpUrl, {}).then(function (result) {
            if (result.ok && result.data.success) {
                startResendCooldown(30);
                if (result.data.devOtpCode) {
                    otpCodeInput.value = result.data.devOtpCode;
                    otpCodeInput.placeholder = "DEV: " + result.data.devOtpCode;
                }
            }
        });
    });

    paymentContinueBtn.addEventListener("click", function () {
        showStep(3);
    });

    reviewBackBtn.addEventListener("click", function () {
        showStep(2);
    });

    placeOrderBtn.addEventListener("click", async function () {
        setError(reviewError, null);
        placeOrderBtn.disabled = true;
        var originalText = placeOrderBtn.textContent;
        placeOrderBtn.textContent = "Placing Order…";

        try {
            // The Payment Element was created with fields.billingDetails.email: "never" (see
            // initStripePaymentElement) whenever currentUserEmail is known, which means Stripe
            // requires that same email to be supplied explicitly here — otherwise confirmPayment
            // rejects with an IntegrationError ("did not pass confirmParams.payment_method_data.
            // billing_details.email") and every logged-in checkout fails.
            var confirmParams = currentUserEmail
                ? { payment_method_data: { billing_details: { email: currentUserEmail } } }
                : undefined;
            var confirmResult = await stripe.confirmPayment({
                elements: elements,
                redirect: "if_required",
                confirmParams: confirmParams
            });

            if (confirmResult.error) {
                setError(reviewError, confirmResult.error.message || "Your payment could not be processed.");
                placeOrderBtn.disabled = false;
                placeOrderBtn.textContent = originalText;
                return;
            }

            if (!confirmResult.paymentIntent || confirmResult.paymentIntent.status !== "succeeded") {
                setError(reviewError, "Your payment could not be confirmed. Please try again.");
                placeOrderBtn.disabled = false;
                placeOrderBtn.textContent = originalText;
                return;
            }

            var result = await postJson(config.confirmOrderUrl, { paymentIntentId: confirmResult.paymentIntent.id });
            if (!result.ok || !result.data.success) {
                setError(reviewError, (result.data && result.data.message) || "We couldn't confirm your order. Please contact support.");
                placeOrderBtn.disabled = false;
                placeOrderBtn.textContent = originalText;
                return;
            }

            window.location.href = result.data.redirectUrl;
        } catch (e) {
            setError(reviewError, "Something went wrong. Please try again.");
            placeOrderBtn.disabled = false;
            placeOrderBtn.textContent = originalText;
        }
    });
})();
