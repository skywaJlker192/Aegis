// Чат «Протокол связи» на странице экспедиции. Отдельный SignalR-хаб /hubs/comms (ExpeditionCommsHub).
// Сервер сам разводит сообщения по группам экспедиций; здесь — подключение, история, отправка и приём.
(function () {
    'use strict';

    var panel = document.getElementById('comms-panel');
    var root = document.getElementById('expedition-root');
    if (!panel || !root || !window.signalR) return;

    var expeditionId = parseInt(root.getAttribute('data-expedition-id'), 10);
    var myCallsign = panel.getAttribute('data-callsign') || '';

    var logEl = document.getElementById('comms-log');
    var lamp = document.getElementById('comms-lamp');
    var stateEl = document.getElementById('comms-state');
    var input = document.getElementById('comms-input');
    var sendBtn = document.getElementById('comms-send');

    // ---------- Индикатор связи ----------
    function setLink(kind, text) {
        // kind: live (мигающий зелёный) | lost (красный) | off (серый)
        lamp.className = 'lamp ' + (kind === 'live' ? 'lamp-live' : kind === 'lost' ? 'lamp-lost' : 'lamp-off');
        stateEl.textContent = text;
        var enabled = kind === 'live';
        input.disabled = !enabled;
        sendBtn.disabled = !enabled;
    }

    // ---------- Отрисовка ----------
    function hhmm(date) {
        var d = new Date(date);
        return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2);
    }

    // Сообщение добавляется через textContent: чужой текст никогда не попадает в разметку как HTML
    function addMessage(m, markNew) {
        var li = document.createElement('li');
        li.className = 'comm-line comm-' + m.kind;
        li.setAttribute('data-kind', m.kind);

        var time = document.createElement('span');
        time.className = 'comm-time';
        time.textContent = '[' + hhmm(m.timestamp) + '] ';

        var who = document.createElement('span');
        who.className = 'comm-who';
        who.textContent = m.kind === 'system' ? '' : (m.kind === 'hq' ? 'ШТАБ/' + m.sender : m.sender) + ': ';

        var body = document.createElement('span');
        body.className = 'comm-text';
        body.textContent = m.message;

        li.appendChild(time);
        li.appendChild(who);
        li.appendChild(body);

        if (markNew && !m.isRead && m.sender !== myCallsign) {
            var tag = document.createElement('span');
            tag.className = 'comm-new';
            tag.textContent = ' ● НОВОЕ';
            li.appendChild(tag);
        }

        logEl.appendChild(li);
        logEl.scrollTop = logEl.scrollHeight;
    }

    function renderHistory(list) {
        logEl.textContent = '';
        if (!list.length) {
            addMessage({ kind: 'system', sender: 'СИСТЕМА', message: 'КАНАЛ СВЯЗИ ОТКРЫТ. СООБЩЕНИЙ ПОКА НЕТ.', timestamp: new Date(), isRead: true });
            return;
        }
        list.forEach(function (m) { addMessage(m, true); });
    }

    // Текст ошибки хаба: SignalR добавляет служебный префикс, оставляем только сообщение сервера
    function hubError(e) {
        var msg = (e && e.message) || 'ошибка связи';
        var i = msg.indexOf('HubException: ');
        return i >= 0 ? msg.slice(i + 'HubException: '.length) : msg;
    }

    // ---------- Подключение ----------
    var connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/comms')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
        .build();

    // Событие ReceiveMessage получают все участники экспедиции и координаторы из группы
    connection.on('ReceiveMessage', function (m) {
        if (m.expeditionId !== expeditionId) return;
        addMessage(m, false);

        // Чужое сообщение мы увидели — отмечаем прочитанным
        if (m.kind !== 'system' && m.sender !== myCallsign) {
            connection.invoke('MarkRead', expeditionId).catch(function () { /* не критично */ });
        }
    });

    // Вступить в группу экспедиции и получить историю (то же самое после переподключения)
    async function join() {
        var history = await connection.invoke('JoinExpedition', expeditionId);
        renderHistory(history);
        setLink('live', 'НА СВЯЗИ');
    }

    connection.onreconnecting(function () { setLink('lost', 'ПЕРЕПОДКЛЮЧЕНИЕ...'); });
    connection.onreconnected(async function () {
        try { await join(); } catch (e) { setLink('lost', 'НЕТ ДОПУСКА К КАНАЛУ'); Aegis.toast(hubError(e), 'crit'); }
    });
    connection.onclose(function () { setLink('lost', 'СИГНАЛ ПОТЕРЯН'); });

    async function start() {
        try {
            await connection.start();
            await join();
        } catch (e) {
            setLink('lost', 'СИГНАЛ ПОТЕРЯН');
            Aegis.toast('Канал связи недоступен: ' + hubError(e), 'crit');
        }
    }

    // ---------- Отправка ----------
    async function send() {
        var text = input.value.trim();
        if (!text) return;

        sendBtn.disabled = true;
        try {
            await connection.invoke('SendMessage', expeditionId, text);
            input.value = '';
        } catch (e) {
            Aegis.toast(hubError(e), 'crit');
        } finally {
            sendBtn.disabled = false;
            input.focus();
        }
    }

    sendBtn.addEventListener('click', send);
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); send(); }
    });

    start();
})();
