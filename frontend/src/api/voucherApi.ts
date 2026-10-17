import axiosClient from './axiosClient';
import dayjs from 'dayjs';

// -----------------------------------------------------------------------------
// API voucher — nối với nhóm endpoint của User Story 18 "Quản lý Voucher" (Sprint 3).
//
// Hợp đồng đã chốt ở docs/api-contract.md mục "Voucher — /vouchers":
//   GET    /vouchers              → VoucherListResponse          (danh sách + lọc + phân trang — dòng 51, Hiếu)
//   POST   /vouchers              → VoucherResponse (201)        (thêm mới — dòng 51)
//   PUT    /vouchers/{id}         → VoucherResponse              (sửa — dòng 51)
//   PATCH  /vouchers/{id}/status  → VoucherResponse              (bật/tắt áp dụng — dòng 51)
//   POST   /vouchers/validate     → VoucherValidationResponse    (kiểm tra mã + tính tiền giảm — dòng 52, Kiên)
//   GET    /vouchers/statistics   → thống kê hiệu quả voucher (chưa nối FE ở file này)
//
// Backend đã có đủ nên cả 5 hàm gọi thẳng API thật, KHÔNG còn nhánh dữ liệu giả.
//
// `listAvailable` (màn "Khuyến mãi" của khách) là ngoại lệ duy nhất: hợp đồng KHÔNG có endpoint
// công khai nào cho nó (mọi endpoint voucher đều là quản trị ManagerOrAbove hoặc kiểm-tra-mã).
// Vì vậy hàm chạy dữ liệu giả (USE_MOCK_AVAILABLE = true) để màn Khuyến mãi có mã để hiển thị —
// cùng lối revenueByRouteApi.ts / revenueDashboardApi.ts. Nợ hợp đồng: khi có endpoint công khai
// "voucher đang khả dụng", viết thật nhánh `api` rồi lật cờ.
// -----------------------------------------------------------------------------

/** Trạng thái voucher — lưu chuỗi theo quy ước A3, khớp enum backend sau này. */
export type VoucherStatus = 'Active' | 'Inactive';

/** Kiểu giảm giá — theo phần trăm hay số tiền cố định. */
export type VoucherDiscountType = 'Percent' | 'FixedAmount';

/** Một voucher (mã giảm giá) — khớp `VoucherResponse` của backend. */
export interface Voucher {
  id: string;

  /** Mã voucher hiển thị cho khách nhập — "SUMMER10". Duy nhất toàn hệ thống. */
  code: string;

  /** Tên chương trình khuyến mãi. */
  name: string;

  discountType: VoucherDiscountType;

  /** Giá trị giảm: phần trăm (1..100) nếu Percent, số tiền VND nếu FixedAmount. */
  discountValue: number;

  /** Giá trị đơn tối thiểu để áp dụng (VND). 0 = không yêu cầu. */
  minOrderValue: number;

  /** Mức giảm tối đa khi giảm theo phần trăm (VND). null khi FixedAmount. */
  maxDiscount: number | null;

  /** Điều kiện tuyến — null = áp dụng cho mọi tuyến. */
  routeId: string | null;

  /** Mã tuyến để hiển thị — backend kèm sẵn, khỏi gọi thêm GET /routes/{id}. null khi mọi tuyến. */
  routeCode: string | null;

  /** Tên tuyến để hiển thị — null khi mọi tuyến. */
  routeName: string | null;

  /** Tổng số mã phát hành. */
  quantity: number;

  /** Số mã đã dùng — do backend tính, màn hình chỉ hiển thị, không sửa qua form. */
  usedCount: number;

  /** Ngày bắt đầu hiệu lực (ISO 8601). */
  validFrom: string;

  /** Ngày hết hiệu lực (ISO 8601). */
  validUntil: string;

  status: VoucherStatus;

  createdAt: string;

  /** null khi chưa sửa lần nào. */
  updatedAt: string | null;
}

/** Body khi thêm/sửa voucher — không gồm usedCount/createdAt (do backend sinh). */
export interface VoucherPayload {
  code: string;
  name: string;
  discountType: VoucherDiscountType;
  discountValue: number;
  minOrderValue: number;
  maxDiscount: number | null;
  /**
   * Điều kiện tuyến — bỏ trống = áp dụng cho mọi tuyến (form chưa có ô chọn tuyến;
   * trường đã có sẵn ở backend CreateVoucherRequest/UpdateVoucherRequest).
   */
  routeId?: string | null;
  quantity: number;
  validFrom: string;
  validUntil: string;
}

