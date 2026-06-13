(function () {
    const state = { map: null };

    window.routeMap = {
        render: function (elementId, stops) {
            const el = document.getElementById(elementId);
            if (!el || !window.L || !stops) {
                return;
            }

            if (state.map) {
                state.map.remove();
                state.map = null;
            }

            const map = L.map(el, { scrollWheelZoom: false });
            state.map = map;

            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; OpenStreetMap contributors'
            }).addTo(map);

            const latlngs = [];
            stops.forEach(function (s) {
                const ll = [s.lat, s.lng];
                latlngs.push(ll);

                let cls = 'route-pin';
                if (s.kind === 'Office') {
                    cls += ' office';
                } else if (s.violation) {
                    cls += ' viol';
                }

                const icon = L.divIcon({
                    className: 'route-pin-wrap',
                    html: '<div class="' + cls + '">' + s.order + '</div>',
                    iconSize: [26, 26],
                    iconAnchor: [13, 13]
                });

                L.marker(ll, { icon: icon })
                    .addTo(map)
                    .bindTooltip(s.order + '. ' + s.label);
            });

            if (latlngs.length > 1) {
                L.polyline(latlngs, { color: '#0057d8', weight: 3, opacity: 0.7 }).addTo(map);
            }

            if (latlngs.length > 0) {
                map.fitBounds(L.latLngBounds(latlngs).pad(0.2));
            } else {
                map.setView([35.6618, 138.5683], 13);
            }

            setTimeout(function () { map.invalidateSize(); }, 100);
        }
    };
})();
