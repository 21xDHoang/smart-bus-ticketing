import axiosClient from './axiosClient';
import { DRIVER_TRIP_PAGE_SIZE, fetchDriverTrips } from './driverApi';
import type { TripStatus } from './tripApi';

// -----------------------------------------------------------------------------
// API quản trị người dùng — nối với AdminUserController (story 22).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Quản trị người dùng":
//   GET    /api/admin/users?search=&role=&isActive=&page=&pageSize=
//     → { items: AdminUser[], total, page, pageSize }
//   GET    /api/admin/users/{id}             → AdminUser
//   POST   /api/admin/users                  → AdminUser (201)
//   PUT    /api/admin/users/{id}             → AdminUser
//   DELETE /api/admin/users/{id}             → AdminUser  (xoá mềm = khóa)
//   PATCH  /api/admin/users/{id}/status      → AdminUser
//   PUT    /api/admin/users/{id}/roles       → AdminUser
//   PUT    /api/admin/users/{id}/password    → AdminUser
//
// Phân công của tài xế KHÔNG nằm trong AdminUser: backend trả nó qua
// GET /api/drivers/{id}/trips (mục "Hồ sơ tài xế"). Hàm fetchDriverAssignment
// bên dưới rút gọn dữ liệu đó thành "chuyến đang chạy / chuyến kế tiếp".
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
  /** Toàn bộ vai trò tài khoản đang giữ (vai trò chính + bảng nối UserRoles). */
  roles: RoleCode[];
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

/** Body của POST /admin/users. */
export interface CreateAdminUserPayload {
  fullName: string;
  phoneNumber: string;
  email?: string | null;
  password: string;
  /** Vai trò chính — backend chỉ nhận MỘT mã khi tạo. */
  roleCode: RoleCode;
}

/** Body của PUT /admin/users/{id} — hồ sơ thuần, không kèm mật khẩu/vai trò/trạng thái. */
export interface UpdateAdminUserPayload {
  fullName: string;
  phoneNumber: string;
  email?: string | null;
}

/**
 * Phân công hiện tại của một tài xế — rút từ chuyến đang chạy, nếu không có thì từ
 * chuyến kế tiếp đã lên lịch. Backend không có endpoint riêng: dữ liệu lấy từ
 * GET /drivers/{id}/trips (mục "Hồ sơ tài xế" của api-contract.md).
 */
export interface DriverAssignment {
  tripId: string;
  routeId: string;
  routeCode: string;
  routeName: string;
  busId: string;
  busLicensePlate: string;
  departureTime: string;
  /** Running = đang chạy; Scheduled = chuyến kế tiếp. */
  status: TripStatus;
}

export interface RoleMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: string;
  /** Màu nền Avatar (hex). */
  bg: string;
}

