import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';

const API = '/api';

@Injectable({ providedIn: 'root' })
export class UrlService {
  constructor(private http: HttpClient) {}

  createUrl(longUrl: string, customAlias?: string, expiresAt?: string) {
    const body: any = { longUrl };
    if (customAlias) body.customAlias = customAlias;
    if (expiresAt) body.expiresAt = expiresAt;
    return this.http.post<any>(`${API}/urls`, body);
  }

  listUrls(page = 1, pageSize = 20) {
    return this.http.get<any>(`${API}/urls?page=${page}&pageSize=${pageSize}`);
  }

  deleteUrl(id: string) {
    return this.http.delete(`${API}/urls/${id}`);
  }

  getStats(id: string, from?: string, to?: string, bucket = 'hour') {
    let url = `${API}/urls/${id}/stats?bucket=${bucket}`;
    if (from) url += `&from=${from}`;
    if (to) url += `&to=${to}`;
    return this.http.get<any>(url);
  }

  getReferrers(id: string, limit = 10) {
    return this.http.get<any>(`${API}/urls/${id}/referrers?limit=${limit}`);
  }
}
