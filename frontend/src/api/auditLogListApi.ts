import axiosClient from './axiosClient';
import type { AuditAction, AuditLog } from './auditLogApi';

// -----------------------------------------------------------------------------
// API danh sách nhật ký kiểm toán — nối với AuditLogsController (story 23).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Nhật ký kiểm toán":
//   GET /api/audit-logs?from=&to=&userId=&action=&page=&pageSize=
//     → { items: AuditLogListItem[], total, page, pageSize }
//
// File riêng của Hạnh: `auditLogApi.ts` (của Băng) giữ kiểu nền `AuditLog`, bảng nhãn/màu
// hành động và API nhật ký đăng nhập của trang cá nhân. Màn hình danh sách cần thêm họ
// tên + SĐT để hiển thị nên tách riêng ở đây, không sửa file dùng chung.
// -----------------------------------------------------------------------------

/** Một dòng nhật ký trong danh sách — thêm họ tên + SĐT (ghép từ bảng Users) để hiển thị. */
export interface AuditLogListItem extends AuditLog {
  /** Họ tên người thao tác. null khi userId là null (đăng nhập thất bại, hệ thống tự làm). */
  userFullName: string | null;
  /** SĐT người thao tác. null khi userId là null. */
  userPhoneNumber: string | null;
}

export interface AuditLogListParams {
  page: number;
  pageSize: number;
  /** Ngày bắt đầu, `yyyy-MM-dd`, tính trọn ngày theo UTC. */
  from?: string;
  /** Ngày kết thúc, `yyyy-MM-dd`, tính trọn ngày theo UTC. */
  to?: string;
  /** Chỉ lấy thao tác của một người (GUID). */
  userId?: string;
  /** Chỉ lấy một loại hành động — một trong 6 mã AuditAction. */
  action?: AuditAction;
}

export interface AuditLogListResult {
  items: AuditLogListItem[];
  total: number;
  page: number;
  pageSize: number;
}

/** Truy vấn danh sách nhật ký, mới nhất trước, có phân trang. */
export async function fetchAuditLogs(
  params: AuditLogListParams,
): Promise<AuditLogListResult> {
  return axiosClient.get<AuditLogListResult, AuditLogListResult>('/audit-logs', { params });
}
