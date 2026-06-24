import { CommonModule } from '@angular/common';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { Component, Injectable, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { bootstrapApplication } from '@angular/platform-browser';
import { Observable } from 'rxjs';

interface AuthStatus {
  authenticated: boolean;
}

interface FileItem {
  name: string;
  size: number;
  modified: string;
}

interface ShareResponse {
  token: string;
}

@Injectable({ providedIn: 'root' })
class ApiService {
  private readonly http = inject(HttpClient);

  status(): Observable<AuthStatus> {
    return this.http.get<AuthStatus>('/api/auth/status', { withCredentials: true });
  }

  login(password: string): Observable<AuthStatus> {
    return this.http.post<AuthStatus>('/api/auth/login', { password }, { withCredentials: true });
  }

  logout(): Observable<AuthStatus> {
    return this.http.post<AuthStatus>('/api/auth/logout', {}, { withCredentials: true });
  }

  listFiles(): Observable<FileItem[]> {
    return this.http.get<FileItem[]>('/api/files', { withCredentials: true });
  }

  upload(file: File): Observable<FileItem> {
    const body = new FormData();
    body.append('file', file);
    return this.http.post<FileItem>('/api/files/upload', body, { withCredentials: true });
  }

  deleteFile(fileName: string): Observable<void> {
    return this.http.delete<void>(`/api/files/${encodeURIComponent(fileName)}`, { withCredentials: true });
  }

  createShare(fileName: string, maxDownloads: number): Observable<ShareResponse> {
    return this.http.post<ShareResponse>(
      '/api/files/share',
      { fileName, maxDownloads },
      { withCredentials: true }
    );
  }
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <main class="shell">
      <section *ngIf="!authenticated" class="login-panel" aria-labelledby="login-title">
        <div>
          <p class="eyebrow">Khrenkov.top</p>
          <h1 id="login-title">Вход в файловый менеджер</h1>
        </div>

        <form class="login-form" (ngSubmit)="login()">
          <label for="password">Пароль</label>
          <input
            id="password"
            name="password"
            type="password"
            autocomplete="current-password"
            [(ngModel)]="password"
            [disabled]="busy"
            autofocus
            required
          />
          <button type="submit" [disabled]="busy || !password.trim()">
            {{ busy ? 'Проверяю...' : 'Войти' }}
          </button>
          <p *ngIf="error" class="error">{{ error }}</p>
        </form>
      </section>

      <section *ngIf="authenticated" class="workspace" aria-labelledby="files-title">
        <header class="topbar">
          <div>
            <p class="eyebrow">Khrenkov.top</p>
            <h1 id="files-title">Файлы</h1>
          </div>
          <button class="ghost-button" type="button" (click)="logout()" [disabled]="busy">Выйти</button>
        </header>

        <div class="toolbar">
          <label class="upload-control">
            <input type="file" (change)="onFileSelected($event)" [disabled]="busy" />
            <span>{{ selectedFile?.name || 'Выбрать файл' }}</span>
          </label>
          <button type="button" (click)="upload()" [disabled]="busy || !selectedFile">
            {{ busy ? 'Загружаю...' : 'Загрузить' }}
          </button>
          <button class="ghost-button" type="button" (click)="loadFiles()" [disabled]="busy">Обновить</button>
        </div>

        <p *ngIf="error" class="error">{{ error }}</p>
        <p *ngIf="message" class="message">{{ message }}</p>

        <div class="table-wrap">
          <table *ngIf="files.length; else emptyState">
            <thead>
              <tr>
                <th>Имя файла</th>
                <th>Размер</th>
                <th>Изменён</th>
                <th class="actions">Действия</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let file of files">
                <td class="file-name">{{ file.name }}</td>
                <td>{{ formatBytes(file.size) }}</td>
                <td>{{ file.modified | date: 'dd.MM.yyyy HH:mm' }}</td>
                <td class="actions">
                  <a class="icon-button" [href]="downloadUrl(file.name)">Скачать</a>
                  <button class="icon-button" type="button" (click)="openShare(file)">Ссылка</button>
                  <button class="danger-button" type="button" (click)="deleteFile(file)" [disabled]="busy">Удалить</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <ng-template #emptyState>
          <div class="empty-state">Нет загруженных файлов.</div>
        </ng-template>
      </section>

      <div *ngIf="shareFile" class="modal-backdrop" (click)="closeShare()">
        <section class="modal" (click)="$event.stopPropagation()" aria-labelledby="share-title">
          <header>
            <h2 id="share-title">Выдать ссылку</h2>
            <button class="close-button" type="button" (click)="closeShare()" aria-label="Закрыть">x</button>
          </header>

          <p class="share-file">{{ shareFile.name }}</p>
          <label for="shareCount">Количество скачиваний</label>
          <div class="stepper">
            <button type="button" (click)="decrementShareCount()">-</button>
            <input id="shareCount" type="number" min="1" [(ngModel)]="shareCount" />
            <button type="button" (click)="shareCount = shareCount + 1">+</button>
          </div>

          <button type="button" (click)="generateShare()" [disabled]="busy || shareCount < 1">
            Сгенерировать
          </button>

          <div *ngIf="shareUrl" class="share-result">
            <input type="text" [value]="shareUrl" readonly />
            <button type="button" (click)="copyShareUrl()">Копировать</button>
          </div>
        </section>
      </div>
    </main>
  `
})
class AppComponent implements OnInit {
  private readonly api = inject(ApiService);

  authenticated = false;
  busy = false;
  password = '';
  files: FileItem[] = [];
  selectedFile: File | null = null;
  shareFile: FileItem | null = null;
  shareCount = 1;
  shareUrl = '';
  error = '';
  message = '';

  ngOnInit(): void {
    this.api.status().subscribe({
      next: status => {
        this.authenticated = status.authenticated;
        if (status.authenticated) {
          this.loadFiles();
        }
      },
      error: () => {
        this.authenticated = false;
      }
    });
  }

  login(): void {
    this.run(() =>
      this.api.login(this.password).subscribe({
        next: () => {
          this.authenticated = true;
          this.password = '';
          this.loadFiles();
        },
        error: () => {
          this.error = 'Неверный пароль';
          this.busy = false;
        }
      })
    );
  }

  logout(): void {
    this.run(() =>
      this.api.logout().subscribe({
        next: () => {
          this.authenticated = false;
          this.files = [];
          this.busy = false;
        },
        error: () => {
          this.error = 'Не удалось выйти';
          this.busy = false;
        }
      })
    );
  }

  loadFiles(): void {
    this.run(() =>
      this.api.listFiles().subscribe({
        next: files => {
          this.files = files;
          this.busy = false;
        },
        error: () => {
          this.error = 'Не удалось загрузить список файлов';
          this.busy = false;
        }
      })
    );
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.item(0) ?? null;
  }

  upload(): void {
    if (!this.selectedFile) {
      return;
    }

    this.run(() =>
      this.api.upload(this.selectedFile as File).subscribe({
        next: () => {
          this.message = 'Файл загружен';
          this.selectedFile = null;
          this.loadFiles();
        },
        error: () => {
          this.error = 'Не удалось загрузить файл';
          this.busy = false;
        }
      })
    );
  }

  deleteFile(file: FileItem): void {
    if (!confirm(`Удалить ${file.name}?`)) {
      return;
    }

    this.run(() =>
      this.api.deleteFile(file.name).subscribe({
        next: () => {
          this.message = 'Файл удалён';
          this.loadFiles();
        },
        error: () => {
          this.error = 'Не удалось удалить файл';
          this.busy = false;
        }
      })
    );
  }

  openShare(file: FileItem): void {
    this.shareFile = file;
    this.shareCount = 1;
    this.shareUrl = '';
  }

  closeShare(): void {
    this.shareFile = null;
  }

  decrementShareCount(): void {
    this.shareCount = Math.max(1, this.shareCount - 1);
  }

  generateShare(): void {
    if (!this.shareFile) {
      return;
    }

    this.run(() =>
      this.api.createShare(this.shareFile!.name, this.shareCount).subscribe({
        next: response => {
          this.shareUrl = `${window.location.origin}/share/${response.token}`;
          this.busy = false;
        },
        error: () => {
          this.error = 'Не удалось создать ссылку';
          this.busy = false;
        }
      })
    );
  }

  copyShareUrl(): void {
    void navigator.clipboard.writeText(this.shareUrl);
    this.message = 'Ссылка скопирована';
  }

  downloadUrl(fileName: string): string {
    return `/api/files/download/${encodeURIComponent(fileName)}`;
  }

  formatBytes(size: number): string {
    if (size < 1024) {
      return `${size} Б`;
    }

    const units = ['КБ', 'МБ', 'ГБ'];
    let value = size / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit++;
    }
    return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
  }

  private run(action: () => void): void {
    this.busy = true;
    this.error = '';
    this.message = '';
    action();
  }
}

bootstrapApplication(AppComponent, {
  providers: [provideHttpClient()]
}).catch(error => console.error(error));
