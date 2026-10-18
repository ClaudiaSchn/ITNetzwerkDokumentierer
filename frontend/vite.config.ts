import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Für die Entwicklung ohne Docker: "npm run dev" leitet /api an das lokal
// gestartete Backend weiter (dotnet run, Port 5080 aus launchSettings.json).
export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      '/api': 'http://localhost:5080'
    }
  }
})
