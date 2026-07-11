import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { UrlService } from '../services/url.service';

@Component({
  selector: 'app-stats',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="container mx-auto px-4 py-8 max-w-5xl">
      <div class="mb-6">
        <a routerLink="/dashboard" class="text-xs font-semibold text-indigo-400 hover:text-indigo-300 flex items-center gap-1 transition-colors">
          <span>←</span> Back to Dashboard
        </a>
      </div>

      <div class="mb-8">
        <h2 class="text-3xl font-extrabold text-white tracking-tight">Link Analytics</h2>
        <p class="text-sm text-gray-400 mt-1">Real-time traffic statistics and top referrers.</p>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-12 gap-8">
        <!-- Time Series Statistics -->
        <div class="lg:col-span-7 bg-[#1a1d27]/70 backdrop-blur-xl border border-[#2a2d3a] rounded-2xl p-6 shadow-2xl">
          <h3 class="text-lg font-bold text-white mb-4 flex items-center gap-2">
            <span class="text-indigo-400">📈</span> Click Timeline
          </h3>

          <!-- Filters -->
          <div class="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-6">
            <div>
              <label class="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Bucket Size</label>
              <select [(ngModel)]="bucket" (change)="loadStats()" 
                      class="w-full bg-[#0f1117] border border-[#2a2d3a] rounded-xl px-3 py-2 text-xs text-white focus:outline-none focus:border-indigo-500">
                <option value="hour">Hourly</option>
                <option value="day">Daily</option>
              </select>
            </div>
            <div>
              <label class="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">From Date</label>
              <input type="date" [(ngModel)]="fromDate" (change)="loadStats()" 
                     class="w-full bg-[#0f1117] border border-[#2a2d3a] rounded-xl px-3 py-2 text-xs text-white focus:outline-none focus:border-indigo-500">
            </div>
            <div>
              <label class="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">To Date</label>
              <input type="date" [(ngModel)]="toDate" (change)="loadStats()" 
                     class="w-full bg-[#0f1117] border border-[#2a2d3a] rounded-xl px-3 py-2 text-xs text-white focus:outline-none focus:border-indigo-500">
            </div>
          </div>

          <!-- Time series table -->
          <div class="overflow-y-auto max-h-[350px]" *ngIf="timeSeries.length > 0">
            <table class="w-full text-left border-collapse">
              <thead>
                <tr class="border-b border-[#2a2d3a]">
                  <th class="pb-3 text-xs font-semibold text-gray-400 uppercase tracking-wider">Date / Time</th>
                  <th class="pb-3 text-xs font-semibold text-gray-400 uppercase tracking-wider text-right">Clicks</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-[#2a2d3a]">
                <tr *ngFor="let item of timeSeries" class="hover:bg-[#202330]/40 transition-colors">
                  <td class="py-3 text-xs font-mono text-gray-300">{{ item.bucket | date:'medium' }}</td>
                  <td class="py-3 text-right">
                    <span class="inline-flex items-center justify-center px-2.5 py-0.5 rounded-full text-xs font-bold bg-indigo-500/10 border border-indigo-500/20 text-indigo-400">
                      {{ item.count }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div *ngIf="timeSeries.length === 0" class="text-center py-12">
            <span class="text-3xl">📊</span>
            <p class="mt-3 text-sm text-gray-400">No clicks recorded for this period.</p>
          </div>
        </div>

        <!-- Top Referrers Statistics -->
        <div class="lg:col-span-5 bg-[#1a1d27]/70 backdrop-blur-xl border border-[#2a2d3a] rounded-2xl p-6 shadow-2xl">
          <h3 class="text-lg font-bold text-white mb-6 flex items-center gap-2">
            <span class="text-indigo-400">🧭</span> Top Referrers
          </h3>

          <div class="overflow-y-auto max-h-[420px]" *ngIf="referrers.length > 0">
            <table class="w-full text-left border-collapse">
              <thead>
                <tr class="border-b border-[#2a2d3a]">
                  <th class="pb-3 text-xs font-semibold text-gray-400 uppercase tracking-wider">Source</th>
                  <th class="pb-3 text-xs font-semibold text-gray-400 uppercase tracking-wider text-right">Clicks</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-[#2a2d3a]">
                <tr *ngFor="let ref of referrers" class="hover:bg-[#202330]/40 transition-colors">
                  <td class="py-3 text-xs text-gray-300 truncate max-w-[180px] font-mono">
                    {{ ref.referrer || '(Direct / Email / Chat)' }}
                  </td>
                  <td class="py-3 text-right">
                    <span class="inline-flex items-center justify-center px-2.5 py-0.5 rounded-full text-xs font-bold bg-purple-500/10 border border-purple-500/20 text-purple-400">
                      {{ ref.count }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div *ngIf="referrers.length === 0" class="text-center py-12">
            <span class="text-3xl">🌐</span>
            <p class="mt-3 text-sm text-gray-400">No referrer data collected yet.</p>
          </div>
        </div>
      </div>
    </div>
  `
})
export class StatsComponent implements OnInit {
  urlId = '';
  bucket = 'hour';
  fromDate = '';
  toDate = '';
  timeSeries: any[] = [];
  referrers: any[] = [];

  constructor(private route: ActivatedRoute, private urlService: UrlService) {}

  ngOnInit() {
    this.urlId = this.route.snapshot.paramMap.get('id')!;
    const now = new Date();
    this.toDate = now.toISOString().split('T')[0];
    const weekAgo = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
    this.fromDate = weekAgo.toISOString().split('T')[0];
    this.loadStats();
    this.loadReferrers();
  }

  loadStats() {
    this.urlService.getStats(this.urlId, this.fromDate, this.toDate, this.bucket).subscribe({
      next: (data) => this.timeSeries = data,
      error: () => this.timeSeries = []
    });
  }

  loadReferrers() {
    this.urlService.getReferrers(this.urlId).subscribe({
      next: (data) => this.referrers = data,
      error: () => this.referrers = []
    });
  }
}
