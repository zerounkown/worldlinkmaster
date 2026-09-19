(function () {
    var mainImage = document.getElementById("mainProductImage");
    var mainVideo = document.getElementById("mainProductVideo");
    var zoomContainer = document.getElementById("zoomContainer");
    var thumbsContainer = document.getElementById("productThumbs");
    var lightbox = document.getElementById("zoomLightbox");
    var lightboxImage = document.getElementById("zoomLightboxImage");
    var lightboxClose = document.getElementById("zoomLightboxClose");
    var colorLabel = document.getElementById("selectedColorLabel");
    var galleryDataEl = document.getElementById("colorGalleryData");
    var sizeDataEl = document.getElementById("sizesByColorData");
    var sizeContainer = document.getElementById("sizeOptionsContainer");
    var lifestyleDataEl = document.getElementById("lifestyleByColorData");
    var lifestylePhoto = document.getElementById("pdpLifestylePhoto");
    var lifestyleImage = document.getElementById("pdpLifestyleImage");
    var skuValueEl = document.getElementById("pdpSkuValue");
    var variantSkuDataEl = document.getElementById("variantSkuData");
    var thumbsArrowPrev = document.getElementById("thumbsArrowPrev");
    var thumbsArrowNext = document.getElementById("thumbsArrowNext");
    var splitSizeConfigEl = document.getElementById("splitSizeConfig");
    var sizeUnitContainer = document.getElementById("sizeUnitOptionsContainer");
    var sizeLengthContainer = document.getElementById("sizeLengthOptionsContainer");
    var sizeHiddenInput = document.getElementById("pdpSizeHiddenInput");

    if (!mainImage || !zoomContainer) {
        return;
    }

    // Per-color galleries (Media Rules M001-M009) — only present on products imported through
    // the Product Importer. { "<productColorId>": [ { type, url, thumb, alt }, ... ], ... }
    var galleryByColorId = null;
    if (galleryDataEl) {
        try {
            galleryByColorId = JSON.parse(galleryDataEl.textContent);
        } catch (e) {
            galleryByColorId = null;
        }
    }

    // Per-color size lists — sizes must be scoped to the selected color's own variants, since
    // colors that share size labels (S/M/L, 30/32/34, ...) would otherwise show every size from
    // every color mixed together. { "<productColorId>": ["30", "32", ...], ... }
    var sizesByColorId = null;
    if (sizeDataEl) {
        try {
            sizesByColorId = JSON.parse(sizeDataEl.textContent);
        } catch (e) {
            sizesByColorId = null;
        }
    }

    // Per-color Description-tab lifestyle photo (Media Role "Lifestyle") — already resolved
    // server-side to include the default-color fallback, so a null/missing entry here means
    // there's genuinely nothing to show for that color. { "<productColorId>": "<url>"|null }
    var lifestyleByColorId = null;
    if (lifestyleDataEl) {
        try {
            lifestyleByColorId = JSON.parse(lifestyleDataEl.textContent);
        } catch (e) {
            lifestyleByColorId = null;
        }
    }

    // Per-variant SKU, keyed the same way as the size lookup above: "<colorKey>|<sizeLabel>",
    // where colorKey is whichever id the currently-checked color radio's data-color-id carries
    // (ProductColor.Id or shared Color.Id, matching how the server built this map) and "" fills
    // in for a missing color/size dimension. { "<colorKey>|<sizeLabel>": "<sku>", ... }
    var variantSkuByKey = null;
    if (variantSkuDataEl) {
        try {
            variantSkuByKey = JSON.parse(variantSkuDataEl.textContent);
        } catch (e) {
            variantSkuByKey = null;
        }
    }

    // Presentation-only Size/Length split (server-gated to WMN-R and FLT-SUIT via
    // splitSizeConfig — see Details.cshtml for why those two and not others). The two visible
    // pill rows never submit directly; they only drive the hidden #pdpSizeHiddenInput, which
    // carries name="size" with the same recombined label ("4"+"R"="4R") the flat single-row
    // mode would have submitted — CartController and everything else downstream is unaware
    // this split exists.
    var isSplitSizeMode = false;
    if (splitSizeConfigEl) {
        try {
            isSplitSizeMode = !!JSON.parse(splitSizeConfigEl.textContent).enabled;
        } catch (e) {
            isSplitSizeMode = false;
        }
    }
    var currentSplitLabels = [];

    function currentColorKey() {
        var checked = document.querySelector('input[name="color"]:checked');
        return (checked && checked.getAttribute("data-color-id")) || "";
    }

    function currentSizeLabel() {
        if (isSplitSizeMode) {
            return sizeHiddenInput ? sizeHiddenInput.value : "";
        }
        var checked = sizeContainer ? sizeContainer.querySelector('input[name="size"]:checked') : null;
        return checked ? checked.value : "";
    }

    function decomposeSize(label) {
        var match = /^(\d+)([A-Za-z]+)$/.exec(label);
        return match ? { unit: match[1], length: match[2].toUpperCase() } : null;
    }

    function lengthSortRank(length) {
        return length === "S" ? 0 : length === "R" ? 1 : length === "L" ? 2 : 3;
    }

    function uniqueInOrder(values) {
        var seen = {};
        var result = [];
        values.forEach(function (v) {
            if (!seen[v]) { seen[v] = true; result.push(v); }
        });
        return result;
    }

    function unitsForLength(length) {
        return uniqueInOrder(currentSplitLabels.map(decomposeSize).filter(function (d) { return d && d.length === length; }).map(function (d) { return d.unit; }));
    }

    function lengthsForUnit(unit) {
        var lengths = uniqueInOrder(currentSplitLabels.map(decomposeSize).filter(function (d) { return d && d.unit === unit; }).map(function (d) { return d.length; }));
        return lengths.sort(function (a, b) { return lengthSortRank(a) - lengthSortRank(b); });
    }

    // Rebuilds one pill row (unit or length) and returns whichever value ends up selected —
    // preferredValue if it's still a valid option, otherwise the first available one. Shared by
    // both axes since they're otherwise identical markup/behavior.
    function buildSplitPillRow(container, inputName, values, preferredValue) {
        if (!container) return null;
        container.innerHTML = "";
        var selected = values.indexOf(preferredValue) >= 0 ? preferredValue : values[0];
        values.forEach(function (value, i) {
            var pill = document.createElement("label");
            pill.className = "size-pill";
            var input = document.createElement("input");
            input.type = "radio";
            input.name = inputName;
            input.value = value;
            if (value === selected) {
                input.checked = true;
            }
            if (i === 0) {
                input.required = true;
            }
            pill.appendChild(input);
            var span = document.createElement("span");
            span.textContent = value;
            pill.appendChild(span);
            container.appendChild(pill);
        });
        return selected || null;
    }

    function syncSplitSize(unit, length) {
        if (sizeHiddenInput) {
            sizeHiddenInput.value = (unit || "") + (length || "");
        }
        updateSku();
    }

    function currentSplitUnit() {
        var checked = sizeUnitContainer ? sizeUnitContainer.querySelector('input[name="sizeUnit"]:checked') : null;
        return checked ? checked.value : null;
    }

    function currentSplitLength() {
        var checked = sizeLengthContainer ? sizeLengthContainer.querySelector('input[name="sizeLength"]:checked') : null;
        return checked ? checked.value : null;
    }

    // Rebuilds both pill rows for a new color's label set (mirrors renderSizes' role in flat
    // mode). Length options are scoped to whichever unit ends up selected, so the customer can
    // never land on a combination that doesn't actually exist as a variant (e.g. FLT-SUIT's
    // "34" only ever offers "R", never "S" or "L").
    function renderSplitSizes(labels) {
        if (!sizeUnitContainer || !sizeLengthContainer) return;
        currentSplitLabels = labels || [];
        var units = uniqueInOrder(currentSplitLabels.map(decomposeSize).filter(Boolean).map(function (d) { return d.unit; }));
        var selectedUnit = buildSplitPillRow(sizeUnitContainer, "sizeUnit", units, null);
        var selectedLength = buildSplitPillRow(sizeLengthContainer, "sizeLength", lengthsForUnit(selectedUnit), null);
        syncSplitSize(selectedUnit, selectedLength);
    }

    if (isSplitSizeMode && sizeUnitContainer && sizeLengthContainer) {
        // Server already rendered the correct pills for the default color on page load, but
        // currentSplitLabels itself only gets populated by renderSplitSizes (called on color
        // change) — without this, filtering the length/unit options on first interaction (before
        // any color swap) would see an empty label set and break. Seeding it from the same data
        // the server used avoids a redundant DOM rebuild here.
        if (sizesByColorId) {
            currentSplitLabels = sizesByColorId[currentColorKey()] || [];
        }

        sizeUnitContainer.addEventListener("change", function (e) {
            if (!e.target || e.target.name !== "sizeUnit") return;
            var unit = e.target.value;
            var selectedLength = buildSplitPillRow(sizeLengthContainer, "sizeLength", lengthsForUnit(unit), currentSplitLength());
            syncSplitSize(unit, selectedLength);
        });
        sizeLengthContainer.addEventListener("change", function (e) {
            if (!e.target || e.target.name !== "sizeLength") return;
            var length = e.target.value;
            var selectedUnit = buildSplitPillRow(sizeUnitContainer, "sizeUnit", unitsForLength(length), currentSplitUnit());
            syncSplitSize(selectedUnit, length);
        });
    }

    // Falls back to the parent product's SKU (stashed in data-base-sku) whenever the current
    // color+size combination doesn't resolve to a specific variant — e.g. a product with no
    // variants at all, or a color/size combo that genuinely has no stock row for it.
    function updateSku() {
        if (!skuValueEl || !variantSkuByKey) return;
        var key = currentColorKey() + "|" + currentSizeLabel();
        skuValueEl.textContent = variantSkuByKey[key] || skuValueEl.getAttribute("data-base-sku");
    }

    // Size pills are rebuilt from scratch on every color change (see renderSizes below), so a
    // delegated listener on the container — rather than binding each pill directly — is what
    // keeps working after a rebuild without needing to rebind anything.
    if (sizeContainer) {
        sizeContainer.addEventListener("change", function (e) {
            if (e.target && e.target.name === "size") {
                updateSku();
            }
        });
    }

    // Rebuilds the size-pill row from scratch for the given list of size labels — mirrors the
    // server-rendered markup exactly (first pill required + pre-checked) so Add to Cart keeps
    // working without any other JS needing to know sizes changed.
    function renderSizes(labels) {
        if (!sizeContainer) return;
        sizeContainer.innerHTML = "";
        labels.forEach(function (label, i) {
            var pill = document.createElement("label");
            pill.className = "size-pill";

            var input = document.createElement("input");
            input.type = "radio";
            input.name = "size";
            input.value = label;
            if (i === 0) {
                input.required = true;
                input.checked = true;
            }
            pill.appendChild(input);

            var span = document.createElement("span");
            span.textContent = label;
            pill.appendChild(span);

            sizeContainer.appendChild(pill);
        });
    }

    function isVideoItem(el) {
        return el && el.getAttribute("data-type") === "Video";
    }

    // Shows one gallery item (image or video) in the main display area (M009).
    function showMainItem(url, type) {
        if (type === "Video" && mainVideo) {
            mainImage.style.display = "none";
            mainVideo.style.display = "";
            if (mainVideo.querySelector("source")?.src !== url) {
                mainVideo.pause();
                mainVideo.innerHTML = '<source src="' + url + '" />';
                mainVideo.load();
            }
        } else {
            if (mainVideo) {
                mainVideo.pause();
                mainVideo.style.display = "none";
            }
            mainImage.style.display = "";
            mainImage.src = url;
        }
    }

    function setActiveThumb(thumb) {
        if (!thumbsContainer) return;
        thumbsContainer.querySelectorAll(".product-thumb").forEach(function (t) { t.classList.remove("active"); });
        if (thumb) {
            thumb.classList.add("active");
        }
    }

    // Event delegation so thumbs rebuilt after a color swap keep working without rebinding.
    // Clicking a thumbnail also selects the matching color button (when the thumbnail is
    // traceable to one, via data-color-id) so the picture shown and the selected color stay
    // in sync — previously you could click a thumbnail to view e.g. Navy while the Color
    // button below stayed on whatever was selected before, so "the picture" and "the button"
    // disagreed about which color was current.
    if (thumbsContainer) {
        thumbsContainer.addEventListener("click", function (e) {
            var thumb = e.target.closest(".product-thumb");
            if (!thumb) return;
            var full = thumb.getAttribute("data-full");
            var type = thumb.getAttribute("data-type") || "Image";
            showMainItem(full, type);
            setActiveThumb(thumb);

            var colorId = thumb.getAttribute("data-color-id");
            if (colorId) {
                var matchingInput = document.querySelector('input[name="color"][data-color-id="' + colorId + '"]');
                if (matchingInput && !matchingInput.checked) {
                    matchingInput.checked = true;
                    matchingInput.dispatchEvent(new Event("change", { bubbles: true }));
                }
            }
        });
    }

    // Rebuilds the thumbnail strip for a list of gallery items and shows the one flagged
    // "active" (that color's own photo), falling back to the first item when none is flagged.
    function renderGallery(items) {
        if (!thumbsContainer || !items || items.length === 0) return;

        var activeIndex = items.findIndex(function (item) { return item.active === "1"; });
        if (activeIndex < 0) activeIndex = 0;

        thumbsContainer.innerHTML = "";
        items.forEach(function (item, i) {
            var wrap = document.createElement("span");
            wrap.className = "product-thumb-item";

            var img = document.createElement("img");
            img.src = item.thumb || item.url;
            img.className = "product-thumb" + (i === activeIndex ? " active" : "");
            img.setAttribute("data-full", item.url);
            img.setAttribute("data-type", item.type || "Image");
            if (item.colorId) {
                img.setAttribute("data-color-id", item.colorId);
            }
            img.alt = item.alt || "";
            wrap.appendChild(img);

            if (item.type === "Video") {
                var playIcon = document.createElement("i");
                playIcon.className = "bi bi-play-circle-fill product-thumb-play";
                wrap.appendChild(playIcon);
            }

            thumbsContainer.appendChild(wrap);
        });

        showMainItem(items[activeIndex].url, items[activeIndex].type || "Image");
        thumbsContainer.scrollTop = 0;
        thumbsContainer.scrollLeft = 0;
        updateThumbsArrows();
    }

    // The strip scrolls vertically on desktop (a side column) and horizontally on mobile (a row
    // below the main image, per the CSS breakpoint) — whichever axis actually overflows is the
    // one the arrows control, so the same up/down buttons work in both layouts without needing
    // separate left/right icons for mobile.
    function thumbsScrollAxis() {
        if (!thumbsContainer) return null;
        if (thumbsContainer.scrollHeight - thumbsContainer.clientHeight > 2) return "vertical";
        if (thumbsContainer.scrollWidth - thumbsContainer.clientWidth > 2) return "horizontal";
        return null;
    }

    function updateThumbsArrows() {
        if (!thumbsContainer || !thumbsArrowPrev || !thumbsArrowNext) return;
        var axis = thumbsScrollAxis();
        if (!axis) {
            thumbsArrowPrev.classList.remove("visible");
            thumbsArrowNext.classList.remove("visible");
            return;
        }
        var atStart, atEnd;
        if (axis === "vertical") {
            atStart = thumbsContainer.scrollTop <= 2;
            atEnd = thumbsContainer.scrollTop + thumbsContainer.clientHeight >= thumbsContainer.scrollHeight - 2;
        } else {
            atStart = thumbsContainer.scrollLeft <= 2;
            atEnd = thumbsContainer.scrollLeft + thumbsContainer.clientWidth >= thumbsContainer.scrollWidth - 2;
        }
        thumbsArrowPrev.classList.toggle("visible", !atStart);
        thumbsArrowNext.classList.toggle("visible", !atEnd);
    }

    function scrollThumbs(direction) {
        if (!thumbsContainer) return;
        var step = 2 * (70 + 10); // ~2 thumbnails + their gap
        var axis = thumbsScrollAxis();
        if (axis === "horizontal") {
            thumbsContainer.scrollBy({ left: direction * step, behavior: "smooth" });
        } else {
            thumbsContainer.scrollBy({ top: direction * step, behavior: "smooth" });
        }
    }

    if (thumbsArrowPrev) {
        thumbsArrowPrev.addEventListener("click", function () { scrollThumbs(-1); });
    }
    if (thumbsArrowNext) {
        thumbsArrowNext.addEventListener("click", function () { scrollThumbs(1); });
    }
    if (thumbsContainer) {
        thumbsContainer.addEventListener("scroll", updateThumbsArrows);
        window.addEventListener("resize", updateThumbsArrows);
        updateThumbsArrows();
    }

    // Hover-to-zoom: scale the image and track cursor position as the transform origin.
    // The container itself also tilts in 3D toward the cursor, so the photo feels
    // like it's popping toward the customer rather than just zooming flat.
    var maxTilt = 6;
    zoomContainer.addEventListener("mousemove", function (e) {
        if (mainImage.style.display === "none") return; // no hover-zoom while a video is showing
        var rect = zoomContainer.getBoundingClientRect();
        var xPct = (e.clientX - rect.left) / rect.width;
        var yPct = (e.clientY - rect.top) / rect.height;
        mainImage.style.transformOrigin = (xPct * 100) + "% " + (yPct * 100) + "%";
        mainImage.classList.add("zoomed-hover");

        var rotateY = (xPct - 0.5) * maxTilt * 2;
        var rotateX = (0.5 - yPct) * maxTilt * 2;
        zoomContainer.style.transform = "perspective(1000px) rotateX(" + rotateX + "deg) rotateY(" + rotateY + "deg)";
    });

    zoomContainer.addEventListener("mouseleave", function () {
        mainImage.classList.remove("zoomed-hover");
        zoomContainer.style.transform = "";
    });

    // Click-to-enlarge lightbox (images only — video already has native controls for that).
    function openLightbox() {
        if (mainImage.style.display === "none") return;
        lightboxImage.src = mainImage.src;
        lightbox.classList.add("open");
        document.body.style.overflow = "hidden";
    }

    function closeLightbox() {
        lightbox.classList.remove("open");
        document.body.style.overflow = "";
    }

    mainImage.addEventListener("click", openLightbox);
    if (lightboxClose) {
        lightboxClose.addEventListener("click", closeLightbox);
    }
    if (lightbox) {
        lightbox.addEventListener("click", function (e) {
            if (e.target === lightbox) {
                closeLightbox();
            }
        });
    }
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
            closeLightbox();
        }
    });

    // Changing color swaps the whole gallery to that color's Media (shared + color-specific,
    // per M002-M005) when per-color gallery data is available; otherwise falls back to the
    // single representative image the legacy variant-based swatches carry.
    document.querySelectorAll('input[name="color"]').forEach(function (input) {
        input.addEventListener("change", function () {
            if (colorLabel) {
                colorLabel.textContent = input.value;
            }

            var colorId = input.getAttribute("data-color-id");

            if (sizesByColorId && colorId) {
                if (isSplitSizeMode) {
                    renderSplitSizes(sizesByColorId[colorId] || []);
                } else {
                    renderSizes(sizesByColorId[colorId] || []);
                }
            }

            // After renderSizes (if it ran) so this reads whichever size pill just got
            // auto-checked as the new color's default, not the previous color's selection.
            updateSku();

            if (lifestylePhoto && lifestyleImage && lifestyleByColorId && colorId) {
                var lifestyleUrl = lifestyleByColorId[colorId];
                if (lifestyleUrl) {
                    lifestyleImage.src = lifestyleUrl;
                    lifestylePhoto.style.display = "";
                } else {
                    lifestylePhoto.style.display = "none";
                }
            }

            var items = galleryByColorId && colorId ? galleryByColorId[colorId] : null;
            if (items && items.length > 0) {
                renderGallery(items);
                return;
            }

            // Legacy fallback: swap just the main image to this color's representative photo.
            var image = input.getAttribute("data-image");
            if (image) {
                showMainItem(image, "Image");
                if (lightbox && lightbox.classList.contains("open")) {
                    lightboxImage.src = image;
                }
                var matchingThumb = thumbsContainer ? thumbsContainer.querySelector('.product-thumb[data-full="' + CSS.escape(image) + '"]') : null;
                setActiveThumb(matchingThumb);
            }
        });
    });
})();
