// Color swatches on product cards (category/search grids, homepage carousels, related
// products, favorites — everywhere .product-card or .featured-product-card renders).
// Desktop: hovering a swatch swaps the card image to that color's photo; moving off the whole
// card (not just the swatch) reverts to the default image. Mobile: tapping a swatch swaps the
// image and "arms" that color; tapping the image or name afterward opens the product with that
// color preselected via ?color=Name. Delegated at the document level (mirrors
// product-quick-add.js's own pattern) so it keeps working for cards rendered after an
// AJAX-refreshed grid, not just ones present at page load.
(function () {
    function cardOf(el) {
        return el.closest(".product-card, .featured-product-card");
    }

    function withColor(href, colorName) {
        if (!href) return href;
        var sep = href.indexOf("?") >= 0 ? "&" : "?";
        return href + sep + "color=" + encodeURIComponent(colorName);
    }

    // The base (no-color) href is read fresh from a data attribute set the first time a card is
    // touched, not by stripping ?color= back out of the live href — simpler and avoids any
    // chance of accumulating stale query params across repeated hovers.
    function baseHref(link) {
        var stored = link.getAttribute("data-base-href");
        if (stored === null) {
            stored = link.getAttribute("href") || "";
            link.setAttribute("data-base-href", stored);
        }
        return stored;
    }

    function setActiveSwatch(card, swatch) {
        card.querySelectorAll(".product-card-swatch").forEach(function (s) {
            s.classList.toggle("active", s === swatch);
        });
    }

    function applyColor(card, swatch) {
        var img = card.querySelector(".card-product-image:not(.card-product-image-hover)");
        var imageUrl = swatch.getAttribute("data-image");
        if (img && imageUrl) {
            img.src = imageUrl;
        }
        card.querySelectorAll(".product-card-link").forEach(function (link) {
            link.setAttribute("href", withColor(baseHref(link), swatch.getAttribute("data-color")));
        });
        setActiveSwatch(card, swatch);
    }

    function resetColor(card) {
        var img = card.querySelector(".card-product-image:not(.card-product-image-hover)");
        if (img) {
            var defaultImage = img.getAttribute("data-default-image");
            if (defaultImage) img.src = defaultImage;
        }
        card.querySelectorAll(".product-card-link").forEach(function (link) {
            link.setAttribute("href", baseHref(link));
        });
        setActiveSwatch(card, card.querySelector(".product-card-swatch[data-default='1']"));
    }

    // mouseover/mouseout bubble (unlike mouseenter/mouseleave), so these can be delegated at the
    // document level the same way the click handler below is.
    document.addEventListener("mouseover", function (e) {
        var swatch = e.target.closest(".product-card-swatch");
        if (!swatch) return;
        var card = cardOf(swatch);
        if (!card) return;
        applyColor(card, swatch);
    });

    document.addEventListener("mouseout", function (e) {
        var swatch = e.target.closest(".product-card-swatch");
        if (!swatch) return;
        var card = cardOf(swatch);
        if (!card) return;
        // Only reset once the pointer has actually left the whole card (not just moved from one
        // swatch to another, or from the swatch onto the card image) — e.relatedTarget is where
        // the mouse is going.
        var goingTo = e.relatedTarget;
        if (goingTo && cardOf(goingTo) === card) return;
        resetColor(card);
    });

    // Swatches are <button>, so clicking one shouldn't submit/navigate anything on its own — it
    // only "arms" the color (image swap + card links updated). A separate click on the image or
    // name is what actually opens the product, already carrying the armed color through.
    document.addEventListener("click", function (e) {
        var swatch = e.target.closest(".product-card-swatch");
        if (!swatch) return;
        e.preventDefault();
        var card = cardOf(swatch);
        if (!card) return;
        applyColor(card, swatch);
    });
})();
