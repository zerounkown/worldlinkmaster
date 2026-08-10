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
                renderSizes(sizesByColorId[colorId] || []);
            }

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
