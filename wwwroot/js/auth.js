// Adds a show/hide toggle to every password field on the Identity auth pages
// (Login, Register, Reset Password, Change Password, etc.). Works with the compiled
// Identity UI without touching its markup or the authentication logic.
(function () {
    "use strict";

    function addToggle(input) {
        var container = input.closest(".form-floating") || input.parentElement;
        if (!container || container.querySelector(".password-toggle")) {
            return; // already wired up
        }

        container.classList.add("has-password-toggle");

        var button = document.createElement("button");
        button.type = "button"; // never submit the form
        button.className = "password-toggle";
        button.setAttribute("aria-label", "Show password");
        button.setAttribute("title", "Show password");
        button.innerHTML = '<i class="bi bi-eye"></i>';

        button.addEventListener("click", function () {
            var reveal = input.type === "password";
            input.type = reveal ? "text" : "password";
            button.querySelector("i").className = reveal ? "bi bi-eye-slash" : "bi bi-eye";
            var label = reveal ? "Hide password" : "Show password";
            button.setAttribute("aria-label", label);
            button.setAttribute("title", label);
        });

        container.appendChild(button);
    }

    function init() {
        var inputs = document.querySelectorAll('.auth-page input[type="password"]');
        for (var i = 0; i < inputs.length; i++) {
            addToggle(inputs[i]);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
