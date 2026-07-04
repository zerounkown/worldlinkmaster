(function () {
    var mainImage = document.getElementById("mainProductImage");
    var zoomContainer = document.getElementById("zoomContainer");
    var thumbs = document.querySelectorAll(".product-thumb");
    var lightbox = document.getElementById("zoomLightbox");
    var lightboxImage = document.getElementById("zoomLightboxImage");
    var lightboxClose = document.getElementById("zoomLightboxClose");
    var colorLabel = document.getElementById("selectedColorLabel");

    if (!mainImage || !zoomContainer) {
        return;
    }

    function setActiveThumb(thumb) {
        thumbs.forEach(function (t) { t.classList.remove("active"); });
        if (thumb) {
            thumb.classList.add("active");
        }
    }

    thumbs.forEach(function (thumb) {
        thumb.addEventListener("click", function () {
            var full = thumb.getAttribute("data-full");
            mainImage.src = full;
            setActiveThumb(thumb);
        });
    });

    // Hover-to-zoom: scale the image and track cursor position as the transform origin.
    // The container itself also tilts in 3D toward the cursor, so the photo feels
    // like it's popping toward the customer rather than just zooming flat.
    var maxTilt = 6;
    zoomContainer.addEventListener("mousemove", function (e) {
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

    // Click-to-enlarge lightbox.
    function openLightbox() {
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

    // Changing color swaps the main (and lightbox) image to that color's photo,
    // and syncs the thumbnail strip so the matching thumb shows as active.
    document.querySelectorAll('input[name="color"]').forEach(function (input) {
        input.addEventListener("change", function () {
            if (colorLabel) {
                colorLabel.textContent = input.value;
            }

            var image = input.getAttribute("data-image");
            if (image) {
                mainImage.src = image;
                if (lightbox && lightbox.classList.contains("open")) {
                    lightboxImage.src = image;
                }

                var matchingThumb = null;
                thumbs.forEach(function (thumb) {
                    if (thumb.getAttribute("data-full") === image) {
                        matchingThumb = thumb;
                    }
                });
                setActiveThumb(matchingThumb);
            }
        });
    });
})();
