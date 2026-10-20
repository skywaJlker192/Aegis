// Мини-игра «Протокол сканирования аномалии».
// Ползунок двигает CSS-анимация (@keyframes scan-sweep), а JavaScript в момент нажатия
// считает его положение на шкале 0.00–99.99 кГц и отправляет серверу. Попадание проверяет сервер.
(function () {
    'use strict';

    var MAX_FREQ = 99.99;

    var openBtn = document.getElementById('btn-scan');
    var modal = document.getElementById('scan-modal');
    if (!openBtn || !modal) return;

    var select = document.getElementById('Input_ExpeditionId');
    var badge = document.getElementById('scan-badge');

    var els = {
        target: document.getElementById('scan-target'),
        status: document.getElementById('scan-status'),
        freq: document.getElementById('scan-freq'),
        track: document.getElementById('scan-track'),
        zone: document.getElementById('scan-zone'),
        needle: document.getElementById('scan-needle'),
        result: document.getElementById('scan-result'),
        lock: document.getElementById('scan-lock'),
        retry: document.getElementById('scan-retry'),
        close: document.getElementById('scan-close')
    };

    var state = { anomalyId: null, token: null, running: false, raf: 0 };

    // ---------- Положение ползунка -> частота ----------
    // Чистая функция: центр ползунка относительно ширины шкалы, переведённый в 0.00–99.99
    function frequencyAt(needleRect, trackRect) {
        var center = needleRect.left + needleRect.width / 2;
        var ratio = (center - trackRect.left) / trackRect.width;
        var freq = ratio * MAX_FREQ;
        if (freq < 0) freq = 0;
        if (freq > MAX_FREQ) freq = MAX_FREQ;
        return Math.round(freq * 100) / 100;
    }

    function currentFrequency() {
        return frequencyAt(els.needle.getBoundingClientRect(), els.track.getBoundingClientRect());
    }

    function format(freq) {
        var s = freq.toFixed(2);
        return s.length < 5 ? '0' + s : s;
    }

    // Показание на табло обновляется каждый кадр, пока ползунок движется
    function tick() {
        if (!state.running) return;
        els.freq.textContent = format(currentFrequency());
        state.raf = requestAnimationFrame(tick);
    }

    // ---------- Управление анимацией ----------
    function startSweep(seconds) {
        els.needle.classList.remove('sweeping', 'frozen');
        void els.needle.offsetWidth; // перезапуск CSS-анимации
        els.needle.style.animationDuration = seconds + 's';
        els.needle.classList.add('sweeping');
        state.running = true;
        cancelAnimationFrame(state.raf);
        tick();
    }

    function freezeSweep() {
        state.running = false;
        cancelAnimationFrame(state.raf);
        els.needle.classList.add('frozen'); // animation-play-state: paused
    }

    // ---------- Звук (Web Audio API, без файлов) ----------
    function beep(success) {
        try {
            var Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return;
            var ctx = new Ctx();
            var notes = success ? [660, 880, 1320] : [180];
            notes.forEach(function (f, i) {
                var osc = ctx.createOscillator();
                var gain = ctx.createGain();
                osc.type = success ? 'sine' : 'square';
                osc.frequency.value = f;
                gain.gain.value = 0.06;
                osc.connect(gain);
                gain.connect(ctx.destination);
                var t = ctx.currentTime + i * 0.12;
                osc.start(t);
                osc.stop(t + (success ? 0.11 : 0.3));
            });
            setTimeout(function () { ctx.close(); }, 800);
        } catch (e) { /* звук необязателен */ }
    }

    // ---------- Сценарий игры ----------
    function setStatus(text, blinking) {
        els.status.textContent = text;
        if (blinking) {
            var cur = document.createElement('span');
            cur.className = 'cursor-block';
            els.status.appendChild(cur);
        }
    }

    function showResult(text, cls) {
        els.result.textContent = text;
        els.result.className = 'scan-result ' + (cls || '');
    }

    // Просим у сервера новое задание: зону захвата и токен
    async function begin() {
        els.lock.disabled = true;
        els.retry.hidden = true;
        showResult('', '');
        setStatus('ИНИЦИАЛИЗАЦИЯ', true);

        var res = await Aegis.request('POST', '/ajax/scan/start', { anomalyId: state.anomalyId });
        if (!res.ok) {
            setStatus('СБОЙ ПРОТОКОЛА', false);
            showResult(res.error, 'miss');
            return;
        }

        var d = res.data;
        state.token = d.token;
        els.target.textContent = d.code;

        // Зелёная зона: положение и ширина в процентах шкалы
        els.zone.style.left = (d.zoneStart / MAX_FREQ * 100) + '%';
        els.zone.style.width = ((d.zoneEnd - d.zoneStart) / MAX_FREQ * 100) + '%';

        setStatus('СКАНИРОВАНИЕ', true);
        startSweep(d.sweepSeconds);
        els.lock.disabled = false;
    }

    async function lock() {
        if (!state.running || !state.token) return;

        // Читаем положение в момент нажатия и сразу замораживаем ползунок
        var freq = currentFrequency();
        freezeSweep();
        els.freq.textContent = format(freq);
        els.lock.disabled = true;

        var res = await Aegis.request('POST', '/ajax/scan/lock', {
            anomalyId: state.anomalyId,
            token: state.token,
            frequency: freq
        });

        if (!res.ok) {
            setStatus('СБОЙ ПРОТОКОЛА', false);
            showResult(res.error, 'miss');
            els.retry.hidden = false;
            return;
        }

        if (res.data.success) {
            setStatus('СКАНИРОВАНИЕ ЗАВЕРШЕНО', false);
            showResult(res.data.message, 'hit');
            beep(true);

            badge.textContent = 'ТИП: ' + res.data.kindLabel;
            badge.className = 'badge status-active';
            state.token = null;
        } else {
            setStatus('СКАНИРОВАНИЕ ПРОВАЛЕНО', false);
            showResult(res.data.message, 'miss');
            beep(false);
            els.retry.hidden = false; // нужно повторить
        }
    }

    function openModal() {
        var opt = select.options[select.selectedIndex];
        var id = opt ? parseInt(opt.getAttribute('data-anomaly-id'), 10) : NaN;
        if (!id) return;

        state.anomalyId = id;
        els.target.textContent = '—';
        modal.hidden = false;
        document.body.classList.add('modal-open');
        begin();
    }

    function closeModal() {
        freezeSweep();
        modal.hidden = true;
        document.body.classList.remove('modal-open');
    }

    // Кнопка открывается только когда выбрана экспедиция
    function syncOpenButton() {
        openBtn.disabled = !(select && select.value);
        if (badge && select && select.value) {
            badge.textContent = 'ТИП НЕ ОПРЕДЕЛЁН';
            badge.className = 'badge status-planned';
        }
    }

    openBtn.addEventListener('click', openModal);
    els.lock.addEventListener('click', lock);
    els.retry.addEventListener('click', begin);
    els.close.addEventListener('click', closeModal);
    document.addEventListener('keydown', function (e) {
        if (modal.hidden) return;
        if (e.key === 'Escape') closeModal();
        if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); lock(); }
    });

    if (select) {
        select.addEventListener('change', syncOpenButton);
        syncOpenButton();
    }

    // Для автотестов
    window.AegisScanner = { frequencyAt: frequencyAt };
})();
