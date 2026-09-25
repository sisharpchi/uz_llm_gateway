import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({ plugins: [react()], server: {
  port: 5175, proxy: { '/management': { target: 'http://localhost:5063', changeOrigin: true } }
} });
