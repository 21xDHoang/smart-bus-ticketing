import { useSyncExternalStore } from 'react';
import type { ColorMode } from './theme';

// Chế độ sáng/tối của app — một store nhỏ nằm NGOÀI React (không cần Provider). Quy tắc:
//   • Người dùng chưa tự chọn  → theo cài đặt hệ điều hành, hệ điều hành đổi thì đổi theo.
//   • Người dùng đã bấm nút   → lựa chọn được ghi nhớ (localStorage), không theo hệ điều hành nữa.
// DOM nhận chế độ qua `data-theme` trên <html> để CSS thô trong index.css đổi theo; script nhỏ
// trong index.html đặt sẵn thuộc tính này TRƯỚC khi React mount để không nháy màu.

const STORAGE_KEY = 'sbt-color-mode';
const MEDIA = '(prefers-color-scheme: dark)';

function readStored(): ColorMode | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === 'light' || value === 'dark' ? value : null;
  } catch {
    return null; // Chế độ riêng tư chặn localStorage — vẫn chạy, chỉ không nhớ được lựa chọn.
  }
}

function systemMode(): ColorMode {
  return window.matchMedia(MEDIA).matches ? 'dark' : 'light';
}

let stored: ColorMode | null = readStored();
let mode: ColorMode = stored ?? systemMode();

const listeners = new Set<() => void>();

function apply(next: ColorMode): void {
  mode = next;
  document.documentElement.dataset.theme = next;
  document.documentElement.style.colorScheme = next;
}

function emit(): void {
  listeners.forEach((listener) => listener());
}

/** Đổi chế độ và ghi nhớ lựa chọn — dùng cho nút bật/tắt. */
export function setColorMode(next: ColorMode): void {
  stored = next;
  try {
    localStorage.setItem(STORAGE_KEY, next);
  } catch {
    // Không ghi được thì thôi — lựa chọn chỉ có hiệu lực trong phiên này.
  }
  apply(next);
  emit();
}

/** Đảo chế độ hiện tại. */
export function toggleColorMode(): void {
  setColorMode(mode === 'dark' ? 'light' : 'dark');
}

// Hệ điều hành đổi sáng/tối giữa chừng — chỉ đổi theo khi người dùng chưa tự chọn tay.
window.matchMedia(MEDIA).addEventListener('change', (event) => {
  if (stored) return;
  apply(event.matches ? 'dark' : 'light');
  emit();
});

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): ColorMode {
  return mode;
}

// Đồng bộ DOM ngay khi module được nạp (script trong index.html đã đặt trước đó — ghi lại
// cùng giá trị nên không nháy).
apply(mode);

/** Đọc chế độ hiện tại + hai hàm đổi — gọi trong ThemeModeToggle hoặc App.tsx. */
export function useColorMode(): {
  mode: ColorMode;
  setMode: (next: ColorMode) => void;
  toggle: () => void;
} {
  const current = useSyncExternalStore(subscribe, getSnapshot);
  return { mode: current, setMode: setColorMode, toggle: toggleColorMode };
}
