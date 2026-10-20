// SignalR-клиент ситуационного центра. Подключается на всех страницах для вошедших пользователей.
(function () {
    'use strict';

    var LEVELS = ['threat-low', 'threat-medium', 'threat-high', 'threat-critical'];

    var lamp = document.getElementById('link-lamp');
    var linkText = document.getElementById('link-text');

    function setLink(state, text) {
        if (lamp) lamp.className = 'lamp ' + state;
        if (linkText) linkText.textContent = text;
    }

    // Строка в журнал событий на дашборде и в телеметрию на странице экспедиции
    function addLine(listId, text, cls) {
        var list = document.getElementById(listId);
        if (!list) return;
        var li = document.createElement('li');
        li.className = 'terminal-line' + (cls ? ' ' + cls : '');
        li.textContent = text;
        list.appendChild(li);
        list.scrollTop = list.scrollHeight;
    }

    function time() {
        return new Date().toLocaleTimeString('ru-RU');
    }

    // ---------- Подключение с автоматическим переподключением ----------
    var connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/situation')
        // Повторные попытки через 0, 2, 5, 10, 20 секунд, затем раз в 30
        .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
        .build();

    // ---------- Событие 1: BroadcastThreatLevelChange ----------
    // Все открытые страницы мгновенно перекрашивают индикаторы нужной аномалии
    connection.on('BroadcastThreatLevelChange', function (data) {
        var cls = 'threat-' + String(data.level).toLowerCase();

        document.querySelectorAll('[data-anomaly-id="' + data.anomalyId + '"]').forEach(function (el) {
            LEVELS.forEach(function (c) { el.classList.remove(c); });
            el.classList.add(cls);
        });
        document.querySelectorAll('[data-threat-label-for="' + data.anomalyId + '"]').forEach(function (el) {
            el.textContent = data.levelLabel;
        });

        addLine('event-feed', '[' + time() + '] УГРОЗА // ' + data.title + ' -> ' + data.levelLabel,
            data.level === 'Critical' ? 'crit' : 'warn');
        Aegis.toast('Уровень угрозы: ' + data.title + ' -> ' + data.levelLabel,
            data.level === 'Critical' ? 'crit' : 'warn');
    });

    // ---------- Событие 2: AgentTelemetryUpdate (участники экспедиции + модераторы) ----------
    connection.on('AgentTelemetryUpdate', function (data) {
        var line = '[' + new Date(data.timestamp).toLocaleTimeString('ru-RU') + '] ' +
            data.callsign + ': ' + data.message;

        // Журнал на странице нужной экспедиции
        var root = document.getElementById('expedition-root');
        if (root && root.getAttribute('data-expedition-id') === String(data.expeditionId)) {
            // Убираем заглушку «ожидание сигнала»
            var log = document.getElementById('telemetry-log');
            if (log && log.children.length === 1 && log.children[0].textContent.indexOf('ОЖИДАНИЕ') === 0) {
                log.innerHTML = '';
            }
            addLine('telemetry-log', line);
        }

        addLine('event-feed', 'ТЕЛЕМЕТРИЯ ЭКСП-' + String(data.expeditionId).padStart(3, '0') + ' // ' + line);
    });

    // ---------- Новая аномалия (шлёт AnomalyService через IHubContext) ----------
    connection.on('NewAnomaly', function (data) {
        addLine('event-feed', '[' + time() + '] ' + data.message + ': ' + data.title + ' (' + data.levelLabel + ')', 'crit');
        Aegis.toast(data.message + ': ' + data.title, 'crit');
    });

    // ---------- Завершено сканирование аномалии (приходит только штабу, группа HQ) ----------
    connection.on('ScanCompleted', function (data) {
        addLine('event-feed', '[' + time() + '] СКАНИРОВАНИЕ // ' + data.message + ' — ' + data.kindLabel);
        Aegis.toast(data.message + ': ' + data.kindLabel);
    });

    // ---------- Положение агента (имитация телеметрии, приходит штабу из группы HQ) ----------
    // Сам realtime.js карту не рисует: он передаёт данные скрипту карты через DOM-событие
    connection.on('AgentLocationUpdate', function (data) {
        document.dispatchEvent(new CustomEvent('aegis:agent-location', { detail: data }));
    });

    // ---------- Новое сообщение агента в чате экспедиции (уведомление координатору, группа HQ) ----------
    connection.on('CommsMessage', function (data) {
        // Если координатор уже открыл эту экспедицию, он видит сообщение в самом чате
        var root = document.getElementById('expedition-root');
        if (root && root.getAttribute('data-expedition-id') === String(data.expeditionId)) return;
        addLine('event-feed', '[' + time() + '] СВЯЗЬ // ' + data.code + ' ' + data.callsign + ': ' + data.message, 'warn');
        Aegis.toast('Сообщение ' + data.code + ' от ' + data.callsign + ': ' + data.message, 'warn');
    });

    // ---------- Счётчик «на связи» ----------
    connection.on('PresenceChanged', function (data) {
        var counter = document.getElementById('online-count');
        if (counter) counter.textContent = data.online;
        addLine('event-feed', '[' + time() + '] ' + data.callsign + ' — ' + data.state);
    });

    // ---------- Группы экспедиции ----------
    // Сервер сам добавляет в группы при подключении, а здесь заново вступаем
    // в группу открытой экспедиции после восстановления связи.
    function currentExpeditionId() {
        var root = document.getElementById('expedition-root');
        return root ? parseInt(root.getAttribute('data-expedition-id'), 10) : null;
    }

    async function joinCurrentGroup() {
        var id = currentExpeditionId();
        if (!id) return;
        try {
            await connection.invoke('JoinExpeditionGroup', id);
        } catch (e) {
            console.warn('Не удалось вступить в группу экспедиции', e);
        }
    }

    connection.onreconnecting(function () {
        setLink('lamp-lost', 'СВЯЗЬ: ПЕРЕПОДКЛЮЧЕНИЕ...');
    });

    connection.onreconnected(async function () {
        setLink('lamp-on', 'СВЯЗЬ: НА СВЯЗИ');
        await joinCurrentGroup(); // после восстановления связи заново вступаем в группы
        Aegis.toast('Связь восстановлена.');
    });

    connection.onclose(function () {
        setLink('lamp-lost', 'СВЯЗЬ: СИГНАЛ ПОТЕРЯН');
    });

    async function start() {
        try {
            await connection.start();
            setLink('lamp-on', 'СВЯЗЬ: НА СВЯЗИ');
            await joinCurrentGroup();
        } catch (e) {
            setLink('lamp-lost', 'СВЯЗЬ: СИГНАЛ ПОТЕРЯН');
            // Первое подключение автоматически не повторяется — пробуем сами
            setTimeout(start, 5000);
        }
    }

    // Публичный интерфейс для expedition.js
    window.AegisRealtime = {
        sendTelemetry: function (expeditionId, message) {
            return connection.invoke('SendTelemetry', expeditionId, message);
        }
    };

    start();
})();