// bg là màu nền Avatar (chữ trắng bên trên) — lấy từ bảng màu trong components/ui/theme.ts,
// cả 5 sắc đều đạt ≥4.5:1 với chữ trắng. color vẫn là tên màu Tag preset của AntD
// (StatusTag quy về tông tương ứng). Không đổi cấu trúc — chỉ đổi giá trị màu.
export const ROLE_META: Record<RoleCode, RoleMeta> = {
  Admin: { label: 'Admin', color: 'geekblue', bg: '#0B5FFF' },
  Manager: { label: 'Quản lý', color: 'purple', bg: '#6C4FA1' },
  Driver: { label: 'Tài xế', color: 'cyan', bg: '#007A6F' },
  Passenger: { label: 'Hành khách', color: 'green', bg: '#15803D' },
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
    ROLE_META[code as RoleCode] ?? { label: code || 'Không xác định', color: 'default', bg: '#64748B' }
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
  roles: [roleFor(index)],
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

/** GET /api/admin/users/{id} — chi tiết một tài khoản. */
export function fetchAdminUser(id: string): Promise<AdminUser> {
  return axiosClient.get<AdminUser, AdminUser>(`/admin/users/${id}`);
}

/** POST /api/admin/users — tạo tài khoản với MỘT vai trò chính. */
export function createAdminUser(payload: CreateAdminUserPayload): Promise<AdminUser> {
  return axiosClient.post<AdminUser, AdminUser>('/admin/users', payload);
}

/** PUT /api/admin/users/{id} — sửa hồ sơ (họ tên, SĐT, email). */
export function updateAdminUser(
  id: string,
  payload: UpdateAdminUserPayload,
): Promise<AdminUser> {
  return axiosClient.put<AdminUser, AdminUser>(`/admin/users/${id}`, payload);
}

/** DELETE /api/admin/users/{id} — xoá mềm: khóa tài khoản, dữ liệu vẫn giữ nguyên. */
export function deleteAdminUser(id: string): Promise<AdminUser> {
  return axiosClient.delete<AdminUser, AdminUser>(`/admin/users/${id}`);
}

/** PATCH /api/admin/users/{id}/status — khóa / mở khóa tài khoản. */
export function setAdminUserStatus(id: string, isActive: boolean): Promise<AdminUser> {
  return axiosClient.patch<AdminUser, AdminUser>(`/admin/users/${id}/status`, { isActive });
}

/** PUT /api/admin/users/{id}/roles — gửi DANH SÁCH ĐẦY ĐỦ vai trò muốn giữ. */
export function setAdminUserRoles(id: string, roleCodes: RoleCode[]): Promise<AdminUser> {
  return axiosClient.put<AdminUser, AdminUser>(`/admin/users/${id}/roles`, { roleCodes });
}

/** PUT /api/admin/users/{id}/password — Admin đặt lại mật khẩu, buộc phiên cũ đăng nhập lại. */
export function resetAdminUserPassword(id: string, newPassword: string): Promise<AdminUser> {
  return axiosClient.put<AdminUser, AdminUser>(`/admin/users/${id}/password`, { newPassword });
}

/**
 * Thứ tự quyền hạn giảm dần — dùng để chọn vai trò chính khi tạo tài khoản có nhiều vai trò.
 * Backend tự chọn vai trò chính khi đổi danh sách (PUT /roles), nhưng POST chỉ nhận một mã.
 */
const ROLE_PRIORITY: RoleCode[] = ['Admin', 'Manager', 'Driver', 'Passenger'];

/** Vai trò có quyền cao nhất trong danh sách; rỗng thì trả undefined. */
export function primaryRoleOf(roles: RoleCode[]): RoleCode | undefined {
  return ROLE_PRIORITY.find((role) => roles.includes(role)) ?? roles[0];
}

/** Tài khoản có mang vai trò Tài xế không (ở vai trò chính hoặc bất kỳ vai trò nào). */
export function isDriver(user: Pick<AdminUser, 'role' | 'roles'>): boolean {
  // `roles` có thể vắng ở phản hồi cũ đang được cache — vẫn phải đọc ra vai trò chính.
  return user.role === 'Driver' || (user.roles ?? []).includes('Driver');
}

/**
 * Phân công hiện tại của tài xế: chuyến đang chạy, nếu không có thì chuyến kế tiếp.
 *
 * Lấy lùi 24 giờ để không bỏ sót chuyến đã khởi hành từ sáng mà vẫn đang Running.
 * Trả null khi tài xế chưa được gán chuyến nào sắp tới.
 */
export async function fetchDriverAssignment(driverId: string): Promise<DriverAssignment | null> {
  const from = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();

  const result = await fetchDriverTrips(driverId, {
    page: 1,
    pageSize: DRIVER_TRIP_PAGE_SIZE,
    from,
  });

  // Backend xếp theo giờ khởi hành tăng dần. Ưu tiên chuyến đang chạy vì đó là thứ người
  // quản trị muốn thấy trước; nếu không có mới lấy chuyến Đã lên lịch gần nhất trong tương lai.
  const running = result.items.find((trip) => trip.status === 'Running');
  const now = Date.now();
  const upcoming = result.items.find(
    (trip) => trip.status === 'Scheduled' && new Date(trip.departureTime).getTime() >= now,
  );

  const trip = running ?? upcoming;
  if (!trip) return null;

  return {
    tripId: trip.id,
    routeId: trip.routeId,
    routeCode: trip.routeCode,
    routeName: trip.routeName,
    busId: trip.busId,
    busLicensePlate: trip.busLicensePlate,
    departureTime: trip.departureTime,
    status: trip.status,
  };
}