export interface VoucherListParams {
  /** Tìm theo mã hoặc tên chương trình — không phân biệt hoa thường. */
  search?: string;
  /** Bỏ trống = lấy cả hai trạng thái. */
  status?: VoucherStatus;
  page: number;
  pageSize: number;
}

export interface VoucherListResult {
  /** Chỉ là trang hiện tại, không phải toàn bộ. */
  items: Voucher[];
  /** Tổng số dòng KHỚP BỘ LỌC (không phải số dòng trong `items`) — dùng để vẽ phân trang. */
  total: number;
  page: number;
  pageSize: number;
}

// ---------------------------------------------------------------------------
// POST /vouchers/validate — kiểm tra mã và tính số tiền giảm (dòng 52, task của Kiên).
// Màn thanh toán gọi khi khách áp dụng mã để HIỂN THỊ số tiền được giảm và giá sau giảm
// (dòng 58 — Băng). Đây là XEM TRƯỚC: mã có thể hết lượt/hết hạn giữa lúc xem và lúc bấm
// thanh toán; số tiền giảm chỉ được chốt lại ở tầng thanh toán (xem hợp đồng).
// ---------------------------------------------------------------------------

/** Lý do một voucher KHÔNG dùng được — khớp hằng `VoucherReasonCodes` của backend. */
export type VoucherReasonCode =
  | 'NotFound'
  | 'Inactive'
  | 'NotStarted'
  | 'Expired'
  | 'OutOfStock'
  | 'WrongRoute'
  | 'BelowMinOrder';

/** Body của POST /vouchers/validate — khớp `ValidateVoucherRequest` của backend. */
export interface VoucherValidateRequest {
  /** Mã khách gõ; backend chuẩn hoá chữ HOA nên gõ thường vẫn khớp. */
  code: string;
  /** Chuyến khách đang đặt — để backend tra điều kiện tuyến (`routeId`). */
  tripId: string;
  /** Tổng tiền TRƯỚC giảm giá (VND). */
  orderAmount: number;
}

/** Kết quả POST /vouchers/validate — khớp `VoucherValidationResponse` của backend. */
export interface VoucherValidationResponse {
  valid: boolean;
  reasonCode: VoucherReasonCode | null;
  message: string | null;
  voucherId: string | null;
  code: string | null;
  discountType: VoucherDiscountType | null;
  /** Số tiền được giảm, ĐÃ làm tròn về đồng nguyên; 0 khi mã không dùng được. */
  discountAmount: number;
  /** Số tiền phải trả sau giảm = orderAmount - discountAmount; không bao giờ âm. */
  finalAmount: number;
  maxDiscount: number | null;
}

interface VoucherStatusMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: string;
}

export const VOUCHER_STATUS_META: Record<VoucherStatus, VoucherStatusMeta> = {
  Active: { label: 'Đang áp dụng', color: 'success' },
  Inactive: { label: 'Ngừng áp dụng', color: 'default' },
};

interface VoucherDiscountTypeMeta {
  label: string;
}

export const VOUCHER_DISCOUNT_TYPE_META: Record<VoucherDiscountType, VoucherDiscountTypeMeta> = {
  Percent: { label: 'Phần trăm' },
  FixedAmount: { label: 'Số tiền cố định' },
};

/** Danh sách trạng thái cho bộ lọc — sinh từ META để nhãn không lệch với cột Trạng thái. */
export const VOUCHER_STATUS_OPTIONS = (Object.keys(VOUCHER_STATUS_META) as VoucherStatus[]).map(
  (value) => ({ value, label: VOUCHER_STATUS_META[value].label }),
);

/** Danh sách kiểu giảm giá cho form — sinh từ META để nhãn không lệch với cột Giá trị giảm. */
export const VOUCHER_DISCOUNT_TYPE_OPTIONS = (
  Object.keys(VOUCHER_DISCOUNT_TYPE_META) as VoucherDiscountType[]
).map((value) => ({ value, label: VOUCHER_DISCOUNT_TYPE_META[value].label }));

interface VoucherReasonMeta {
  label: string;
}

/**
 * Nhãn tiếng Việt cho từng lý do từ chối — FE DỊCH theo `reasonCode`, KHÔNG so chuỗi `message`
 * (câu chữ của `message` sẽ còn đổi). Thứ tự khớp bảng mã ở hợp đồng.
 */
