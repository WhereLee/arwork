import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// 测试配置和构建配置放一起，避免两边插件版本对不上。
export default defineConfig({
  plugins: [react()],
  test: {
    // 组件要跑在模拟的浏览器环境里，antd 依赖 matchMedia 之类的 API
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
