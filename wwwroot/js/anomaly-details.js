// Страница аномалии: смена уровня угрозы (модератор).
// Сервер сохраняет и рассылает BroadcastThreatLevelChange всем — индикаторы обновятся у всех, включая нас.
(function () {
    'use strict';

    var btn = document.getElementById('btn-threat');
    var select = document.getElementById('threat-select');
    if (!btn || !select) return;

    btn.addEventListener('click', async function () {
        var id = btn.getAttribute('data-target-anomaly');
        btn.disabled = true;

        var res = await Aegis.request('PUT', '/ajax/anomalies/' + id + '/threat', {
            threatLevel: select.value
        });
        btn.disabled = false;

        if (!res.ok) {
            Aegis.toast(res.error, 'crit');
            return;
        }

        Aegis.toast('Уровень угрозы изменён: ' + res.data.levelLabel);
    });
})();
