// Tuyến vừa tra cứu — ghi nhớ trong localStorage để trang chủ gợi ý lại ("Tuyến gần đây").
//
// Đọc ở HomePage; ghi ở màn tra cứu / kết quả tìm chuyến khi người dùng thực hiện một lượt
// tìm (RouteLookupPage gọi `rememberRecentRoute`). Chỉ lưu cặp điểm đi – điểm đến dạng chữ
// hiển thị; KHÔNG lưu id hay giá — dữ liệu thật luôn hỏi lại API.
//
// Cùng lối chịu lỗi với colorMode.ts: chế độ riêng tư chặn localStorage thì tính năng gợi ý
// im lặng tắt, không được làm sập trang.

export interface RecentRoute {
  origin: string;
  destination: string;
}

const STORAGE_KEY = 'sbt-recent-routes';
const MAX_ITEMS = 4;

function isRecentRoute(value: unknown): value is RecentRoute {
  if (typeof value !== 'object' || value === null) return false;
  const item = value as Record<string, unknown>;
  return typeof item.origin === 'string' && typeof item.destination === 'string';
}

/** Tuyến vừa tra cứu, mới nhất trước. Chưa có gì / dữ liệu hỏng / bị chặn → mảng rỗng. */
export function readRecentRoutes(): RecentRoute[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(isRecentRoute).slice(0, MAX_ITEMS);
  } catch {
    return [];
  }
}

/** Ghi nhớ một tuyến vừa tra cứu — trùng thì đưa lên đầu, giữ tối đa `MAX_ITEMS` mục. */
export function rememberRecentRoute(origin: string, destination: string): void {
  const from = origin.trim();
  const to = destination.trim();
  if (!from || !to) return;

  const next: RecentRoute[] = [
    { origin: from, destination: to },
    ...readRecentRoutes().filter((item) => !(item.origin === from && item.destination === to)),
  ].slice(0, MAX_ITEMS);

  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    // Không ghi được thì thôi — gợi ý chỉ là tiện ích, không phải chức năng chính.
  }
}
