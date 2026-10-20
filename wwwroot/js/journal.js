// Журнал агента: счётчик символов в форме и AJAX-модерация (одобрить / отклонить).
(function () {
    'use strict';

    // ---------- Счётчик символов ----------
    var text = document.getElementById('journal-text');
    var counter = document.getElementById('journal-counter');
    if (text && counter) {
        var update = function () { counter.textContent = String(text.value.length); };
        text.addEventListener('input', update);
        update();
    }

    // ---------- Модерация ----------
    document.addEventListener('click', async function (event) {
        var btn = event.target.closest('.btn-journal');
        if (!btn) return;

        var id = btn.getAttribute('data-id');
        var action = btn.getAttribute('data-action'); // approve | reject
        var card = document.getElementById('journal-' + id);
        var buttons = card ? card.querySelectorAll('.btn-journal') : [btn];
        buttons.forEach(function (b) { b.disabled = true; });

        var res = await Aegis.request('PUT', '/ajax/journal/' + id + '/' + action);
        if (!res.ok) {
            buttons.forEach(function (b) { b.disabled = false; });
            Aegis.toast(res.error, 'crit');
            return;
        }

        var approved = action === 'approve';
        if (card) {
            card.classList.remove('journal-pending');
            card.classList.add(approved ? 'journal-approved' : 'journal-rejected');
            card.setAttribute('data-state', approved ? 'approved' : 'rejected');
            var state = card.querySelector('.journal-state');
            if (state) state.textContent = approved ? 'ОДОБРЕНО' : 'ОТКЛОНЕНО';
            var actions = card.querySelector('.journal-actions');
            if (actions) actions.remove();
        }

        var pending = document.getElementById('pending-count');
        if (pending) {
            var left = Math.max(0, (parseInt(pending.textContent, 10) || 0) - 1);
            pending.textContent = String(left);
            var empty = document.getElementById('pending-empty');
            if (empty && left === 0) empty.hidden = false;
        }

        Aegis.toast(approved ? 'Запись одобрена: попала в общий журнал экспедиции.' : 'Запись отклонена.', approved ? '' : 'warn');
    });
})();
