import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Needed when tunnelling the dev server for real-device testing inside Telegram.
    allowedHosts: true,
    proxy: {
      "/api": { target: "http://127.0.0.1:5199", changeOrigin: true },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./test/setup.ts"],
    css: false,
  },
});
