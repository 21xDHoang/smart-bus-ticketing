// Hợp đồng API /audit-logs — xem docs/api-contract.md (mục "Nhật ký kiểm toán").
// Backend hiện mới chỉ có GET /audit-logs/export; GET /audit-logs (truy vấn danh sách)
// là task của Kiên, chưa có trong contract. Vì vậy file này CHỈ khai báo kiểu dữ liệu
// và bảng nhãn/màu hành động để modal chi tiết (và sau này màn hình danh sách của Hạnh)
// dùng chung — chưa có hàm gọi API nào.

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
