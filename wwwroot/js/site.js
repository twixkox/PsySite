// Небольшой скрипт без библиотек: меню на телефоне, подписка без перезагрузки, cookie-баннер.
(function () {
    // ----- Мобильное меню -----
    const toggle = document.querySelector('.nav-toggle');
    if (toggle) {
        toggle.addEventListener('click', function () {
            const open = document.body.classList.toggle('nav-open');
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    }

    // ----- Подписка на рассылку без перезагрузки страницы -----
    document.querySelectorAll('form[data-ajax]').forEach(function (form) {
        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            const status = form.querySelector('.form-status');
            const button = form.querySelector('button[type=submit]');
            button.disabled = true;
            try {
                const res = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'fetch' }
                });
                const data = res.ok ? await res.json() : { ok: false, error: 'Слишком много попыток, попробуйте позже.' };
                status.textContent = data.ok ? 'Спасибо! Вы подписались.' : data.error;
                status.className = 'form-status ' + (data.ok ? 'ok' : 'error');
                if (data.ok) form.reset();
            } catch {
                status.textContent = 'Нет связи с сервером. Попробуйте ещё раз.';
                status.className = 'form-status error';
            } finally {
                button.disabled = false;
            }
        });
    });

    // ----- Cookie-баннер -----
    const banner = document.querySelector('.cookie-banner');
    if (banner) {
        let accepted = false;
        try { accepted = localStorage.getItem('cookies-ok') === '1'; } catch { }
        if (!accepted) banner.hidden = false;
        banner.querySelector('[data-cookie-ok]').addEventListener('click', function () {
            try { localStorage.setItem('cookies-ok', '1'); } catch { }
            banner.hidden = true;
        });
    }
})();
