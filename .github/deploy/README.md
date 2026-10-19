# VPS deployment

Pipeline находится в `.github/workflows/deploy.yml` и запускается на push в `main` или `master`, а также вручную через `workflow_dispatch`.

## GitHub Secrets

Добавьте в `Settings -> Secrets and variables -> Actions -> Secrets`:

- `VPS_HOST` - IP или hostname VPS.
- `VPS_USER` - пользователь Ubuntu с sudo-доступом.
- `VPS_SSH_KEY` - приватный SSH-ключ для подключения к VPS.
- `VPS_PORT` - SSH-порт, если не `22`.
- `SITE_PASSWORD` - пароль входа в приложение.

## GitHub Variables

Опционально добавьте в `Settings -> Secrets and variables -> Actions -> Variables`:

- `DEPLOY_PATH` - каталог на сервере, по умолчанию `/var/www/khrenkov-top`.
- `DOMAIN_NAME` - домен для nginx `server_name`, по умолчанию `_`.
- `BACKEND_URLS` - адрес Kestrel, по умолчанию `http://127.0.0.1:5000`.
- `BACKEND_PROXY_URL` - адрес backend для nginx proxy, по умолчанию `http://127.0.0.1:5000`.
- `ENABLE_HTTPS` - режим HTTPS: `auto` по умолчанию, `true` чтобы падать при ошибке выпуска сертификата, `false` чтобы оставить только HTTP.
- `LETSENCRYPT_EMAIL` - email для Let's Encrypt. Если не задан, certbot регистрируется без email.

## VPS

На сервере должен быть Ubuntu-пользователь с доступом по SSH и правом выполнять `sudo` для установки/перезапуска `nginx` и `systemd` service. .NET runtime на VPS не нужен: backend публикуется как self-contained `linux-x64`.

Если `DOMAIN_NAME` указывает на VPS и не является IP-адресом, deploy ставит `certbot`,
выпускает/обновляет сертификат Let's Encrypt и переключает nginx на HTTPS. Если
сертификат не удалось выпустить в режиме `auto`, сайт остаётся доступен по HTTP;
для строгого HTTPS задайте `ENABLE_HTTPS=true`.

После первого deploy будут созданы:

- `/var/www/khrenkov-top/current` - текущий релиз.
- `/var/www/khrenkov-top/shared/Uploads` - постоянные загруженные файлы.
- `/var/www/khrenkov-top/shared/Data/application.db` - постоянная SQLite-база метаданных и публичных ссылок; каталог доступен только пользователю сервиса и root.
- `/etc/systemd/system/khrenkov-top.service` - backend service.
- `/etc/nginx/sites-available/khrenkov-top` - nginx config.

## SQLite при деплое

Перед публикацией GitHub Actions запускает интеграционные тесты бэкенда и миграций. Отдельный сервер SQLite устанавливать не требуется: библиотека и нативный движок входят в self-contained публикацию .NET. Рабочие файлы БД и содержимое локального `Uploads` в релиз не включаются.

Deploy создаёт постоянный `shared/Data`, задаёт права каталога `700`, владельца `www-data` и передаёт в `/etc/khrenkov-top.env` настройку `Database__Path`, указывающую на `shared/Data/application.db`. База находится вне каталога пользовательских файлов и сохраняется при переключении релизов и очистке старых сборок.

При старте бэкенд создаёт базу, выполняет незавершённые миграции и регистрирует уже существующие файлы из постоянного `shared/Uploads`. Старые ссылки из `_shares.json` переносятся однократно с сохранением токенов и лимитов. Последующие старты сверяют метаданные файлов с диском, но не импортируют старые ссылки заново. После этого начинает отвечать `/api/auth/status`, который проверяет deploy.

Перед первым деплоем с SQLite сохраните резервную копию `shared/Uploads`, включая `_shares.json`. Если импорт не проходит, backend не начнёт обслуживать запросы, а проверка здоровья в deploy покажет ошибку. Посмотрите `journalctl -u khrenkov-top.service`, исправьте исходные данные и перезапустите сервис: незавершённый импорт повторится транзакционно. Уже применённую схему и runtime-базу удалять не нужно.

Создание и изменение схемы не откатываются автоматически при переключении на старый релиз. Для резервного копирования и восстановления БД вместе с файлами см. `backend/README.md`. Не запускайте старую JSON-версию приложения поверх новых данных без отдельного плана восстановления ссылок.
