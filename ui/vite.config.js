import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath } from 'node:url';
const at = p => fileURLToPath(new URL(p, import.meta.url));
export default defineConfig({plugins:[vue()],base:'./',resolve:{alias:{'@ui':at('./vendor'),'@pocketshell/core/shared':at('./vendor/shared')}},build:{outDir:'../src/Annotator/Web',emptyOutDir:true,cssCodeSplit:false,rollupOptions:{output:{entryFileNames:'app.js',assetFileNames:'[name][extname]'}}}});
