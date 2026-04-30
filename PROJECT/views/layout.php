<?php

function layout_head(string $title): void
{
    $csrf = csrf_token();
?>
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="xsrf-token" content="<?= htmlspecialchars($csrf, ENT_QUOTES) ?>" />
    <title><?= htmlspecialchars($title, ENT_QUOTES) ?> – Khrenkov.top</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" />
</head>
<body>
<header>
    <nav class="navbar navbar-expand-sm navbar-light bg-white border-bottom mb-3">
        <div class="container-fluid">
            <a class="navbar-brand" href="/">Khrenkov.top</a>
            <div class="collapse navbar-collapse">
                <ul class="navbar-nav me-auto">
                    <li class="nav-item">
                        <a class="nav-link" href="/">Файлы</a>
                    </li>
                    <li class="nav-item">
                        <a class="nav-link" href="/swagger" target="_blank">API</a>
                    </li>
                </ul>
            </div>
        </div>
    </nav>
</header>
<div class="container">
    <main class="pb-3">
<?php
}

function layout_foot(string $extraScripts = ''): void
{
?>
    </main>
</div>
<footer class="border-top footer text-muted mt-4 py-2">
    <div class="container">&copy; <?= date('Y') ?> – Khrenkov.top</div>
</footer>
<script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
<?php if ($extraScripts !== '') { echo $extraScripts; } ?>
</body>
</html>
<?php
}
