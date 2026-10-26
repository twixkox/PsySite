// Визуальный редактор текста (Quill) для полей <textarea data-editor>
// и автоматическое заполнение адреса страницы по заголовку.
(function () {
    // ----- Редактор -----
    document.querySelectorAll('textarea[data-editor]').forEach(function (textarea) {
        if (typeof Quill === 'undefined') return; // CDN недоступен — остаётся обычное поле с HTML

        const wrap = document.createElement('div');
        wrap.className = 'editor-wrap';
        const editorEl = document.createElement('div');
        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'link-btn editor-toggle';
        toggle.textContent = 'Редактировать HTML';
        wrap.appendChild(editorEl);
        textarea.parentNode.insertBefore(toggle, textarea);
        textarea.parentNode.insertBefore(wrap, textarea);
        textarea.hidden = true;

        const quill = new Quill(editorEl, {
            theme: 'snow',
            modules: {
                toolbar: [
                    [{ header: [2, 3, false] }],
                    ['bold', 'italic', 'link', 'blockquote'],
                    [{ list: 'ordered' }, { list: 'bullet' }],
                    ['clean']
                ]
            }
        });
        quill.clipboard.dangerouslyPasteHTML(textarea.value || '');

        let htmlMode = false;
        const toHtml = () => quill.getSemanticHTML().replace(/&nbsp;/g, ' ');

        toggle.addEventListener('click', function () {
            htmlMode = !htmlMode;
            if (htmlMode) {
                textarea.value = toHtml();
                wrap.hidden = true;
                textarea.hidden = false;
                toggle.textContent = 'Визуальный редактор';
            } else {
                quill.clipboard.dangerouslyPasteHTML(textarea.value);
                wrap.hidden = false;
                textarea.hidden = true;
                toggle.textContent = 'Редактировать HTML';
            }
        });

        textarea.form.addEventListener('submit', function () {
            if (!htmlMode) textarea.value = toHtml();
        });
    });

    // ----- Адрес страницы из заголовка: «Внутренний дом» → vnutrennij-dom -----
    const map = {
        а: 'a', б: 'b', в: 'v', г: 'g', д: 'd', е: 'e', ё: 'e', ж: 'zh', з: 'z', и: 'i', й: 'j', к: 'k',
        л: 'l', м: 'm', н: 'n', о: 'o', п: 'p', р: 'r', с: 's', т: 't', у: 'u', ф: 'f', х: 'h', ц: 'c',
        ч: 'ch', ш: 'sh', щ: 'sch', ъ: '', ы: 'y', ь: '', э: 'e', ю: 'yu', я: 'ya'
    };
    const slugify = s => s.toLowerCase().split('').map(c => map[c] ?? c).join('')
        .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 80);

    const source = document.querySelector('[data-slug-source]');
    const target = document.querySelector('[data-slug-target]');
    if (source && target) {
        let auto = target.dataset.slugTarget === 'auto' || target.value === '';
        target.addEventListener('input', () => { auto = false; });
        source.addEventListener('input', () => { if (auto) target.value = slugify(source.value); });
    }
})();
