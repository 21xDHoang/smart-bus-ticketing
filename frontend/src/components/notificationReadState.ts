import { useEffect, useState } from 'react';

// -----------------------------------------------------------------------------
// Trạng thái "đã đọc" của thông báo — PHÍA CLIENT, vì backend chưa có.
//
// Bảng Notifications chưa có cột "đã đọc" (docs/api-contract.md mục /notifications cảnh báo
// không được giả trường `isRead`), nên màn danh sách + chi tiết (task "Màn hình chi tiết
// thông báo và đánh dấu đã đọc", US 10, Sprint 5 — Dương Thị Hạnh) lưu danh sách id đã đọc
// trong localStorage, khoá theo từng người dùng. Cùng lối client-side mà NotificationBell đang
// dùng cho badge ("mốc đã xem"), chỉ khác là ở đây theo TỪNG thông báo. Khi backend có cột đã
// đọc, thay khối này bằng giá trị API trả về, không đổi hợp đồng hiển thị.
// -----------------------------------------------------------------------------

/** Khoá localStorage danh sách id đã đọc — khoá theo id người dùng. */
const READ_KEY_PREFIX = 'smartbus.notifications.read.';

/** Đọc danh sách id đã đọc của một người. Không có / lỗi → mảng rỗng (coi chưa đọc gì). */
function readIds(userId: string): string[] {
  try {
    const raw = localStorage.getItem(`${READ_KEY_PREFIX}${userId}`);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as string[]) : [];
  } catch {
    return [];
  }
}

/** Ghi danh sách id đã đọc. Lỗi (private mode, đầy bộ nhớ) thì nuốt — chỉ mất trạng thái. */
function writeIds(userId: string, ids: string[]): void {
  try {
    localStorage.setItem(`${READ_KEY_PREFIX}${userId}`, JSON.stringify(ids));
  } catch {
    // Không làm gì — trạng thái đã đọc chỉ không nhớ được.
  }
}

/** Danh sách id đã đọc của một người; không có người dùng → mảng rỗng. */
export function getReadNotificationIds(userId: string | null | undefined): string[] {
  if (!userId) return [];
  return readIds(userId);
}

/**
 * Hook quản lý trạng thái đã đọc cho các màn thông báo. Trả về danh sách id đã đọc để vẽ
 * trạng thái chưa đọc, cùng hai hàm đánh dấu (một thông báo / nhiều thông báo). Nguồn dữ liệu
 * là state, localStorage chỉ là nơi lưu lại giữa các lần mở app.
 */
export function useNotificationReadState(userId: string | null | undefined) {
  const [readIds, setReadIds] = useState<string[]>(() => getReadNotificationIds(userId));

  // Đổi người đăng nhập (hoặc đăng xuất) → đọc lại danh sách đã đọc của người đó.
  useEffect(() => {
    // Đồng bộ state với localStorage (nguồn ngoài React), không phải state dẫn xuất.
    // oxlint-disable-next-line react/set-state-in-effect
    setReadIds(getReadNotificationIds(userId));
  }, [userId]);

  /** Đánh dấu một thông báo là đã đọc (không ghi trùng id). */
  const markRead = (notificationId: string) => {
    if (!userId) return;
    setReadIds((prev) => {
      if (prev.includes(notificationId)) return prev;
      const next = [...prev, notificationId];
      writeIds(userId, next);
      return next;
    });
  };

  /** Đánh dấu nhiều thông báo là đã đọc trong một lần ghi. */
  const markAllRead = (notificationIds: string[]) => {
    if (!userId) return;
    setReadIds((prev) => {
      let changed = false;
      const next = [...prev];
      for (const id of notificationIds) {
        if (!next.includes(id)) {
          next.push(id);
          changed = true;
        }
      }
      if (!changed) return prev;
      writeIds(userId, next);
      return next;
    });
  };

  return { readIds, markRead, markAllRead };
}
