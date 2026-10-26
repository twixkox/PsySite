// Общие скрипты админки. Лежат отдельным файлом, а не внутри страниц, —
// так политика безопасности (Content-Security-Policy) может запретить любые встроенные скрипты.
(function () {
    // Подтверждение для опасных кнопок: <button data-confirm="Точно удалить?">
    document.addEventListener('submit', function (e) {
        const btn = e.submitter;
        const text = (btn && btn.dataset.confirm) || e.target.dataset.confirm;
        if (text && !confirm(text)) e.preventDefault();
    });

    // Список, который сразу отправляет форму при выборе: <select data-autosubmit>
    document.querySelectorAll('select[data-autosubmit]').forEach(function (select) {
        select.addEventListener('change', function () { select.form.submit(); });
    });
})();
