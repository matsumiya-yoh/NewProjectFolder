import 'zone.js';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app'; // ← ここを AppComponent に変更

bootstrapApplication(AppComponent, appConfig) // ← ここも AppComponent に変更
  .catch((err) => console.error(err));