export const VOUCHER_REASON_META: Record<VoucherReasonCode, VoucherReasonMeta> = {
  NotFound: { label: 'Mã voucher không tồn tại.' },
  Inactive: { label: 'Mã voucher đã ngừng áp dụng.' },
  NotStarted: { label: 'Mã voucher chưa tới thời gian áp dụng.' },
  Expired: { label: 'Mã voucher đã hết hiệu lực.' },
  OutOfStock: { label: 'Mã voucher đã hết lượt sử dụng.' },
  WrongRoute: { label: 'Mã voucher không áp dụng cho tuyến này.' },
  BelowMinOrder: { label: 'Đơn hàng chưa đạt giá trị tối thiểu để dùng mã này.' },
};

export interface VoucherApi {
  /** Danh sách voucher + tìm kiếm + lọc trạng thái + phân trang (màn quản trị). */
  list: (params: VoucherListParams) => Promise<VoucherListResult>;
  /**
   * Danh sách voucher ĐANG khả dụng cho hành khách (màn "Khuyến mãi", không phân trang).
   * Hiện trả mảng rỗng: hợp đồng chưa có endpoint công khai — xem ghi chú đầu file.
   */
  listAvailable: () => Promise<Voucher[]>;
  create: (payload: VoucherPayload) => Promise<Voucher>;
  update: (id: string, payload: VoucherPayload) => Promise<Voucher>;
  /** Bật / tắt áp dụng voucher — trả về voucher đã đổi trạng thái. */
  updateStatus: (id: string, status: VoucherStatus) => Promise<Voucher>;
  /** Kiểm tra mã và tính số tiền được giảm cho một đơn (xem trước, chưa chốt). */
  validate: (request: VoucherValidateRequest) => Promise<VoucherValidationResponse>;
}

// ---------------------------------------------------------------------------
// Dữ liệu giả cho màn "Khuyến mãi" của khách. Hợp đồng chưa có endpoint công khai "voucher
// đang khả dụng" nên `listAvailable` dùng danh sách giả TẤT ĐỊNH (không Math.random) để mỗi lần
// mở màn hình, thứ tự và số liệu voucher vẫn ổn định. Ngày hiệu lực tính tương đối theo hôm nay
// để mã luôn "đang khả dụng" khi demo. Khi backend bổ sung endpoint thì lật USE_MOCK_AVAILABLE.
const USE_MOCK_AVAILABLE = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** ISO 8601 UTC của ngày cách hôm nay `days` ngày — âm = quá khứ, dương = tương lai. */
function isoInDays(days: number): string {
  return dayjs().add(days, 'day').toISOString();
}

/** Danh sách voucher khả dụng giả — gồm mã áp dụng mọi tuyến lẫn mã gắn riêng từng tuyến. Tuyến
 *  gắn là các tuyến xe buýt nội tỉnh Thái Nguyên (Quyết định 2809/QĐ-UBND ngày 9/11/2023). */
