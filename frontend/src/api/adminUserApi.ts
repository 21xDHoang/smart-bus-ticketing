import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API quản trị người dùng — nối với AdminUserController (story 22).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Quản trị người dùng":
//   GET /api/admin/users?search=&role=&isActive=&page=&pageSize=
//     → { items: AdminUser[], total, page, pageSize }
// -----------------------------------------------------------------------------

/** Mã vai trò — khớp với Role.Code và claim role trong JWT (TokenService.cs). */
export type RoleCode = 'Admin' | 'Manager' | 'Driver' | 'Passenger';

export interface AdminUser {
  id: string;
  fullName: string;
  phoneNumber: string;
  email: string | null;
  /** Tài khoản đang mở (true) hay đã bị khóa (false). */
  isActive: boolean;
  /** Vai trò chính của tài khoản. */
  role: RoleCode;
  createdAt: string;
}

export interface AdminUserListParams {
  page: number;
  pageSize: number;
  /** Từ khóa tìm theo họ tên / SĐT / email. */
  search?: string;
  role?: RoleCode;
  isActive?: boolean;
}

export interface AdminUserListResult {
  items: AdminUser[];
  total: number;
  page: number;
  pageSize: number;
}

export interface RoleMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: string;
  /** Màu nền Avatar (hex). */
  bg: string;
}

export const ROLE_META: Record<RoleCode, RoleMeta> = {
  Admin: { label: 'Admin', color: 'geekblue', bg: '#2f54eb' },
  Manager: { label: 'Quản lý', color: 'purple', bg: '#722ed1' },
  Driver: { label: 'Tài xế', color: 'cyan', bg: '#13c2c2' },
  Passenger: { label: 'Hành khách', color: 'green', bg: '#52c41a' },
};

/** Danh sách vai trò cho bộ lọc, theo thứ tự hiển thị mong muốn. */
export const ROLE_OPTIONS: { value: RoleCode; label: string }[] = [
  { value: 'Admin', label: 'Admin' },
  { value: 'Manager', label: 'Quản lý' },
  { value: 'Driver', label: 'Tài xế' },
  { value: 'Passenger', label: 'Hành khách' },
];

/** Nhãn + màu của một vai trò; mã lạ (backend trả thêm sau này) thì trả nhãn xám an toàn. */
export function getRoleMeta(code: string): RoleMeta {
  return (
    ROLE_META[code as RoleCode] ?? { label: code || 'Không xác định', color: 'default', bg: '#8c8c8c' }
  );
}

// -----------------------------------------------------------------------------
// DỮ LIỆU GIẢ (MOCK)
// -----------------------------------------------------------------------------

/**
 * AdminUserController ĐÃ có (Nguyễn Duy Kiên, story 22) nên màn hình gọi API thật.
 * Khối dữ liệu giả bên dưới giữ lại làm đường lùi; đổi cờ thành true là quay lại được.
 */
const USE_MOCK = false;

const MOCK_NAMES = [
  'Nguyễn Văn An', 'Trần Thị Bích Ngọc', 'Lê Hoàng Cường', 'Phạm Minh Đức',
  'Hoàng Thu Hà', 'Vũ Quốc Hùng', 'Đặng Ngọc Lan', 'Bùi Thanh Long',
  'Đỗ Thị Mai', 'Ngô Văn Nam', 'Phan Thu Phương', 'Dương Minh Quân',
  'Lý Thu Trang', 'Hà Ngọc Sơn', 'Đinh Thị Thảo', 'Mai Xuân Thắng',
  'Vương Kim Thư', 'Lưu Bảo Trâm', 'Tô Văn Trường', 'Huỳnh Thị Tuyết',
  'Nguyễn Đình Anh', 'Trần Văn Bình', 'Lê Thị Cẩm', 'Phạm Quốc Dũng',
  'Hoàng Minh Đạt', 'Vũ Thị Giang', 'Đặng Văn Hải', 'Bùi Thị Hồng',
  'Đỗ Văn Khoa', 'Ngô Thị Linh', 'Phan Minh Mẫn', 'Dương Thị Nhung',
  'Lý Văn Phong', 'Hà Thu Quỳnh', 'Đinh Văn Sỹ', 'Mai Thị Thanh',
];

/** Các chỉ số tương ứng với tài khoản bị khóa (isActive = false). */
const LOCKED_INDEXES = new Set([4, 13, 25, 31]);

/** Bỏ dấu tiếng Việt để tạo email trông tự nhiên từ họ tên. */
function slugify(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '');
}

/** Rải đều 4 vai trò cho dữ liệu giả, phần lớn là Hành khách. */
function roleFor(index: number): RoleCode {
  if (index === 0 || index === 18) return 'Admin';
  if (index % 10 === 1) return 'Manager'; // 1, 11, 21, 31
  if (index % 8 === 3) return 'Driver'; // 3, 19, 27, 35
  return 'Passenger';
}

const MOCK_USERS: AdminUser[] = MOCK_NAMES.map((fullName, index) => ({
  id: `00000000-0000-0000-0000-${String(index).padStart(12, '0')}`,
  fullName,
  phoneNumber: `09${String(10000000 + index * 137).padStart(8, '0')}`,
  email: index % 6 === 2 ? null : `${slugify(fullName)}${index}@gmail.com`,
  isActive: !LOCKED_INDEXES.has(index),
  role: roleFor(index),
  createdAt: new Date(Date.UTC(2026, 8, 24) - index * 86_400_000).toISOString(),
}));

/** Mô phỏng đúng hành vi lọc + tìm kiếm + phân trang ở phía server. */
function queryMockUsers(params: AdminUserListParams): AdminUserListResult {
  const search = params.search?.trim().toLowerCase();

  let rows = MOCK_USERS;

  if (search) {
    rows = rows.filter(
      (u) =>
        u.fullName.toLowerCase().includes(search) ||
        u.phoneNumber.includes(search) ||
        (u.email ?? '').toLowerCase().includes(search),
    );
  }

  if (params.role) {
    rows = rows.filter((u) => u.role === params.role);
  }

  if (params.isActive !== undefined) {
    rows = rows.filter((u) => u.isActive === params.isActive);
  }

  const total = rows.length;
  const start = (params.page - 1) * params.pageSize;
  const items = rows.slice(start, start + params.pageSize);

  return { items, total, page: params.page, pageSize: params.pageSize };
}

export async function fetchAdminUsers(params: AdminUserListParams): Promise<AdminUserListResult> {
  if (USE_MOCK) {
    // Giả lập độ trễ mạng nhẹ để trải nghiệm loading gần với API thật.
    await new Promise((resolve) => setTimeout(resolve, 250));
    return queryMockUsers(params);
  }

  return axiosClient.get<AdminUserListResult, AdminUserListResult>('/admin/users', { params });
}
