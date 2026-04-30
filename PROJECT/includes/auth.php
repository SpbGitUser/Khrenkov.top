<?php

function auth_start(): void
{
    if (session_status() === PHP_SESSION_NONE) {
        session_set_cookie_params([
            'httponly' => true,
            'samesite' => 'Lax',
        ]);
        session_start();
    }
}

function auth_check(): bool
{
    auth_start();
    return !empty($_SESSION['auth']);
}

function auth_require(): void
{
    if (!auth_check()) {
        $return = urlencode($_SERVER['REQUEST_URI'] ?? '/');
        header('Location: /login?returnUrl=' . $return);
        exit;
    }
}

function auth_login(): void
{
    auth_start();
    session_regenerate_id(true);
    $_SESSION['auth'] = true;
}

function auth_logout(): void
{
    auth_start();
    $_SESSION = [];
    session_destroy();
}

function csrf_token(): string
{
    auth_start();
    if (empty($_SESSION['csrf'])) {
        $_SESSION['csrf'] = bin2hex(random_bytes(32));
    }
    return $_SESSION['csrf'];
}

function csrf_field(): string
{
    return '<input type="hidden" name="csrf_token" value="'
        . htmlspecialchars(csrf_token(), ENT_QUOTES) . '">';
}

function csrf_verify(): bool
{
    auth_start();
    $expected = $_SESSION['csrf'] ?? '';
    $provided = $_POST['csrf_token']
        ?? $_SERVER['HTTP_X_XSRF_TOKEN']
        ?? '';
    return $expected !== '' && hash_equals($expected, $provided);
}
