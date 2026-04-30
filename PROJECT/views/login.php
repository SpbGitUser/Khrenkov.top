<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Вход – Khrenkov.top</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" />
    <style>
        body {
            background: #f8f9fa;
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
        }
        .login-box {
            background: #fff;
            padding: 2rem;
            border-radius: .5rem;
            box-shadow: 0 2px 8px rgba(0, 0, 0, .1);
            width: 100%;
            max-width: 360px;
        }
    </style>
</head>
<body>
<div class="login-box">
    <h4 class="mb-4 text-center">Khrenkov.top</h4>
    <?php if (!empty($error)): ?>
        <div class="alert alert-danger"><?= htmlspecialchars($error, ENT_QUOTES) ?></div>
    <?php endif; ?>
    <form method="post" action="/login">
        <?= csrf_field() ?>
        <input type="hidden" name="returnUrl" value="<?= htmlspecialchars($returnUrl ?? '/', ENT_QUOTES) ?>" />
        <div class="mb-3">
            <label class="form-label" for="password">Пароль</label>
            <input type="password" id="password" name="password" class="form-control" autofocus required />
        </div>
        <button type="submit" class="btn btn-primary w-100">Войти</button>
    </form>
</div>
</body>
</html>
