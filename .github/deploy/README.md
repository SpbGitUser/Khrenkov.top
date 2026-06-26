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
- `/etc/systemd/system/khrenkov-top.service` - backend service.
- `/etc/nginx/sites-available/khrenkov-top` - nginx config.
