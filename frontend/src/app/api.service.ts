import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { AuthStatus, FileItem, ShareResponse } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  status(): Observable<AuthStatus> {
    return this.http.get<AuthStatus>('/api/auth/status', { withCredentials: true });
  }

  login(password: string): Observable<HttpResponse<string>> {
    return this.http.post('/api/auth/login', { password }, {
      withCredentials: true,
      observe: 'response',
      responseType: 'text'
    });
  }

  logout(): Observable<HttpResponse<string>> {
    return this.http.post('/api/auth/logout', {}, {
      withCredentials: true,
      observe: 'response',
      responseType: 'text'
    });
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
