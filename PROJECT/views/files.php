<?php
require __DIR__ . '/layout.php';
layout_head('Файлы');
// $files is provided by index.php
?>

<ul class="nav nav-tabs mb-4" id="fileTabs" role="tablist">
    <li class="nav-item" role="presentation">
        <button class="nav-link active" data-bs-toggle="tab" data-bs-target="#tab-download" type="button" role="tab">
            Скачать
        </button>
    </li>
    <li class="nav-item" role="presentation">
        <button class="nav-link" data-bs-toggle="tab" data-bs-target="#tab-upload" type="button" role="tab">
            Загрузить
        </button>
    </li>
</ul>

<div class="tab-content">

    <div class="tab-pane fade show active" id="tab-download" role="tabpanel">
        <?php if (empty($files)): ?>
            <p class="text-muted">Нет загруженных файлов.</p>
        <?php else: ?>
        <table class="table table-hover">
            <thead>
                <tr>
                    <th>Имя файла</th>
                    <th style="width:310px"></th>
                </tr>
            </thead>
            <tbody>
                <?php foreach ($files as $file): ?>
                <tr>
                    <td class="align-middle"><?= htmlspecialchars($file, ENT_QUOTES) ?></td>
                    <td class="text-end">
                        <a href="/file/download?name=<?= urlencode($file) ?>"
                           class="btn btn-sm btn-outline-primary me-1">Скачать</a>
                        <button type="button"
                                class="btn btn-sm btn-outline-success me-1 share-btn"
                                data-file="<?= htmlspecialchars($file, ENT_QUOTES) ?>">Выдать ссылку</button>
                        <form method="post" action="/file/delete" class="d-inline">
                            <?= csrf_field() ?>
                            <input type="hidden" name="fileName"
                                   value="<?= htmlspecialchars($file, ENT_QUOTES) ?>" />
                            <button type="submit" class="btn btn-sm btn-outline-danger"
                                    onclick="return confirm('Удалить <?= htmlspecialchars(addslashes($file), ENT_QUOTES) ?>?')">
                                Удалить
                            </button>
                        </form>
                    </td>
                </tr>
                <?php endforeach; ?>
            </tbody>
        </table>
        <?php endif; ?>
    </div>

    <div class="tab-pane fade" id="tab-upload" role="tabpanel">
        <form method="post" action="/file/upload" enctype="multipart/form-data" style="max-width:480px">
            <?= csrf_field() ?>
            <div class="mb-3">
                <label class="form-label">Выберите файл</label>
                <input type="file" name="file" class="form-control" required />
            </div>
            <button type="submit" class="btn btn-primary">Загрузить</button>
        </form>
    </div>

</div>

<div class="mt-4">
    <form method="post" action="/logout">
        <?= csrf_field() ?>
        <button type="submit" class="btn btn-outline-secondary btn-sm">Выход</button>
    </form>
</div>

<!-- Share Modal -->
<div class="modal fade" id="shareModal" tabindex="-1" aria-labelledby="shareModalLabel" aria-hidden="true">
    <div class="modal-dialog modal-sm">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title" id="shareModalLabel">Выдать ссылку</h5>
                <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
            </div>
            <div class="modal-body">
                <p class="mb-3">Файл: <strong id="shareFileName" class="text-break"></strong></p>
                <label class="form-label">Количество скачиваний:</label>
                <div class="input-group" style="width:150px">
                    <button class="btn btn-outline-secondary" type="button" id="decrementBtn">−</button>
                    <input type="number" id="shareCount" class="form-control text-center" value="1" min="1" />
                    <button class="btn btn-outline-secondary" type="button" id="incrementBtn">+</button>
                </div>
                <div id="shareResult" class="mt-3 d-none">
                    <label class="form-label">Ссылка:</label>
                    <div class="input-group">
                        <input type="text" id="shareUrl" class="form-control form-control-sm" readonly />
                        <button class="btn btn-outline-secondary btn-sm" type="button" id="copyBtn">Копировать</button>
                    </div>
                    <div id="copyConfirm" class="text-success small mt-1 d-none">Скопировано!</div>
                </div>
                <div id="shareError" class="alert alert-danger mt-3 d-none"></div>
            </div>
            <div class="modal-footer">
                <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Закрыть</button>
                <button type="button" class="btn btn-success" id="generateBtn">Сгенерировать</button>
            </div>
        </div>
    </div>
</div>

<?php layout_foot(<<<'JS'
<script>
(function () {
    const modal = new bootstrap.Modal(document.getElementById('shareModal'));
    let currentFile = null;

    document.querySelectorAll('.share-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            currentFile = btn.dataset.file;
            document.getElementById('shareFileName').textContent = currentFile;
            document.getElementById('shareCount').value = 1;
            document.getElementById('shareResult').classList.add('d-none');
            document.getElementById('shareError').classList.add('d-none');
            document.getElementById('copyConfirm').classList.add('d-none');
            modal.show();
        });
    });

    document.getElementById('decrementBtn').addEventListener('click', () => {
        const el = document.getElementById('shareCount');
        if (parseInt(el.value) > 1) el.value = parseInt(el.value) - 1;
    });

    document.getElementById('incrementBtn').addEventListener('click', () => {
        const el = document.getElementById('shareCount');
        el.value = parseInt(el.value) + 1;
    });

    document.getElementById('generateBtn').addEventListener('click', async () => {
        const count = parseInt(document.getElementById('shareCount').value);
        if (count < 1) return;

        const xsrf = document.querySelector('meta[name="xsrf-token"]').content;
        document.getElementById('shareError').classList.add('d-none');
        document.getElementById('shareResult').classList.add('d-none');

        try {
            const resp = await fetch('/api/files/share', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-XSRF-TOKEN': xsrf
                },
                body: JSON.stringify({ fileName: currentFile, maxDownloads: count })
            });

            if (!resp.ok) {
                const err = await resp.json().catch(() => ({ error: 'Ошибка сервера' }));
                const el = document.getElementById('shareError');
                el.textContent = err.error ?? 'Ошибка сервера';
                el.classList.remove('d-none');
                return;
            }

            const data = await resp.json();
            document.getElementById('shareUrl').value = location.origin + '/share/' + data.token;
            document.getElementById('shareResult').classList.remove('d-none');
        } catch {
            const el = document.getElementById('shareError');
            el.textContent = 'Ошибка соединения с сервером';
            el.classList.remove('d-none');
        }
    });

    document.getElementById('copyBtn').addEventListener('click', () => {
        navigator.clipboard.writeText(document.getElementById('shareUrl').value)
            .then(() => document.getElementById('copyConfirm').classList.remove('d-none'));
    });
})();
</script>
JS); ?>
