import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Em produção o painel é servido pela própria API (mesma origem). Em dev, o Vite repassa /api para ela.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/Indexa.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080' },
  },
});
