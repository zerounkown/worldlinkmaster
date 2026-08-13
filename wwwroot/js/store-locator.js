(function () {
    "use strict";

    var data = window.storeLocatorData;
    if (!data) {
        return;
    }

    var map, geocoder, autocomplete, infoWindow;
    var storeMarkers = [];
    var youAreHereMarker = null;

    function toRad(deg) {
        return (deg * Math.PI) / 180;
    }

    // Straight-line distance, not driving distance — see the plan's rationale for
    // preferring Haversine over the Distance Matrix API for a "nearest store" sort.
    function haversineKm(lat1, lng1, lat2, lng2) {
        var earthRadiusKm = 6371;
        var dLat = toRad(lat2 - lat1);
        var dLng = toRad(lng2 - lng1);
        var a =
            Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) * Math.sin(dLng / 2);
        var c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
        return earthRadiusKm * c;
    }

    function directionsUrl(store) {
        return "https://www.google.com/maps/dir/?api=1&destination=" + store.lat + "," + store.lng;
    }

    function openInfoWindow(marker, store) {
        infoWindow.setContent("<strong>" + store.name + "</strong><br>" + store.address);
        infoWindow.open(map, marker);
    }

    function focusStore(store) {
        map.panTo({ lat: store.lat, lng: store.lng });
        map.setZoom(15);
        var marker = storeMarkers.filter(function (m) {
            return m.__storeId === store.id;
        })[0];
        if (marker) {
            openInfoWindow(marker, store);
        }
    }

    function buildResultCard(store, distanceKm) {
        var template = document.getElementById("storeCardTemplate").firstElementChild;
        var card = template.cloneNode(true);

        var badge = card.querySelector(".store-category-badge");
        badge.textContent = store.category;
        badge.classList.add(store.categoryKey === "OfficialStore" ? "store-category-official" : "store-category-dealer");

        card.querySelector(".store-name").textContent = store.name;

        var distanceEl = card.querySelector(".store-distance");
        if (distanceKm !== null) {
            distanceEl.querySelector("span").textContent = distanceKm.toFixed(1) + " km";
        } else {
            distanceEl.classList.add("d-none");
        }

        card.querySelector(".store-address span").textContent = store.address + (store.city ? ", " + store.city : "");

        var phoneEl = card.querySelector(".store-phone");
        if (store.phone) {
            var phoneLink = phoneEl.querySelector("a");
            phoneLink.textContent = store.phone;
            phoneLink.href = "tel:" + store.phone.replace(/\s+/g, "");
        } else {
            phoneEl.classList.add("d-none");
        }

        var websiteEl = card.querySelector(".store-website");
        if (store.website) {
            websiteEl.classList.remove("d-none");
            var websiteLink = websiteEl.querySelector("a");
            websiteLink.textContent = store.website;
            websiteLink.href = store.website;
        }

        card.querySelector(".store-directions-btn").href = directionsUrl(store);

        card.addEventListener("click", function (e) {
            if (e.target.closest("a")) {
                return;
            }
            focusStore(store);
        });

        return card;
    }

    function clearStoreMarkers() {
        storeMarkers.forEach(function (m) {
            m.setMap(null);
        });
        storeMarkers = [];
    }

    function renderMarkers(stores, userLocation) {
        clearStoreMarkers();
        var bounds = new google.maps.LatLngBounds();

        stores.forEach(function (store) {
            var marker = new google.maps.Marker({
                position: { lat: store.lat, lng: store.lng },
                map: map,
                title: store.name
            });
            marker.__storeId = store.id;
            marker.addListener("click", function () {
                openInfoWindow(marker, store);
            });
            storeMarkers.push(marker);
            bounds.extend(marker.getPosition());
        });

        if (youAreHereMarker) {
            youAreHereMarker.setMap(null);
            youAreHereMarker = null;
        }

        if (userLocation) {
            youAreHereMarker = new google.maps.Marker({
                position: userLocation,
                map: map,
                icon: {
                    path: google.maps.SymbolPath.CIRCLE,
                    scale: 9,
                    fillColor: "#e53935",
                    fillOpacity: 1,
                    strokeColor: "#ffffff",
                    strokeWeight: 2
                },
                title: data.youAreHereLabel,
                zIndex: 999
            });
            var youAreHereInfo = new google.maps.InfoWindow({
                content: "<strong>" + data.youAreHereLabel + "</strong>"
            });
            youAreHereInfo.open(map, youAreHereMarker);
            bounds.extend(userLocation);
        }

        if (!bounds.isEmpty()) {
            map.fitBounds(bounds, 60);
            if (stores.length + (userLocation ? 1 : 0) <= 1) {
                map.setZoom(13);
            }
        }
    }

    function renderResults(userLocation) {
        var resultsList = document.getElementById("storeResultsList");
        var countLabel = document.getElementById("storeResultCount");
        resultsList.innerHTML = "";

        var stores = data.stores.slice();
        if (userLocation) {
            stores.forEach(function (s) {
                s._distanceKm = haversineKm(userLocation.lat, userLocation.lng, s.lat, s.lng);
            });
            stores.sort(function (a, b) {
                return a._distanceKm - b._distanceKm;
            });
        }

        countLabel.textContent = data.resultCountLabel.replace("{0}", stores.length);

        stores.forEach(function (store) {
            resultsList.appendChild(buildResultCard(store, userLocation ? store._distanceKm : null));
        });

        renderMarkers(stores, userLocation);
    }

    function geocodeAndSearch(query) {
        geocoder.geocode({ address: query }, function (results, status) {
            if (status === "OK" && results && results[0]) {
                var loc = results[0].geometry.location;
                renderResults({ lat: loc.lat(), lng: loc.lng() });
            } else {
                alert(data.noAddressFoundLabel);
            }
        });
    }

    function bindControls() {
        var searchInput = document.getElementById("storeSearchInput");
        var searchBtn = document.getElementById("storeSearchBtn");
        var locateMeBtn = document.getElementById("storeLocateMeBtn");

        searchBtn.addEventListener("click", function () {
            var q = searchInput.value.trim();
            if (q) {
                geocodeAndSearch(q);
            }
        });

        searchInput.addEventListener("keydown", function (e) {
            if (e.key === "Enter") {
                e.preventDefault();
                var q = searchInput.value.trim();
                if (q) {
                    geocodeAndSearch(q);
                }
            }
        });

        locateMeBtn.addEventListener("click", function () {
            if (!navigator.geolocation) {
                alert(data.geolocationDeniedLabel);
                return;
            }
            navigator.geolocation.getCurrentPosition(
                function (position) {
                    renderResults({ lat: position.coords.latitude, lng: position.coords.longitude });
                },
                function () {
                    alert(data.geolocationDeniedLabel);
                }
            );
        });
    }

    function setupAutocomplete() {
        autocomplete = new google.maps.places.Autocomplete(document.getElementById("storeSearchInput"));
        autocomplete.addListener("place_changed", function () {
            var place = autocomplete.getPlace();
            if (!place.geometry || !place.geometry.location) {
                return;
            }
            renderResults({ lat: place.geometry.location.lat(), lng: place.geometry.location.lng() });
        });
    }

    // Called by the Google Maps JS SDK once it finishes loading (see the &callback= param
    // on the script tag in Views/Home/Locations.cshtml) — everything that touches `google.maps`
    // has to wait until then, so this is the single entry point for the whole page.
    window.initStoreLocatorMap = function () {
        var mapEl = document.getElementById("storeLocatorMap");
        if (!mapEl) {
            return;
        }

        geocoder = new google.maps.Geocoder();
        infoWindow = new google.maps.InfoWindow();
        map = new google.maps.Map(mapEl, {
            center: { lat: 24.35, lng: 54.7 },
            zoom: 8
        });

        setupAutocomplete();
        bindControls();
        renderResults(null);
    };
})();
