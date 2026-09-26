// Hợp đồng API /audit-logs — xem docs/api-contract.md (mục "Nhật ký kiểm toán").
// Backend hiện mới chỉ có GET /audit-logs/export; GET /audit-logs (truy vấn danh sách)
// là task của Kiên, chưa có trong contract. Vì vậy file này khai báo kiểu dữ liệu, bảng
// nhãn/màu hành động (dùng chung cho modal chi tiết và màn hình danh sách) và hàm lấy
// nhật ký đăng nhập gần nhất — nhưng chưa gọi endpoint /audit-logs thật nào.

import type { TagProps } from 'antd';

// Sáu mã hành động khớp enum AuditAction của backend.
export type AuditAction =
  | 'Login'
  | 'Logout'
  | 'LoginFailed'
  | 'Create'
  | 'Update'
  | 'Delete';

// Bản ghi nhật ký kiểm toán — đúng các trường trong api-contract.md.
export interface AuditLog {
  id: string;
  /** Người thao tác. null khi không xác định được (đăng nhập thất bại, hệ thống tự làm). */
  userId: string | null;
  action: string;
  /** Đối tượng bị tác động, dạng "<Tên bảng>:<Id>". null với Login/Logout. */
  target: string | null;
  /** Địa chỉ IP người gọi. null khi không lấy được. */
  ipAddress: string | null;
  /** Thời điểm hành động xảy ra (ISO 8601, UTC). */
  createdAt: string;
}

export interface AuditActionMeta {
  label: string;
  color: TagProps['color'];
}

// Nhãn tiếng Việt + màu tag cho từng mã hành động — dùng chung cho modal chi tiết
// và cột "Hành động" của màn hình danh sách.
export const AUDIT_ACTION_META: Record<AuditAction, AuditActionMeta> = {
  Login: { label: 'Đăng nhập', color: 'green' },
  Logout: { label: 'Đăng xuất', color: 'default' },
  LoginFailed: { label: 'Đăng nhập thất bại', color: 'red' },
  Create: { label: 'Tạo mới', color: 'geekblue' },
  Update: { label: 'Cập nhật', color: 'gold' },
  Delete: { label: 'Xoá', color: 'volcano' },
};

// Lấy nhãn + màu cho một mã hành động. Mã lạ (backend bổ sung sau này) vẫn hiển thị
// được: trả về đúng mã đó kèm màu mặc định thay vì crash.
export function getAuditActionMeta(action: string): AuditActionMeta {
  return AUDIT_ACTION_META[action as AuditAction] ?? { label: action, color: 'default' };
}

// -----------------------------------------------------------------------------
// NHẬT KÝ ĐĂNG NHẬP GẦN NHẤT (trang cá nhân)
// -----------------------------------------------------------------------------

// Backend GET /audit-logs (truy vấn danh sách) là task của Kiên, CHƯA có trong
// api-contract.md — mới chỉ có GET /audit-logs/export. Vì vậy hàm dưới tạm trả dữ liệu
// giả để trang cá nhân dựng được giao diện. Khi endpoint có thật và hình dạng response
// được chốt vào contract, thay toàn bộ thân hàm bằng lời gọi axiosClient.get thật.

/**
 * Lấy các bản ghi đăng nhập / đăng xuất gần nhất của một người dùng, mới nhất trước.
 * Tạm trả dữ liệu giả — xem ghi chú ngay phía trên.
 */
export async function fetchMyLoginActivity(userId: string): Promise<AuditLog[]> {
  // Giả lập độ trễ mạng nhẹ để trải nghiệm loading gần với API thật.
  await new Promise((resolve) => setTimeout(resolve, 350));

  const hoursAgo = (hours: number) => new Date(Date.now() - hours * 3_600_000).toISOString();

  // Mọi bản ghi giả đều gắn userId của người đang xem để "thuộc về" họ.
  return [
    { id: 'log-1', userId, action: 'Login', target: null, ipAddress: '203.113.188.5', createdAt: hoursAgo(2) },
    { id: 'log-2', userId, action: 'Logout', target: null, ipAddress: '203.113.188.5', createdAt: hoursAgo(5) },
    { id: 'log-3', userId, action: 'Login', target: null, ipAddress: '113.161.72.12', createdAt: hoursAgo(24) },
    { id: 'log-4', userId, action: 'LoginFailed', target: null, ipAddress: '113.161.72.12', createdAt: hoursAgo(48) },
  ];
}
