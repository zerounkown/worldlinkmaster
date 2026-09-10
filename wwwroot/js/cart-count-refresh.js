// Home/Products/Index/Details are output-cached for anonymous requests (see
// AnonymousOnlyOutputCachePolicy), so the server-rendered cart badge on those pages can be
// stale — frozen at whatever count was true for the first visitor who populated that cache
// entry, not necessarily this visitor's own session. Re-fetch the real count from the
// deliberately-uncached /Cart/Count endpoint on every page load and correct the badge if it
// differs. Harmless (a same-value no-op) on pages that were never cached to begin with.
(function () {
    fetch("/Cart/Count", { credentials: "same-origin" })
        .then(function (res) { return res.ok ? res.json() : null; })
        .then(function (data) {
            if (!data) {
                return;
            }
            document.querySelectorAll(".cart-count").forEach(function (el) {
                el.textContent = data.count;
            });
        })
        .catch(function () { /* badge just keeps its server-rendered value */ });
})();
