(function () {
    "use strict";

    var data = window.storeLocatorData;
    if (!data || typeof L === "undefined") {
        return;
    }

    var map, youAreHereMarker;
    var storeMarkers = [];

    function toRad(deg) {
        return (deg * Math.PI) / 180;
    }

    // Straight-line distance, not driving distance — see the plan's rationale for
    // preferring Haversine over a routing API for a "nearest store" sort.
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

    function focusStore(store) {
        map.panTo([store.lat, store.lng]);
        map.setZoom(15);
        var marker = storeMarkers.filter(function (m) {
            return m.__storeId === store.id;
        })[0];
        if (marker) {
            marker.openPopup();
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
            map.removeLayer(m);
        });
        storeMarkers = [];
    }

    function renderMarkers(stores, userLocation) {
        clearStoreMarkers();
        var latLngs = [];

        stores.forEach(function (store) {
            var marker = L.marker([store.lat, store.lng]).addTo(map);
            marker.bindPopup("<strong>" + store.name + "</strong><br>" + store.address);
            marker.__storeId = store.id;
            storeMarkers.push(marker);
            latLngs.push([store.lat, store.lng]);
        });

        if (youAreHereMarker) {
            map.removeLayer(youAreHereMarker);
            youAreHereMarker = null;
        }

        if (userLocation) {
            youAreHereMarker = L.circleMarker([userLocation.lat, userLocation.lng], {
                radius: 9,
                fillColor: "#e53935",
                color: "#ffffff",
                weight: 2,
                fillOpacity: 1
            }).addTo(map);
            youAreHereMarker.bindPopup("<strong>" + data.youAreHereLabel + "</strong>").openPopup();
            latLngs.push([userLocation.lat, userLocation.lng]);
        }

        if (latLngs.length === 1) {
            map.setView(latLngs[0], 13);
        } else if (latLngs.length > 1) {
            map.fitBounds(L.latLngBounds(latLngs), { padding: [60, 60] });
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
        fetch(data.geocodeSearchUrl + "?query=" + encodeURIComponent(query))
            .then(function (resp) {
                return resp.json();
            })
            .then(function (result) {
                if (result.success) {
                    renderResults({ lat: result.lat, lng: result.lng });
                } else {
                    alert(data.noAddressFoundLabel);
                }
            })
            .catch(function () {
                alert(data.noAddressFoundLabel);
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

        // Deliberately no live/per-keystroke lookups here — Nominatim's usage policy caps
        // requests at 1/second, so geocoding only fires on an explicit submit.
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

    var mapEl = document.getElementById("storeLocatorMap");
    if (!mapEl) {
        return;
    }

    map = L.map(mapEl).setView([24.35, 54.7], 8);
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "&copy; <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a> contributors",
        maxZoom: 19
    }).addTo(map);

    bindControls();
    renderResults(null);
})();
