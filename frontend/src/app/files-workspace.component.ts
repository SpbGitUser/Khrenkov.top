import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild
} from '@angular/core';

import { FileRow } from './models';

@Component({
  selector: 'app-files-workspace',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="workspace view-enter" aria-labelledby="files-title">
      <header class="topbar">
        <div>
          <p class="eyebrow">Khrenkov.top</p>
          <h1 id="files-title">Файлы</h1>
        </div>
        <button class="ghost-button" type="button" (click)="logoutRequested.emit()" [disabled]="loggingOut">
          {{ loggingOut ? 'Выхожу...' : 'Выйти' }}
        </button>
      </header>

      <div class="toolbar">
        <label class="upload-control" [class.disabled]="uploading">
          <input #fileInput type="file" (change)="selectFile($event)" [disabled]="uploading" />
          <span>{{ selectedFileName || 'Выбрать файл' }}</span>
        </label>
        <button type="button" (click)="uploadRequested.emit()" [disabled]="uploading || !selectedFileName">
          {{ uploading ? 'Загружаю...' : 'Загрузить' }}
        </button>
        <button class="ghost-button" type="button" (click)="refreshRequested.emit()" [disabled]="loadingFiles">
          {{ loadingFiles ? 'Обновляю...' : 'Обновить' }}
        </button>
        <span *ngIf="syncingFiles && !loadingFiles" class="sync-indicator">Сверяю список...</span>
      </div>

      <p *ngIf="error" class="error">{{ error }}</p>
      <p *ngIf="message" class="message">{{ message }}</p>

      <div class="table-wrap">
        <table *ngIf="files.length; else emptyState">
          <thead>
            <tr>
              <th>Имя файла</th>
              <th>Размер</th>
              <th>Изменен</th>
              <th class="actions">Действия</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let file of files; trackBy: trackFile">
              <td class="file-name">{{ file.name }}</td>
              <td>{{ file.sizeLabel }}</td>
              <td>{{ file.modifiedLabel }}</td>
              <td class="actions">
                <div class="file-action-group">
                  <a class="icon-button" [href]="file.downloadHref">Скачать</a>
                  <button class="icon-button" type="button" (click)="shareRequested.emit(file)" [disabled]="file.deleting">
                    Ссылка
                  </button>

                  <ng-container *ngIf="file.confirmingDelete; else deleteButton">
                    <button class="danger-button" type="button" (click)="deleteConfirmed.emit(file)" [disabled]="file.deleting">
                      Да
                    </button>
                    <button class="ghost-button" type="button" (click)="deleteCancelled.emit(file.name)" [disabled]="file.deleting">
                      Нет
                    </button>
                  </ng-container>

                  <ng-template #deleteButton>
                    <button class="danger-button" type="button" (click)="deleteRequested.emit(file)" [disabled]="file.deleting">
                      {{ file.deleting ? 'Удаляю...' : 'Удалить' }}
                    </button>
                  </ng-template>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <ng-template #emptyState>
        <div class="empty-state">{{ loadingFiles ? 'Загружаю список...' : 'Нет загруженных файлов.' }}</div>
      </ng-template>
    </section>
  `
})
export class FilesWorkspaceComponent implements OnChanges {
  @Input() files: readonly FileRow[] = [];
  @Input() selectedFileName = '';
  @Input() loadingFiles = false;
  @Input() syncingFiles = false;
  @Input() uploading = false;
  @Input() loggingOut = false;
  @Input() error = '';
  @Input() message = '';

  @Output() readonly logoutRequested = new EventEmitter<void>();
  @Output() readonly refreshRequested = new EventEmitter<void>();
  @Output() readonly fileSelected = new EventEmitter<File | null>();
  @Output() readonly uploadRequested = new EventEmitter<void>();
  @Output() readonly shareRequested = new EventEmitter<FileRow>();
  @Output() readonly deleteRequested = new EventEmitter<FileRow>();
  @Output() readonly deleteConfirmed = new EventEmitter<FileRow>();
  @Output() readonly deleteCancelled = new EventEmitter<string>();

  @ViewChild('fileInput') private fileInput?: ElementRef<HTMLInputElement>;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['selectedFileName'] && !this.selectedFileName && this.fileInput) {
      this.fileInput.nativeElement.value = '';
    }
  }

  selectFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.fileSelected.emit(input.files?.item(0) ?? null);
  }

  trackFile(_index: number, file: FileRow): string {
    return file.name;
  }
}
