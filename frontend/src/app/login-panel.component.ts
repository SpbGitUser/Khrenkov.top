import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-login-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="login-panel view-enter" aria-labelledby="login-title">
      <div>
        <p class="eyebrow">Khrenkov.top</p>
        <h1 id="login-title">Вход в файловый менеджер</h1>
      </div>

      <form class="login-form" (ngSubmit)="submit()">
        <label for="password">Пароль</label>
        <input
          id="password"
          name="password"
          type="password"
          autocomplete="current-password"
          [(ngModel)]="password"
          [disabled]="loggingIn"
          autofocus
          required
        />
        <button type="submit" [disabled]="loggingIn || !password.trim()">
          {{ loggingIn ? 'Проверяю...' : 'Войти' }}
        </button>
        <p *ngIf="error" class="error">{{ error }}</p>
      </form>
    </section>
  `
})
export class LoginPanelComponent {
  @Input() loggingIn = false;
  @Input() error = '';

  @Output() readonly loginRequested = new EventEmitter<string>();

  password = '';

  submit(): void {
    const password = this.password.trim();
    if (!password || this.loggingIn) {
      return;
    }

    this.loginRequested.emit(password);
  }
}
