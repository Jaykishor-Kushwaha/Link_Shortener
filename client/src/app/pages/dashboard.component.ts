import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { UrlService } from '../services/url.service';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="container mx-auto px-4 py-8 max-w-6xl">
      <div class="flex flex-col md:flex-row md:items-center md:justify-between gap-4 mb-8">
        <div>
          <h2 class="text-3xl font-extrabold text-white tracking-tight">Dashboard</h2>
          <p class="text-sm text-gray-400 mt-1">Shorten, monitor, and optimize your links in real-time.</p>
        </div>
      </div>

      <!-- Create URL Box -->
      <div class="bg-[#1a1d27]/70 backdrop-blur-xl border border-[#2a2d3a] rounded-2xl p-6 md:p-8 shadow-2xl mb-8">
        <h3 class="text-lg font-bold text-white mb-4 flex items-center gap-2">
          <span class="text-indigo-400">🪄</span> Shorten a new link
        </h3>
        
        <div class="grid grid-cols-1 lg:grid-cols-12 gap-4">
          <!-- Long URL Input -->
          <div class="lg:col-span-6">
            <label class="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Destination URL</label>
            <input [(ngModel)]="longUrl" placeholder="https://example.com/very/long/url-goes-here" 
                   class="w-full bg-[#0f1117] border border-[#2a2d3a] rounded-xl px-4 py-3 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 transition-all">
          </div>
          
          <!-- Custom Alias Input -->
          <div class="lg:col-span-3">
            <label class="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Custom Alias (Optional)</label>
            <input [(ngModel)]="customAlias" placeholder="e.g. my-link" 
                   class="w-full bg-[#0f1117] border border-[#2a2d3a] rounded-xl px-4 py-3 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 transition-all">
          </div>

          <!-- Create Button -->
          <div class="lg:col-span-3 flex items-end">
            <button (click)="createUrl()" 
                    class="w-full bg-gradient-to-r from-indigo-500 to-purple-600 hover:from-indigo-600 hover:to-purple-700 text-white font-semibold py-3 px-4 rounded-xl shadow-lg hover:shadow-indigo-500/20 active:scale-[0.99] transition-all duration-150">
              Generate Short URL
            </button>
          </div>
        </div>

        <!-- Feedback Messages -->
        <div *ngIf="createError" class="mt-4 bg-red-500/10 border border-red-500/20 rounded-xl p-3 text-sm text-red-400 flex items-center gap-2">
          <span class="text-lg">⚠️</span>
          <span>{{ createError }}</span>
        </div>

        <div *ngIf="createdUrl" class="mt-4 bg-green-500/10 border border-green-500/20 rounded-xl p-4 text-sm text-green-400 flex flex-col md:flex-row md:items-center justify-between gap-3">
          <div class="flex items-center gap-2">
            <span class="text-lg">🎉</span>
            <span class="font-medium text-gray-200">Link shortened successfully!</span>
          </div>
          <div class="flex items-center gap-3">
            <a [href]="createdUrl" target="_blank" class="font-mono text-indigo-400 hover:text-indigo-300 underline font-semibold">{{ createdUrl }}</a>
            <button (click)="copyToClipboard(createdUrl)" class="bg-[#2a2d3a] hover:bg-[#3b3e50] text-gray-200 px-3 py-1.5 rounded-lg text-xs font-semibold transition-colors">
              Copy
            </button>
          </div>
        </div>
      </div>

      <!-- Links List Box -->
      <div class="bg-[#1a1d27]/70 backdrop-blur-xl border border-[#2a2d3a] rounded-2xl p-6 shadow-2xl">
        <h3 class="text-lg font-bold text-white mb-6 flex items-center gap-2">
          <span class="text-indigo-400">📁</span> Your Active Links
        </h3>

        <!-- Responsive Table / List -->
        <div class="overflow-x-auto" *ngIf="urls.length > 0">
          <table class="w-full text-left border-collapse">
            <thead>
              <tr class="border-b border-[#2a2d3a]">
                <th class="pb-3 text-xs font-semibold text-gray-400 uppercase tracking-wider">Short Link</th>
                <th class="pb-3 text-xs font-semibold text-gray-400 uppercase tracking-wider hidden md:table-cell">Destination</th>
                <th class="pb-3 text-xs font-semibold text-gray-400 uppercase tracking-wider text-center">Clicks</th>
                <th class="pb-3 text-xs font-semibold text-gray-400 uppercase tracking-wider text-right">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-[#2a2d3a]">
              <tr *ngFor="let url of urls" class="hover:bg-[#202330]/40 transition-colors group">
                <!-- Short Link -->
                <td class="py-4 font-semibold text-white">
                  <a [href]="url.shortUrl" target="_blank" class="text-indigo-400 hover:text-indigo-300 font-mono tracking-wide transition-colors">
                    /{{ url.shortCode }}
                  </a>
                </td>
                <!-- Long URL -->
                <td class="py-4 text-gray-300 hidden md:table-cell max-w-[320px] truncate pr-4">
                  {{ url.longUrl }}
                </td>
                <!-- Clicks -->
                <td class="py-4 text-center">
                  <span class="inline-flex items-center justify-center px-3 py-1 rounded-full text-xs font-bold bg-indigo-500/10 border border-indigo-500/20 text-indigo-400">
                    {{ url.clickCount | number }}
                  </span>
                </td>
                <!-- Actions -->
                <td class="py-4 text-right">
                  <div class="inline-flex items-center gap-2">
                    <a [routerLink]="['/stats', url.id]" 
                       class="text-xs bg-[#2a2d3a] hover:bg-[#3b3e50] text-gray-200 px-3 py-2 rounded-lg font-semibold transition-colors">
                      📊 Analytics
                    </a>
                    <button (click)="deleteUrl(url.id)" 
                            class="text-xs bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 px-3 py-2 rounded-lg font-semibold transition-colors">
                      Delete
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Pagination -->
        <div *ngIf="totalPages > 1 && urls.length > 0" class="mt-6 flex items-center justify-center gap-4">
          <button (click)="page > 1 && loadUrls(page - 1)" 
                  class="btn bg-[#2a2d3a] hover:bg-[#3b3e50] text-white text-xs font-bold py-2.5 px-4 rounded-xl transition-all disabled:opacity-40 disabled:cursor-not-allowed" 
                  [disabled]="page <= 1">
            ← Prev
          </button>
          <span class="text-xs font-medium text-gray-400">Page {{ page }} of {{ totalPages }}</span>
          <button (click)="page < totalPages && loadUrls(page + 1)" 
                  class="btn bg-[#2a2d3a] hover:bg-[#3b3e50] text-white text-xs font-bold py-2.5 px-4 rounded-xl transition-all disabled:opacity-40 disabled:cursor-not-allowed" 
                  [disabled]="page >= totalPages">
            Next →
          </button>
        </div>

        <!-- Empty State -->
        <div *ngIf="urls.length === 0" class="text-center py-12">
          <span class="text-4xl">📭</span>
          <p class="mt-4 text-gray-300 font-medium">No links created yet</p>
          <p class="text-sm text-gray-400 mt-1">Shorten a URL using the box above to get started.</p>
        </div>
      </div>
    </div>
  `
})
export class DashboardComponent implements OnInit {
  longUrl = '';
  customAlias = '';
  createError = '';
  createdUrl = '';
  urls: any[] = [];
  page = 1;
  totalPages = 1;

  constructor(private urlService: UrlService, private auth: AuthService, private router: Router) {}

  ngOnInit() {
    if (!this.auth.isLoggedIn()) {
      this.router.navigate(['/login']);
      return;
    }
    this.loadUrls();
  }

  loadUrls(page = 1) {
    this.page = page;
    this.urlService.listUrls(page).subscribe({
      next: (res) => {
        this.urls = res.items;
        this.totalPages = Math.ceil(res.total / res.pageSize);
      },
      error: () => this.router.navigate(['/login'])
    });
  }

  createUrl() {
    this.createError = '';
    this.createdUrl = '';
    this.urlService.createUrl(this.longUrl, this.customAlias || undefined).subscribe({
      next: (res) => {
        this.createdUrl = res.shortUrl;
        this.longUrl = '';
        this.customAlias = '';
        this.loadUrls();
      },
      error: (err) => this.createError = err.error?.error || 'Failed to create URL.'
    });
  }

  deleteUrl(id: string) {
    if (confirm('Are you sure you want to delete this link?')) {
      this.urlService.deleteUrl(id).subscribe({
        next: () => this.loadUrls(this.page),
        error: (err) => alert(err.error?.error || 'Failed to delete.')
      });
    }
  }

  copyToClipboard(text: string) {
    navigator.clipboard.writeText(text).then(() => {
      alert('Copied to clipboard!');
    });
  }
}
