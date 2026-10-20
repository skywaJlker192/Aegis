// Страница экспедиции: SOS и добавление агента в экипаж (AJAX через fetch).
(function () {
    'use strict';

    var root = document.getElementById('expedition-root');
    if (!root) return;
    var expeditionId = root.getAttribute('data-expedition-id');

    // ---------- 1. SOS / запрос эвакуации: POST, статус -> Aborted ----------
    var abortBtn = document.getElementById('btn-abort');
    if (abortBtn) {
        abortBtn.addEventListener('click', async function () {
            if (!confirm('Прервать экспедицию и запросить эвакуацию?')) return;

            abortBtn.disabled = true;
            var res = await Aegis.request('POST', '/ajax/expeditions/' + expeditionId + '/abort');

            if (!res.ok) {
                abortBtn.disabled = false;
                // 401 -> «ОТКАЗАНО В ДОСТУПЕ» (текст формирует site.js)
                Aegis.toast(res.error, 'crit');
                return;
            }

            // Частичное обновление DOM: меняем только бейдж и лампочку статуса
            var badge = document.getElementById('exp-status');
            var lamp = document.getElementById('exp-lamp');
            badge.textContent = res.data.statusLabel;
            badge.className = 'badge status-aborted';
            lamp.className = 'lamp status-aborted';

            abortBtn.remove();
            var panel = document.getElementById('add-member-panel');
            if (panel) panel.remove();
            Aegis.toast('ЭКСПЕДИЦИЯ ПРЕРВАНА. ЭВАКУАЦИЯ ЗАПРОШЕНА.', 'warn');
        });
    }

    // ---------- 2. Добавление агента в экипаж: POST, новая строка в таблице ----------
    var addBtn = document.getElementById('btn-add-member');
    if (addBtn) {
        addBtn.addEventListener('click', async function () {
            var agentSelect = document.getElementById('agent-select');
            var roleSelect = document.getElementById('role-select');

            if (!agentSelect.value) {
                Aegis.toast('Выберите агента из списка.', 'warn');
                return;
            }

            addBtn.disabled = true;
            var res = await Aegis.request('POST', '/ajax/expeditions/' + expeditionId + '/members', {
                agentId: agentSelect.value,
                roleInMission: roleSelect.value
            });
            addBtn.disabled = false;

            if (!res.ok) {
                Aegis.toast(res.error, 'crit');
                return;
            }

            // Строим строку через createElement + textContent: никакого innerHTML с данными
            var d = res.data;
            var tr = document.createElement('tr');
            tr.id = 'member-' + d.agentId;

            var tdName = document.createElement('td');
            tdName.textContent = d.callsign;
            var tdRole = document.createElement('td');
            tdRole.textContent = d.role;
            var tdState = document.createElement('td');
            var lamp = document.createElement('span');
            lamp.className = 'lamp status-active';
            tdState.appendChild(lamp);
            tdState.appendChild(document.createTextNode('НА СВЯЗИ'));

            tr.appendChild(tdName);
            tr.appendChild(tdRole);
            tr.appendChild(tdState);
            document.getElementById('crew-body').appendChild(tr);

            // Агент теперь в экипаже — убираем из выпадающего списка
            var opt = agentSelect.querySelector('option[value="' + d.agentId + '"]');
            if (opt) opt.remove();
            agentSelect.value = '';

            Aegis.toast('Агент ' + d.callsign + ' добавлен в экипаж.');
        });
    }

    // ---------- 3. Отправка телеметрии через SignalR (realtime.js) ----------
    var telemetryBtn = document.getElementById('btn-telemetry');
    var telemetryInput = document.getElementById('telemetry-input');
    if (telemetryBtn && telemetryInput) {
        var send = async function () {
            var text = telemetryInput.value.trim();
            if (!text) return;
            if (!window.AegisRealtime || !window.AegisRealtime.sendTelemetry) {
                Aegis.toast('Канал связи не готов.', 'warn');
                return;
            }
            try {
                await window.AegisRealtime.sendTelemetry(parseInt(expeditionId, 10), text);
                telemetryInput.value = '';
            } catch (e) {
                Aegis.toast('Не удалось передать: ' + (e.message || 'ошибка связи'), 'crit');
            }
        };
        telemetryBtn.addEventListener('click', send);
        telemetryInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') send();
        });
    }
})();
