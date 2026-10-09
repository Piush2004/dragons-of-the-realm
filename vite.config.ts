import { defineConfig } from 'vite';

export default defineConfig({
  root: '.',
  server: {
    port: 5173,
    watch: {
      // Reference media, Blender scripts and build output are not part of the running app.
      ignored: ['**/Images of Dragon/**', '**/build/**', '**/tools/**', '**/dist/**', '**/.remember/**'],
    },
  },
  build: {
    target: 'es2022',
  },
});
