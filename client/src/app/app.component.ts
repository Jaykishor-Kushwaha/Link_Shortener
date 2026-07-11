import { Component } from '@angular/core';
import { RouterOutlet, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, CommonModule],
  template: `
    <div class="min-h-screen bg-[#0f1117] text-gray-100 flex flex-col">
      <!-- Premium Navigation -->
      <nav class="bg-[#1a1d27]/90 backdrop-blur-md sticky top-0 z-50 border-b border-[#2a2d3a] px-6 py-4 flex justify-between items-center transition-all duration-300">
        <h1 class="text-xl font-bold tracking-tight">
          <a routerLink="/" class="flex items-center gap-2 hover:opacity-90 transition-opacity">
            <span class="text-2xl">🔗</span>
            <span class="bg-gradient-to-r from-indigo-400 to-purple-400 bg-clip-text text-transparent">URL.Short</span>
          </a>
        </h1>
        <div class="flex items-center gap-4">
          <a *ngIf="!auth.isLoggedIn()" routerLink="/login" class="text-sm font-medium text-gray-300 hover:text-white transition-colors">Login</a>
          <a *ngIf="!auth.isLoggedIn()" routerLink="/signup" class="btn-primary text-sm font-semibold bg-gradient-to-r from-indigo-500 to-purple-600 hover:from-indigo-600 hover:to-purple-700 text-white px-4 py-2 rounded-lg shadow-lg hover:shadow-indigo-500/20 transition-all duration-200">Sign Up</a>
          
          <button *ngIf="auth.isLoggedIn()" (click)="auth.logout()" class="text-sm font-medium bg-red-500/10 hover:bg-red-500/20 text-red-400 px-4 py-2 rounded-lg border border-red-500/20 transition-all duration-200">
            Logout
          </button>
        </div>
      </nav>

      <!-- Main Layout -->
      <main class="flex-grow flex flex-col justify-start">
        <router-outlet></router-outlet>
      </main>
    </div>
  `
})
export class AppComponent {
  constructor(public auth: AuthService) {}
}
