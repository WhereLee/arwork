import '@testing-library/jest-dom/vitest';

// antd 的响应式组件会问 window.matchMedia，jsdom 没有实现，补一个空壳。
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
});
