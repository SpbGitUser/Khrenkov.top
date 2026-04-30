<?php
declare(strict_types=1);

require __DIR__ . '/includes/auth.php';
require __DIR__ . '/includes/shares.php';

$config = require __DIR__ . '/config.php';

foreach ([$config['upload_dir'], $config['data_dir']] as $dir) {
    if (!is_dir($dir)) {
        mkdir($dir, 0755, true);
    }
}

$shares = new ShareLinkService($config['data_dir']);
$method = strtoupper($_SERVER['REQUEST_METHOD']);
$uri    = rtrim(parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH), '/') ?: '/';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function json_out(mixed $data, int $status = 200): void
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($data, JSON_UNESCAPED_UNICODE);
    exit;
}

function send_file(string $path, string $name): void
{
    header('Content-Type: application/octet-stream');
    header('Content-Disposition: attachment; filename="' . rawurlencode(basename($name)) . '"');
    header('Content-Length: ' . filesize($path));
    readfile($path);
    exit;
}

function safe_name(string $name): string
{
    $base = basename($name);
    // Prevent hidden/service files
    if ($base === '' || $base[0] === '.') {
        return '';
    }
    return $base;
}

function upload_dir(): string
{
    global $config;
    return $config['upload_dir'];
}

function list_files(): array
{
    $dir  = upload_dir();
    $list = [];
    foreach (scandir($dir) as $f) {
        if ($f === '.' || $f === '..') {
            continue;
        }
        if (!is_file($dir . '/' . $f)) {
            continue;
        }
        $list[] = $f;
    }
    sort($list);
    return $list;
}

function safe_return_url(string $url): string
{
    if (!str_starts_with($url, '/') || str_starts_with($url, '//')) {
        return '/';
    }
    return $url;
}

// ─── PUBLIC: share download ───────────────────────────────────────────────────

if (preg_match('#^/share/([a-fA-F0-9]+)$#', $uri, $m)) {
    $fileName = $shares->consume($m[1]);
    if ($fileName === null) {
        http_response_code(410);
        header('Content-Type: text/html; charset=utf-8');
        echo '<!DOCTYPE html><html><body><p>Ссылка недействительна или лимит скачиваний исчерпан.</p></body></html>';
        exit;
    }
    $filePath = upload_dir() . '/' . safe_name($fileName);
    if (!file_exists($filePath)) {
        http_response_code(404);
        echo 'Файл не найден.';
        exit;
    }
    send_file($filePath, basename($fileName));
}

// ─── LOGIN ────────────────────────────────────────────────────────────────────

if ($uri === '/login') {
    auth_start();
    $error     = null;
    $returnUrl = safe_return_url($_GET['returnUrl'] ?? '/');

    if ($method === 'POST') {
        if (!csrf_verify()) {
            http_response_code(403);
            exit;
        }
        $returnUrl = safe_return_url($_POST['returnUrl'] ?? '/');
        if (hash_equals($config['password'], $_POST['password'] ?? '')) {
            auth_login();
            header('Location: ' . $returnUrl);
            exit;
        }
        $error = 'Неверный пароль';
    }

    require __DIR__ . '/views/login.php';
    exit;
}

// ─── SWAGGER UI ───────────────────────────────────────────────────────────────

if ($uri === '/swagger') {
    auth_require();
    require __DIR__ . '/views/swagger.php';
    exit;
}

