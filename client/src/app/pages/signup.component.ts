import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-signup',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="flex-grow flex items-center justify-center px-4 py-12 bg-radial-gradient">
      <div class="w-full max-w-md bg-[#1a1d27]/70 backdrop-blur-xl border border-[#2a2d3a] rounded-2xl p-8 shadow-2xl shadow-black/40">
        <!-- Logo/Title -->
        <div class="text-center mb-8">
          <div class="inline-flex items-center justify-center w-14 h-14 rounded-2xl bg-indigo-500/10 border border-indigo-500/20 text-3xl mb-4">
            🚀
          </div>
          <h2 class="text-2xl font-bold tracking-tight text-white">Create an account</h2>
          <p class="text-sm text-gray-400 mt-2">Get started with secure, fast URL shortening</p>
        </div>

        <!-- Form -->
        <div class="space-y-5">
          <div>
            <label class="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Email Address</label>
            <input type="email" [(ngModel)]="email" placeholder="you@example.com" 
                   class="w-full bg-[#0f1117] border border-[#2a2d3a] rounded-xl px-4 py-3 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 transition-all">
          </div>

          <div>
            <label class="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Password</label>
            <input type="password" [(ngModel)]="password" placeholder="At least 8 characters" 
                   class="w-full bg-[#0f1117] border border-[#2a2d3a] rounded-xl px-4 py-3 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 transition-all">
          </div>

          <!-- Error Alert -->
          <div *ngIf="error" class="bg-red-500/10 border border-red-500/20 rounded-xl p-3 text-sm text-red-400 flex items-center gap-2">
            <span class="text-lg">⚠️</span>
            <span>{{ error }}</span>
          </div>

          <!-- Action Button -->
          <button (click)="signup()" 
                  class="w-full bg-gradient-to-r from-indigo-500 to-purple-600 hover:from-indigo-600 hover:to-purple-700 text-white font-semibold py-3 rounded-xl shadow-lg hover:shadow-indigo-500/20 hover:scale-[1.01] active:scale-[0.99] transition-all duration-150">
            Sign Up
          </button>
        </div>

        <p class="text-center mt-6 text-sm text-gray-400">
          Already have an account? 
          <a routerLink="/login" class="text-indigo-400 hover:text-indigo-300 font-medium transition-colors">Login</a>
        </p>
      </div>
    </div>
  `
})
export class SignupComponent {
  email = '';
  password = '';
  error = '';

  constructor(private auth: AuthService, private router: Router) {}

  signup() {
    this.error = '';
    this.auth.signup(this.email, this.password).subscribe({
      next: () => this.router.navigate(['/dashboard']),
      error: (err) => this.error = err.error?.error || 'Signup failed.'
    });
  }
}
