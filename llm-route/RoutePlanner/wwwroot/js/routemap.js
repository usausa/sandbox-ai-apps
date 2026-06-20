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

            // ポリラインは全立ち寄りを順に結ぶ。マーカーは同一地点を1つにまとめる。
            const latlngs = [];
            const groups = new Map();
            const groupOrder = [];
            stops.forEach(function (s) {
                latlngs.push([s.lat, s.lng]);
                const key = s.lat + ',' + s.lng;
                if (!groups.has(key)) {
                    groups.set(key, []);
                    groupOrder.push(key);
                }
                groups.get(key).push(s);
            });

            function timeText(s) {
                if (s.kind === 'Office') {
                    return s.arrival ? ' ' + s.arrival : '';
                }
                if (s.arrival) {
                    return ' 訪問 ' + s.arrival + (s.departure ? '→' + s.departure : '');
                }
                return '';
            }

            groupOrder.forEach(function (key) {
                const items = groups.get(key);
                const first = items[0];
                const ll = [first.lat, first.lng];
                const isOffice = items.some(function (x) { return x.kind === 'Office'; });
                const hasViol = items.some(function (x) { return x.violation; });
                const hasWindow = items.some(function (x) { return x.window; });
                const multi = !isOffice && items.length > 1;

                // 色の優先順位: 拠点 > 時間枠超過 > 時間帯指定あり > 複数件 > 通常
                let cls = 'route-pin';
                if (isOffice) {
                    cls += ' office';
                } else if (hasViol) {
                    cls += ' viol';
                } else if (hasWindow) {
                    cls += ' win';
                } else if (multi) {
                    cls += ' multi';
                }

                // ピンの数字は最初の立ち寄り順。複数件は件数バッジを付ける。
                let pinHtml = '<div class="' + cls + '">' + first.order;
                if (multi) {
                    pinHtml += '<span class="route-pin-count">' + items.length + '</span>';
                }
                pinHtml += '</div>';

                const icon = L.divIcon({
                    className: 'route-pin-wrap',
                    html: pinHtml,
                    iconSize: [26, 26],
                    iconAnchor: [13, 13]
                });

                // ツールチップにはその地点の全件の時刻を列挙する。
                const lines = items.map(function (x) {
                    return x.order + '. ' + x.label + timeText(x)
                        + (x.window ? '（指定 ' + x.window + '）' : '');
                });
                let content = lines.join('<br>');
                if (items.length > 1) {
                    content = '<b>' + items.length + '件</b><br>' + content;
                }

                L.marker(ll, { icon: icon })
                    .addTo(map)
                    .bindTooltip(content);
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
