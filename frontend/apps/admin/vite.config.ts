import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({ plugins: [react()], server: {
  host: '127.0.0.1',
  port: Number(process.env.PORT ?? 5175),
  proxy: { '/management': { target: process.env.UZLLM_MANAGEMENT_PROXY_TARGET ?? 'http://localhost:5063', changeOrigin: true } }
} });
