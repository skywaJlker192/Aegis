// Общие функции для всех страниц: CSRF-токен, fetch-обёртка с обработкой ошибок, тосты.
// Вынесено в window.Aegis, чтобы использовать из других скриптов.
(function () {
    'use strict';

    // Токен читается из <meta name="csrf-token"> и отправляется в заголовке X-CSRF-TOKEN
    function csrfToken() {
        var meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? meta.getAttribute('content') : '';
    }

    // Всплывающее сообщение в углу экрана. type: '' | 'warn' | 'crit'
    function toast(text, type) {
        var area = document.getElementById('toast-area');
        if (!area) return;
        var el = document.createElement('div');
        el.className = 'toast' + (type ? ' ' + type : '');
        el.textContent = text; // textContent, а не innerHTML — защита от XSS
        area.appendChild(el);
        setTimeout(function () { el.remove(); }, 6000);
    }

    // Превращает ответ сервера с ошибкой в понятный текст по статусу
    async function errorText(response) {
        if (response.status === 401) return 'ОТКАЗАНО В ДОСТУПЕ';
        if (response.status === 403) return 'ДОСТУП ЗАПРЕЩЁН: недостаточно прав';
        if (response.status === 400) {
            // 400 — либо ошибка валидации, либо нет CSRF-токена
            var detail = await readDetail(response);
            return 'ОШИБКА ЗАПРОСА (400)' + (detail ? ': ' + detail : '');
        }
        if (response.status >= 500) return 'СБОЙ СЕРВЕРА (' + response.status + '). Повторите позже.';
        var d = await readDetail(response);
        return d || ('Ошибка ' + response.status);
    }

    async function readDetail(response) {
        try {
            var data = await response.clone().json();
            return data.detail || data.title || '';
        } catch (e) {
            return '';
        }
    }

    // Универсальный AJAX-запрос. Всегда шлёт X-CSRF-TOKEN.
    // Возвращает { ok, status, data, error } и никогда не бросает исключение.
    async function request(method, url, body) {
        var headers = {
            'X-CSRF-TOKEN': csrfToken(),
            'X-Requested-With': 'XMLHttpRequest',
            'Accept': 'application/json'
        };
        var options = { method: method, headers: headers, credentials: 'same-origin' };
        if (body !== undefined) {
            headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }

        try {
            var response = await fetch(url, options);
            if (!response.ok) {
                return { ok: false, status: response.status, error: await errorText(response) };
            }
            var data = null;
            var text = await response.text();
            if (text) data = JSON.parse(text);
            return { ok: true, status: response.status, data: data };
        } catch (e) {
            return { ok: false, status: 0, error: 'НЕТ СВЯЗИ С ШТАБОМ' };
        }
    }

    window.Aegis = { csrfToken: csrfToken, toast: toast, request: request };
})();
