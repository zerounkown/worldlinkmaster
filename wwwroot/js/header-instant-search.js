(function () {
    "use strict";

    var DEBOUNCE_MS = 300;
    var MIN_CHARS = 2;

    // Both the desktop .header-search-inline bar and the mobile/tablet .header-search-form
    // panel (Views/Shared/_Layout.cshtml) share this class on their <input>, each with its own
    // sibling .instant-search-dropdown — only one is ever visible at a time (CSS breakpoint),
    // but wiring both independently means this file doesn't need to know which is showing.
    var inputs = document.querySelectorAll(".js-instant-search-input");
    if (inputs.length === 0) {
        return;
    }

    inputs.forEach(function (input) {
        var dropdown = input.parentElement ? input.parentElement.querySelector(".instant-search-dropdown") : null;
        if (!dropdown) {
            return;
        }

        var noResultsText = dropdown.getAttribute("data-no-results-text") || "No results";
        var searchForText = dropdown.getAttribute("data-search-for-text") || "Search for";

        var debounceTimer = null;
        var requestSeq = 0;
        // -1 = nothing highlighted (plain Enter falls through to the form's native submit, which
        // goes to the same full-results page as the "Search for" link); 0..n-1 = a result/link.
        var activeIndex = -1;

        function closeDropdown() {
            dropdown.classList.remove("open");
            dropdown.textContent = "";
            activeIndex = -1;
        }

        function getNavItems() {
            return Array.prototype.slice.call(dropdown.querySelectorAll(".instant-search-item, .instant-search-view-all"));
        }

        function setActive(index) {
            var items = getNavItems();
            items.forEach(function (el) { el.classList.remove("active"); });
            activeIndex = index;
            if (index >= 0 && index < items.length) {
                items[index].classList.add("active");
                items[index].scrollIntoView({ block: "nearest" });
            }
        }

        function renderResults(data, term) {
            dropdown.textContent = "";
            activeIndex = -1;

            var results = data.results || [];
            if (results.length === 0) {
                var empty = document.createElement("div");
                empty.className = "instant-search-empty";
                empty.textContent = noResultsText;
                dropdown.appendChild(empty);
            } else {
                results.forEach(function (item) {
                    var link = document.createElement("a");
                    link.className = "instant-search-item";
                    link.href = item.url;

                    var img = document.createElement("img");
                    img.className = "instant-search-item-image";
                    img.src = item.image;
                    img.alt = "";
                    link.appendChild(img);

                    var info = document.createElement("div");
                    info.className = "instant-search-item-info";

                    var name = document.createElement("span");
                    name.className = "instant-search-item-name";
                    name.textContent = item.name;
                    info.appendChild(name);

                    var price = document.createElement("span");
                    price.className = "instant-search-item-price";
                    price.textContent = item.price;
                    info.appendChild(price);

                    link.appendChild(info);
                    dropdown.appendChild(link);
                });
            }

            var viewAll = document.createElement("a");
            viewAll.className = "instant-search-view-all";
            viewAll.href = data.searchUrl || ("/Products?search=" + encodeURIComponent(term));
            viewAll.textContent = searchForText + ' "' + term + '"';
            dropdown.appendChild(viewAll);

            dropdown.classList.add("open");
        }

        function runSearch(term) {
            var seq = ++requestSeq;
            fetch("/Products/InstantSearch?q=" + encodeURIComponent(term), {
                headers: { "X-Requested-With": "XMLHttpRequest" }
            })
                .then(function (resp) { return resp.ok ? resp.json() : null; })
                .then(function (data) {
                    // A later keystroke may have started a newer request while this one was in
                    // flight — drop this response rather than let it clobber fresher results.
                    if (seq !== requestSeq || !data) {
                        return;
                    }
                    renderResults(data, term);
                })
                .catch(function () {
                    // Silent — the input is still a normal form field, so Enter/submit still
                    // reaches the full results page even if the dropdown itself failed to load.
                });
        }

        input.addEventListener("input", function () {
            var term = input.value.trim();
            window.clearTimeout(debounceTimer);
            requestSeq++; // Invalidate any in-flight request immediately, not just future ones.

            if (term.length < MIN_CHARS) {
                closeDropdown();
                return;
            }

            debounceTimer = window.setTimeout(function () {
                runSearch(term);
            }, DEBOUNCE_MS);
        });

        input.addEventListener("keydown", function (e) {
            var items = getNavItems();
            if (!dropdown.classList.contains("open") || items.length === 0) {
                return;
            }

            if (e.key === "ArrowDown") {
                e.preventDefault();
                setActive(activeIndex + 1 >= items.length ? 0 : activeIndex + 1);
            } else if (e.key === "ArrowUp") {
                e.preventDefault();
                setActive(activeIndex - 1 < 0 ? items.length - 1 : activeIndex - 1);
            } else if (e.key === "Enter") {
                if (activeIndex >= 0 && activeIndex < items.length) {
                    e.preventDefault();
                    window.location.href = items[activeIndex].getAttribute("href");
                }
                // Nothing highlighted: don't preventDefault — the form submits natively to the
                // same full-results page the "Search for" link points to.
            } else if (e.key === "Escape") {
                closeDropdown();
            }
        });

        // Root cause of "clicking a result does nothing": mousedown on a result shifts focus
        // away from the (still-focused) input before the click event fires — on mobile that also
        // closes the on-screen keyboard, which resizes the viewport and can shift the dropdown
        // out from under the pointer between mousedown and mouseup, so the browser never fires
        // click on it at all (click requires mouseup to land on/in the same element mousedown
        // did). preventDefault on pointerdown/mousedown stops the browser's default
        // focus-shift/blur behavior for that press without blocking the subsequent click (or
        // therefore the <a>'s own navigation) — the standard fix for this class of bug in
        // dropdown/autocomplete widgets. Covers mouse AND touch: pointerdown fires for both in
        // every browser this site supports, with mousedown kept as a fallback for the rare
        // browser without Pointer Events.
        function guardAgainstBlur(e) {
            if (e.target.closest(".instant-search-item, .instant-search-view-all")) {
                e.preventDefault();
            }
        }
        dropdown.addEventListener("pointerdown", guardAgainstBlur);
        dropdown.addEventListener("mousedown", guardAgainstBlur);

        document.addEventListener("click", function (e) {
            if (!input.contains(e.target) && !dropdown.contains(e.target)) {
                closeDropdown();
            }
        });
    });
})();
