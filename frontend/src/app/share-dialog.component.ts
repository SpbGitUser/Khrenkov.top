import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { FileItem } from './models';

@Component({
  selector: 'app-share-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="modal-backdrop" (click)="closeRequested.emit()">
      <section class="modal view-enter" (click)="$event.stopPropagation()" aria-labelledby="share-title" role="dialog" aria-modal="true">
        <header>
          <h2 id="share-title">Выдать ссылку</h2>
          <button class="close-button" type="button" (click)="closeRequested.emit()" aria-label="Закрыть">x</button>
        </header>

        <p class="share-file">{{ file.name }}</p>
        <label for="shareCount">Количество скачиваний</label>
        <div class="stepper">
          <button type="button" (click)="changeCount(currentCount - 1)" [disabled]="generating">-</button>
          <input
            id="shareCount"
            type="number"
            min="1"
            [ngModel]="currentCount"
            (ngModelChange)="changeCount($event)"
            [disabled]="generating"
          />
          <button type="button" (click)="changeCount(currentCount + 1)" [disabled]="generating">+</button>
        </div>

        <button type="button" (click)="generateRequested.emit()" [disabled]="generating || currentCount < 1">
          {{ generating ? 'Создаю...' : 'Сгенерировать' }}
        </button>

        <p *ngIf="error" class="error compact">{{ error }}</p>

        <div *ngIf="url" class="share-result">
          <input type="text" [value]="url" readonly />
          <button type="button" (click)="copyRequested.emit()">Копировать</button>
        </div>
      </section>
    </div>
  `
})
export class ShareDialogComponent {
  @Input({ required: true }) file!: FileItem;
  @Input() url = '';
  @Input() generating = false;
  @Input() error = '';

  @Input()
  set count(value: number) {
    this.currentCount = this.coerceCount(value);
  }

  @Output() readonly closeRequested = new EventEmitter<void>();
  @Output() readonly countChange = new EventEmitter<number>();
  @Output() readonly generateRequested = new EventEmitter<void>();
  @Output() readonly copyRequested = new EventEmitter<void>();

  currentCount = 1;

  changeCount(value: number | string): void {
    const next = this.coerceCount(value);
    this.currentCount = next;
    this.countChange.emit(next);
  }

  private coerceCount(value: number | string): number {
    return Math.max(1, Math.floor(Number(value) || 1));
  }
}