function buildMockAvailableVouchers(): Voucher[] {
  return [
    {
      id: 'voucher-001',
      code: 'SUMMER10',
      name: 'Chào hè — giảm 10% mọi tuyến',
      discountType: 'Percent',
      discountValue: 10,
      minOrderValue: 50_000,
      maxDiscount: 20_000,
      routeId: null,
      routeCode: null,
      routeName: null,
      quantity: 500,
      usedCount: 132,
      validFrom: isoInDays(-10),
      validUntil: isoInDays(50),
      status: 'Active',
      createdAt: isoInDays(-15),
      updatedAt: null,
    },
    {
      id: 'voucher-002',
      code: 'TANLONG15',
      name: 'Tuyến Tân Long – Phổ Yên giảm 15.000đ',
      discountType: 'FixedAmount',
      discountValue: 15_000,
      minOrderValue: 30_000,
      maxDiscount: null,
      routeId: 'route-01',
      routeCode: '01',
      routeName: 'Tân Long — Phổ Yên',
      quantity: 200,
      usedCount: 54,
      validFrom: isoInDays(-5),
      validUntil: isoInDays(40),
      status: 'Active',
      createdAt: isoInDays(-8),
      updatedAt: null,
    },
    {
      id: 'voucher-003',
      code: 'YENLANG25',
      name: 'Tuyến Yên Lãng – Gang Thép giảm 25%',
      discountType: 'Percent',
      discountValue: 25,
      minOrderValue: 0,
      maxDiscount: 40_000,
      routeId: 'route-02',
      routeCode: '02',
      routeName: 'Yên Lãng — Gang Thép (TP Thái Nguyên)',
      quantity: 150,
      usedCount: 41,
      validFrom: isoInDays(-20),
      validUntil: isoInDays(25),
      status: 'Active',
      createdAt: isoInDays(-25),
      updatedAt: null,
    },
    {
      id: 'voucher-004',
      code: 'STUDENT20',
      name: 'Sinh viên — giảm 20%',
      discountType: 'Percent',
      discountValue: 20,
      minOrderValue: 20_000,
      maxDiscount: 15_000,
      routeId: null,
      routeCode: null,
      routeName: null,
      quantity: 1000,
      usedCount: 388,
      validFrom: isoInDays(-30),
      validUntil: isoInDays(60),
      status: 'Active',
      createdAt: isoInDays(-35),
      updatedAt: null,
    },
    {
      id: 'voucher-005',
      code: 'QUANCHU15',
      name: 'Tuyến Thái Nguyên – Quân Chu giảm 15%',
      discountType: 'Percent',
      discountValue: 15,
      minOrderValue: 30_000,
      maxDiscount: 25_000,
      routeId: 'route-03',
      routeCode: '03',
      routeName: 'TP Thái Nguyên — Quân Chu (Đại Từ)',
      quantity: 300,
      usedCount: 97,
      validFrom: isoInDays(-12),
      validUntil: isoInDays(35),
      status: 'Active',
      createdAt: isoInDays(-14),
      updatedAt: null,
    },
    {
      id: 'voucher-006',
      code: 'DINHHOA10',
      name: 'Tuyến Thái Nguyên – Định Hóa giảm 10.000đ',
      discountType: 'FixedAmount',
      discountValue: 10_000,
      minOrderValue: 25_000,
      maxDiscount: null,
      routeId: 'route-06',
      routeCode: '06',
      routeName: 'TP Thái Nguyên — Định Hóa',
      quantity: 250,
      usedCount: 60,
      validFrom: isoInDays(-8),
      validUntil: isoInDays(30),
      status: 'Active',
      createdAt: isoInDays(-10),
      updatedAt: null,
    },
    {
      id: 'voucher-007',
      code: 'LOYAL5',
      name: 'Khách quen — giảm 5.000đ mọi tuyến',
      discountType: 'FixedAmount',
      discountValue: 5_000,
      minOrderValue: 25_000,
      maxDiscount: null,
      routeId: null,
      routeCode: null,
      routeName: null,
      quantity: 800,
      usedCount: 210,
      validFrom: isoInDays(-25),
      validUntil: isoInDays(45),
      status: 'Active',
      createdAt: isoInDays(-28),
      updatedAt: null,
    },
    {
      id: 'voucher-008',
      code: 'DINHCA12',
      name: 'Tuyến Quyết Thắng – Đình Cả giảm 12%',
      discountType: 'Percent',
      discountValue: 12,
      minOrderValue: 0,
      maxDiscount: 18_000,
      routeId: 'route-07',
      routeCode: '07',
      routeName: 'Quyết Thắng — Đình Cả (Võ Nhai)',
      quantity: 180,
      usedCount: 33,
      validFrom: isoInDays(-6),
      validUntil: isoInDays(55),
      status: 'Active',
      createdAt: isoInDays(-7),
      updatedAt: null,
    },
  ];
}

async function mockListAvailable(): Promise<Voucher[]> {
  await delay(300);
  return buildMockAvailableVouchers();
}

// Gọi thẳng API thật — `search`/`status` bỏ trống thì axios tự lược khỏi query string,
// backend nhận null = không lọc (ListVouchersRequest mọi trường đều nullable).
const voucherApi: VoucherApi = {
  // GET /api/vouchers?search=&status=&page=&pageSize=
  list: (params) =>
    axiosClient.get<VoucherListResult, VoucherListResult>('/vouchers', { params }),

  // Chưa có endpoint công khai trong hợp đồng → dùng dữ liệu giả (xem USE_MOCK_AVAILABLE ở trên).
  listAvailable: USE_MOCK_AVAILABLE ? mockListAvailable : async () => [],

  // POST /api/vouchers — trùng mã là 409 (message từ backend hiện qua AppError).
  create: (payload) => axiosClient.post<Voucher, Voucher>('/vouchers', payload),

  // PUT /api/vouchers/{id}
  update: (id, payload) => axiosClient.put<Voucher, Voucher>(`/vouchers/${id}`, payload),

  // PATCH /api/vouchers/{id}/status — body đúng một trường status (UpdateVoucherStatusRequest).
  updateStatus: (id, status) =>
    axiosClient.patch<Voucher, Voucher>(`/vouchers/${id}/status`, { status }),

  // POST /api/vouchers/validate — mã không dùng được vẫn trả HTTP 200 kèm valid:false.
  validate: (request) =>
    axiosClient.post<VoucherValidationResponse, VoucherValidationResponse>(
      '/vouchers/validate',
      request,
    ),
};

export default voucherApi;
