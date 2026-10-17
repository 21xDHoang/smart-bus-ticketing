// -----------------------------------------------------------------------------
// Quyền thông báo của trình duyệt — phần logic thuần cho component
// NotificationPermissionPrompt (task "Xin quyền thông báo và hướng dẫn khi người dùng
// từ chối", Sprint 5 dòng 23).
//
// Tách khỏi component để ba việc dễ đọc và dễ thay khi trình duyệt đổi API:
//   1. đọc trạng thái quyền hiện tại (Notification.permission);
//   2. xin quyền — luôn gọi từ một thao tác bấm của người dùng;
//   3. theo dõi quyền đổi từ bên ngoài (người dùng sửa cài đặt trang web).
//
// ⚠️ Quyền này KHÔNG phải "đăng ký nhận Web Push". Đẩy thông báo thật vẫn đi qua
// SignalR + bảng Notifications (quyết định A8.5); quyền trình duyệt chỉ là điều kiện
// cần nếu sau này màn hình muốn hiện thông báo hệ điều hành (docs/46 §2).
// -----------------------------------------------------------------------------

/**
 * Trạng thái quyền mà giao diện cần phân biệt.
 *
 * `unsupported` gộp hai trường hợp không thể xin quyền: trình duyệt không có API
 * Notification, hoặc trang chạy ngoài ngữ cảnh an toàn (HTTP thường). Gọi
 * `Notification.requestPermission()` trong hai trường hợp này hoặc ném lỗi, hoặc trả
 * "denied" mà người dùng không có cách nào sửa — nên không hiện lời mời.
 */
export type NotificationPermissionState = 'unsupported' | 'default' | 'granted' | 'denied';

/** Khoá đánh dấu đã bấm "Để sau"/"Đã hiểu" — giá trị là trạng thái đã bị ẩn. */
const DISMISS_KEY = 'smartbus.notifications.permissionDismissed';

/** Trình duyệt có dùng được API Notification trong trang hiện tại không. */
export function isNotificationSupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    'Notification' in window &&
    // API chỉ chạy trên HTTPS (localhost cũng tính là an toàn). Chạy qua IP LAN bằng
    // HTTP thì isSecureContext = false, xin quyền cũng vô ích.
    window.isSecureContext === true
  );
}

/** Chuẩn hoá giá trị thô của trình duyệt — giá trị lạ (trình duyệt cũ) coi như 'default'. */
function toState(permission: NotificationPermission): NotificationPermissionState {
  return permission === 'granted' || permission === 'denied' ? permission : 'default';
}

/** Quyền thông báo hiện tại. Không hỗ trợ → 'unsupported', không bao giờ ném lỗi. */
export function getNotificationPermission(): NotificationPermissionState {
  if (!isNotificationSupported()) return 'unsupported';
  return toState(Notification.permission);
}

/**
 * Xin quyền thông báo. PHẢI gọi từ thao tác bấm của người dùng — trình duyệt chặn lời
 * gọi tự động khi trang vừa tải (đặc biệt Safari/Firefox).
 *
 * Bọc cả hai kiểu API:
 *   - trình duyệt mới trả Promise;
 *   - Safari cũ chỉ nhận callback và trả undefined.
 * Lỗi bất ngờ (ngữ cảnh không an toàn, chính sách doanh nghiệp chặn) → đọc lại quyền
 * hiện tại thay vì ném lỗi ra ngoài, để giao diện luôn có trạng thái để hiển thị.
 */
export function requestNotificationPermission(): Promise<NotificationPermissionState> {
  if (!isNotificationSupported()) return Promise.resolve('unsupported');

  return new Promise((resolve) => {
    let settled = false;

    const settle = (permission: NotificationPermission) => {
      if (settled) return;
      settled = true;
      resolve(toState(permission));
    };

    try {
      const result = Notification.requestPermission(settle);

      if (result && typeof result.then === 'function') {
        result.then(settle, () => settle(Notification.permission));
      }
    } catch {
      settle(Notification.permission);
    }
  });
}

/**
 * Người dùng đã bấm "Để sau" (lúc quyền còn `default`) hoặc "Đã hiểu" (lúc bị chặn)
 * trong phiên này chưa.
 *
 * Dùng sessionStorage chứ không localStorage: ẩn hết đời thì người từng từ chối sẽ
 * không bao giờ thấy lại hướng dẫn; mà nhắc lại mỗi lần mở tab cũng phiền. Mỗi phiên
 * trình duyệt hỏi/hướng dẫn một lần là mức cân bằng.
 */
export function isPromptDismissed(state: 'default' | 'denied'): boolean {
  try {
    return sessionStorage.getItem(DISMISS_KEY) === state;
  } catch {
    // Chế độ riêng tư/chặn lưu trữ: coi như chưa ẩn, chỉ hiện lời mời sớm hơn.
    return false;
  }
}

/** Ghi nhớ trạng thái vừa bị ẩn trong phiên này. Lỗi lưu trữ thì bỏ qua, không chặn app. */
export function dismissPrompt(state: 'default' | 'denied'): void {
  try {
    sessionStorage.setItem(DISMISS_KEY, state);
  } catch {
    // Không làm gì — chỉ mất việc ghi nhớ "đã ẩn", lời mời có thể hiện lại.
  }
}

/**
 * Theo dõi quyền đổi TỪ BÊN NGOÀI component (người dùng mở cài đặt trang web, đổi
 * sang Cho phép rồi quay lại tab). Trả hàm huỷ theo dõi.
 *
 * Hai nguồn bù nhau:
 *   - Permissions API báo ngay khi quyền đổi (Chrome/Edge/Firefox);
 *   - `visibilitychange` bắt lúc quay lại tab, cho Safari và trình duyệt không hỗ trợ
 *     truy vấn 'notifications'.
 */
export function watchNotificationPermission(onChange: () => void): () => void {
  if (typeof document === 'undefined') return () => {};

  const handleVisibility = () => {
    if (document.visibilityState === 'visible') onChange();
  };

  document.addEventListener('visibilitychange', handleVisibility);

  let status: PermissionStatus | null = null;
  let stopped = false;

  // `navigator.permissions` có ở mọi trình duyệt hiện đại, nhưng tên quyền
  // 'notifications' thì không — Safari cũ trả lỗi, đã có visibilitychange bù.
  navigator.permissions
    ?.query({ name: 'notifications' as PermissionName })
    .then((result) => {
      if (stopped) return;
      status = result;
      result.addEventListener('change', onChange);
    })
    .catch(() => {
      // Không truy vấn được — giữ visibilitychange.
    });

  return () => {
    stopped = true;
    document.removeEventListener('visibilitychange', handleVisibility);
    status?.removeEventListener('change', onChange);
  };
}