if ($uri === '/swagger.json') {
    auth_require();
    $scheme  = (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off') ? 'https' : 'http';
    $baseUrl = $scheme . '://' . $_SERVER['HTTP_HOST'];
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode([
        'openapi' => '3.0.0',
        'info'    => ['title' => 'Khrenkov.top API', 'version' => 'v1'],
        'servers' => [['url' => $baseUrl]],
        'paths'   => [
            '/api/files' => [
                'get' => [
                    'summary'   => 'Список файлов',
                    'tags'      => ['Files'],
                    'responses' => ['200' => ['description' => 'Массив файлов']],
                ],
            ],
            '/api/files/upload' => [
                'post' => [
                    'summary'     => 'Загрузить файл',
                    'tags'        => ['Files'],
                    'requestBody' => [
                        'required' => true,
                        'content'  => [
                            'multipart/form-data' => [
                                'schema' => [
                                    'type'       => 'object',
                                    'properties' => [
                                        'file' => ['type' => 'string', 'format' => 'binary'],
                                    ],
                                    'required' => ['file'],
                                ],
                            ],
                        ],
                    ],
                    'responses' => ['200' => ['description' => 'OK']],
                ],
            ],
            '/api/files/download/{fileName}' => [
                'get' => [
                    'summary'    => 'Скачать файл',
                    'tags'       => ['Files'],
                    'parameters' => [
                        ['name' => 'fileName', 'in' => 'path', 'required' => true, 'schema' => ['type' => 'string']],
                    ],
                    'responses' => [
                        '200' => ['description' => 'Файл'],
                        '404' => ['description' => 'Не найден'],
                    ],
                ],
            ],
            '/api/files/share' => [
                'post' => [
                    'summary'     => 'Создать публичную ссылку',
                    'tags'        => ['Files'],
                    'requestBody' => [
                        'required' => true,
                        'content'  => [
                            'application/json' => [
                                'schema' => [
                                    'type'       => 'object',
                                    'properties' => [
                                        'fileName'     => ['type' => 'string'],
                                        'maxDownloads' => ['type' => 'integer', 'minimum' => 1],
                                    ],
                                    'required' => ['fileName', 'maxDownloads'],
                                ],
                            ],
                        ],
                    ],
                    'responses' => [
                        '200' => [
                            'description' => 'Токен ссылки',
                            'content'     => [
                                'application/json' => [
                                    'schema' => ['type' => 'object', 'properties' => ['token' => ['type' => 'string']]],
                                ],
                            ],
                        ],
                    ],
                ],
            ],
            '/api/files/{fileName}' => [
                'delete' => [
                    'summary'    => 'Удалить файл',
                    'tags'       => ['Files'],
                    'parameters' => [
                        ['name' => 'fileName', 'in' => 'path', 'required' => true, 'schema' => ['type' => 'string']],
                    ],
                    'responses' => [
                        '204' => ['description' => 'Удалён'],
                        '404' => ['description' => 'Не найден'],
                    ],
                ],
            ],
        ],
        'components' => [
            'securitySchemes' => [
                'cookieAuth' => ['type' => 'apiKey', 'in' => 'cookie', 'name' => session_name()],
            ],
        ],
        'security' => [['cookieAuth' => []]],
    ], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
    exit;
}

// ─── ALL BELOW REQUIRES AUTH ──────────────────────────────────────────────────

auth_require();

// LOGOUT
if ($uri === '/logout' && $method === 'POST') {
    if (csrf_verify()) {
        auth_logout();
    }
    header('Location: /login');
    exit;
}

// ─── API ─────────────────────────────────────────────────────────────────────

if (str_starts_with($uri, '/api/')) {
    // GET /api/files
    if ($uri === '/api/files' && $method === 'GET') {
        $files = array_map(fn($f) => [
            'name'     => $f,
            'size'     => filesize(upload_dir() . '/' . $f),
            'modified' => date('c', (int) filemtime(upload_dir() . '/' . $f)),
        ], list_files());
        json_out($files);
    }

    // POST /api/files/upload
    if ($uri === '/api/files/upload' && $method === 'POST') {
        if (empty($_FILES['file'])) {
            json_out(['error' => 'Файл не выбран'], 400);
        }
        $file = $_FILES['file'];
        if ($file['error'] !== UPLOAD_ERR_OK) {
            json_out(['error' => 'Ошибка загрузки: ' . $file['error']], 400);
        }
        $name = safe_name($file['name']);
        if ($name === '') {
            json_out(['error' => 'Недопустимое имя файла'], 400);
        }
        if (!move_uploaded_file($file['tmp_name'], upload_dir() . '/' . $name)) {
            json_out(['error' => 'Не удалось сохранить файл'], 500);
        }
        json_out(['name' => $name, 'size' => $file['size']]);
    }

    // POST /api/files/share
    if ($uri === '/api/files/share' && $method === 'POST') {
        if (!csrf_verify()) {
            json_out(['error' => 'CSRF mismatch'], 403);
        }
        $body         = json_decode(file_get_contents('php://input'), true) ?? [];
        $fileName     = $body['fileName'] ?? '';
        $maxDownloads = (int) ($body['maxDownloads'] ?? 0);
        if ($fileName === '' || $maxDownloads < 1) {
            json_out(['error' => 'Некорректные параметры'], 400);
        }
        $path = upload_dir() . '/' . safe_name($fileName);
        if (!file_exists($path)) {
            json_out(['error' => 'Файл не найден'], 404);
        }
        $token = $shares->create($fileName, $maxDownloads);
        json_out(['token' => $token]);
    }

    // GET /api/files/download/{fileName}
    if (preg_match('#^/api/files/download/(.+)$#', $uri, $m) && $method === 'GET') {
        $name = safe_name(rawurldecode($m[1]));
        $path = upload_dir() . '/' . $name;
        if ($name === '' || !file_exists($path)) {
            json_out(['error' => 'Файл не найден'], 404);
        }
        send_file($path, $name);
    }

    // DELETE /api/files/{fileName}
    if (preg_match('#^/api/files/([^/]+)$#', $uri, $m) && $method === 'DELETE') {
        $name = safe_name(rawurldecode($m[1]));
        $path = upload_dir() . '/' . $name;
        if ($name === '' || !file_exists($path)) {
            json_out(['error' => 'Файл не найден'], 404);
        }
        unlink($path);
        http_response_code(204);
        exit;
    }

    json_out(['error' => 'Not found'], 404);
}

// ─── Form-based file operations ───────────────────────────────────────────────

if ($uri === '/file/upload' && $method === 'POST') {
    if (!csrf_verify()) {
        http_response_code(403);
        exit;
    }
    if (!empty($_FILES['file']) && $_FILES['file']['error'] === UPLOAD_ERR_OK) {
        $name = safe_name($_FILES['file']['name']);
        if ($name !== '') {
            move_uploaded_file($_FILES['file']['tmp_name'], upload_dir() . '/' . $name);
        }
    }
    header('Location: /');
    exit;
}

if ($uri === '/file/delete' && $method === 'POST') {
    if (!csrf_verify()) {
        http_response_code(403);
        exit;
    }
    $name = safe_name($_POST['fileName'] ?? '');
    $path = upload_dir() . '/' . $name;
    if ($name !== '' && file_exists($path)) {
        unlink($path);
    }
    header('Location: /');
    exit;
}

if ($uri === '/file/download' && $method === 'GET') {
    $name = safe_name($_GET['name'] ?? '');
    $path = upload_dir() . '/' . $name;
    if ($name === '' || !file_exists($path)) {
        http_response_code(404);
        echo 'Файл не найден.';
        exit;
    }
    send_file($path, $name);
}

// ─── INDEX ────────────────────────────────────────────────────────────────────

$files = list_files();
require __DIR__ . '/views/files.php';
