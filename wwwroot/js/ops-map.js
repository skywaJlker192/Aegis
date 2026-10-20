// «Карта операций»: рисует аномалии и агентов на SVG-схеме, плавно двигает точки,
// соединяет линиями агентов одной экспедиции и показывает карточку агента по клику.
// Первое состояние берётся по AJAX (/ajax/map/state), дальше схему обновляет событие SignalR
// AgentLocationUpdate (realtime.js пересылает его сюда как DOM-событие 'aegis:agent-location').
(function () {
    'use strict';

    var root = document.getElementById('ops-map');
    if (!root) return;

    var SVG_NS = 'http://www.w3.org/2000/svg';
    var layerLinks = document.getElementById('layer-links');
    var layerAnomalies = document.getElementById('layer-anomalies');
    var layerAgents = document.getElementById('layer-agents');
    var updatedEl = document.getElementById('map-updated');

    var card = {
        root: document.getElementById('agent-card'),
        lamp: document.getElementById('card-lamp'),
        callsign: document.getElementById('card-callsign'),
        status: document.getElementById('card-status'),
        signal: document.getElementById('card-signal'),
        last: document.getElementById('card-last'),
        exp: document.getElementById('card-exp'),
        target: document.getElementById('card-target'),
        close: document.getElementById('card-close')
    };

    var SIGNAL_TEXT = { ok: 'НА СВЯЗИ', warn: 'ПРЕДУПРЕЖДЕНИЕ', crit: 'КРИТИЧЕСКОЕ' };

    var agents = {};        // agentId -> { data, g, x, y, tx, ty }
    var selectedId = null;
    var animating = false;

    function svgEl(name, attrs, parent) {
        var el = document.createElementNS(SVG_NS, name);
        Object.keys(attrs || {}).forEach(function (k) { el.setAttribute(k, attrs[k]); });
        if (parent) parent.appendChild(el);
        return el;
    }

    function stamp(date) { return new Date(date).toLocaleTimeString('ru-RU'); }

    function relative(date) {
        var sec = Math.max(0, Math.round((Date.now() - new Date(date).getTime()) / 1000));
        if (sec < 60) return sec + ' с назад';
        if (sec < 3600) return Math.round(sec / 60) + ' мин назад';
        return Math.round(sec / 3600) + ' ч назад';
    }

    // Строка в журналах (на дашборде и на странице карты), если они есть
    function log(text, cls) {
        ['event-feed', 'map-log'].forEach(function (id) {
            var list = document.getElementById(id);
            if (!list) return;
            var li = document.createElement('li');
            li.className = 'terminal-line' + (cls ? ' ' + cls : '');
            li.textContent = text;
            list.appendChild(li);
            list.scrollTop = list.scrollHeight;
        });
    }

    // ---------- Аномалии ----------
    function renderAnomalies(list) {
        layerAnomalies.textContent = '';
        list.forEach(function (a) {
            var g = svgEl('g', { 'class': 'anomaly-marker threat-' + a.level.toLowerCase(), 'data-anomaly-id': a.id }, layerAnomalies);
            g.style.transform = 'translate(' + a.position.x + 'px,' + a.position.y + 'px)';
            svgEl('polygon', { points: '0,-9 9,0 0,9 -9,0', 'class': 'anomaly-shape' }, g);
            var title = svgEl('title', {}, g);
            title.textContent = a.code + ' // ' + a.title + ' // ' + a.levelLabel;
            var t = svgEl('text', { x: 0, y: 22, 'text-anchor': 'middle', 'class': 'anomaly-code' }, g);
            t.textContent = a.code;
        });
    }

    // ---------- Агенты ----------
    function normalize(d) {
        // Данные приходят и из /ajax/map/state, и из SignalR в одной и той же форме
        return d;
    }

    function applyAppearance(entry) {
        var d = entry.data;
        entry.g.setAttribute('class', 'agent sig-' + d.signal + (entry.id === selectedId ? ' selected' : ''));
        entry.title.textContent = d.callsign + ' // ' + d.statusLabel + ' // ' + SIGNAL_TEXT[d.signal];
    }

    function placeAgent(entry) {
        entry.g.style.transform = 'translate(' + entry.x + 'px,' + entry.y + 'px)';
    }

    function createAgent(d) {
        var g = svgEl('g', { 'class': 'agent', 'data-agent-id': d.agentId, tabindex: 0, role: 'button' }, layerAgents);
        svgEl('circle', { r: 7, 'class': 'agent-pulse' }, g);   // мигающее кольцо
        svgEl('circle', { r: 5, 'class': 'agent-dot' }, g);     // сама точка
        var label = svgEl('text', { x: 11, y: 4, 'class': 'agent-label' }, g);
        label.textContent = d.callsign;
        var title = svgEl('title', {}, g);

        var entry = { id: d.agentId, data: d, g: g, title: title, x: d.coordinates.x, y: d.coordinates.y, tx: d.coordinates.x, ty: d.coordinates.y };
        agents[d.agentId] = entry;

        g.addEventListener('click', function (e) { e.stopPropagation(); select(d.agentId); });
        g.addEventListener('keydown', function (e) { if (e.key === 'Enter') select(d.agentId); });

        applyAppearance(entry);
        placeAgent(entry);
        return entry;
    }

    // Новые данные об агенте: создаём точку или запускаем плавное перемещение
    function upsert(raw, announce) {
        var d = normalize(raw);
        var entry = agents[d.agentId];
        var prevSignal = entry ? entry.data.signal : null;

        if (!entry) {
            entry = createAgent(d);
        } else {
            entry.data = d;
            entry.tx = d.coordinates.x;
            entry.ty = d.coordinates.y;
            applyAppearance(entry);
            startAnimation();
        }

        if (announce && prevSignal && prevSignal !== d.signal && d.signal !== 'ok') {
            log('[' + stamp(d.lastActivity) + '] ТРЕКЕР // ' + d.callsign + ': ' + SIGNAL_TEXT[d.signal], d.signal === 'crit' ? 'crit' : 'warn');
        }

        if (selectedId === d.agentId) fillCard(entry);
        drawLinks();
        if (updatedEl) updatedEl.textContent = 'ОБНОВЛЕНО: ' + new Date().toLocaleTimeString('ru-RU');
    }

    // ---------- Плавное движение точек (и линий связи вместе с ними) ----------
    function frame() {
        var moving = false;
        Object.keys(agents).forEach(function (id) {
            var a = agents[id];
            var dx = a.tx - a.x, dy = a.ty - a.y;
            if (Math.abs(dx) > 0.15 || Math.abs(dy) > 0.15) {
                a.x += dx * 0.1;
                a.y += dy * 0.1;
                moving = true;
            } else {
                a.x = a.tx; a.y = a.ty;
            }
            placeAgent(a);
        });
        drawLinks();
        if (moving) {
            requestAnimationFrame(frame);
        } else {
            animating = false;
        }
    }

    function startAnimation() {
        if (animating) return;
        animating = true;
        requestAnimationFrame(frame);
    }

    // ---------- Линии связи между агентами одной экспедиции ----------
    function drawLinks() {
        layerLinks.textContent = '';
        var groups = {};
        Object.keys(agents).forEach(function (id) {
            var a = agents[id];
            var exp = a.data.expeditionId;
            if (exp == null) return;
            (groups[exp] = groups[exp] || []).push(a);
        });

        Object.keys(groups).forEach(function (exp) {
            var list = groups[exp].sort(function (p, q) { return p.id < q.id ? -1 : 1; });
            for (var i = 0; i < list.length - 1; i++) {
                svgEl('line', {
                    x1: list[i].x, y1: list[i].y, x2: list[i + 1].x, y2: list[i + 1].y,
                    'class': 'map-link', 'data-expedition-id': exp
                }, layerLinks);
            }
        });
    }

    // ---------- Карточка агента ----------
    function fillCard(entry) {
        var d = entry.data;
        card.callsign.textContent = d.callsign;
        card.lamp.className = 'lamp sig-' + d.signal;
        card.status.textContent = d.statusLabel;
        card.signal.textContent = SIGNAL_TEXT[d.signal];
        card.last.textContent = stamp(d.lastActivity) + ' (' + relative(d.lastActivity) + ')';
        if (d.expeditionId != null) {
            card.exp.textContent = d.expeditionCode;
            card.exp.setAttribute('href', '/Expeditions/Details/' + d.expeditionId);
        } else {
            card.exp.textContent = 'НЕ НАЗНАЧЕНА';
            card.exp.removeAttribute('href');
        }
        card.target.textContent = d.target || '—';
    }

    function select(id) {
        var prev = selectedId && agents[selectedId];
        selectedId = id;
        if (prev) applyAppearance(prev);
        var entry = agents[id];
        applyAppearance(entry);
        fillCard(entry);
        card.root.hidden = false;
    }

    function deselect() {
        var prev = selectedId && agents[selectedId];
        selectedId = null;
        if (prev) applyAppearance(prev);
        card.root.hidden = true;
    }

    card.close.addEventListener('click', deselect);
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') deselect(); });
    document.getElementById('ops-svg').addEventListener('click', deselect);

    // ---------- Запуск ----------
    document.addEventListener('aegis:agent-location', function (e) { upsert(e.detail, true); });

    async function load() {
        var res = await Aegis.request('GET', '/ajax/map/state');
        if (!res.ok) {
            if (updatedEl) updatedEl.textContent = res.error;
            return;
        }
        renderAnomalies(res.data.anomalies);
        res.data.agents.forEach(function (a) { upsert(a, false); });
        log('[' + stamp(res.data.serverTime) + '] ТРЕКЕР // на схеме агентов: ' + res.data.agents.length);
    }

    load();

    // Для автотестов
    window.AegisMap = { agents: agents, select: select };
})();
