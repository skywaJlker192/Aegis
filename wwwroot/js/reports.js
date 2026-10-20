// Быстрое одобрение отчёта (модератор): AJAX PUT, карточка зеленеет без перезагрузки.
(function () {
    'use strict';

    document.addEventListener('click', async function (event) {
        var btn = event.target.closest('.btn-approve');
        if (!btn) return;

        var id = btn.getAttribute('data-report-id');
        btn.disabled = true;

        var res = await Aegis.request('PUT', '/ajax/reports/' + id + '/approve');

        if (!res.ok) {
            btn.disabled = false;
            Aegis.toast(res.error, 'crit');
            return;
        }

        var card = document.getElementById('report-' + id);
        if (card) {
            card.classList.remove('report-pending');
            card.classList.add('report-approved');
            var state = card.querySelector('.report-state');
            if (state) state.textContent = 'ПРИНЯТО';
        }
        btn.remove();
        Aegis.toast('Отчёт №' + id + ' принят.');
    });
})();
