// -----------------------------------------------------------------------------
// API quản lý voucher — nối với nhóm endpoint của User Story 18 "Quản lý Voucher" (Sprint 5).
//
// Backend CHƯA có bảng Vouchers và chưa có endpoint voucher nào trong docs/api-contract.md,
// nên file này chạy dữ liệu giả (USE_MOCK_DATA = true) — cùng lối seatLayoutApi.ts /
// paymentApi.ts đã làm khi backend chưa sẵn sàng. Khi Dăm migrate xong bảng Vouchers và
// Hiếu chốt hợp đồng, chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Hiếu chốt vào api-contract.md — CHƯA cam kết):
//   GET    /vouchers            → { items, total, page, pageSize }  (lọc theo search + status)
//   POST   /vouchers            → Voucher  (thêm voucher)
//   PUT    /vouchers/{id}       → Voucher  (sửa voucher)
//   PATCH  /vouchers/{id}/status → Voucher (bật/tắt áp dụng)
// -----------------------------------------------------------------------------

/** Trạng thái voucher — lưu chuỗi theo quy ước A3, khớp enum backend sau này. */
export type VoucherStatus = 'Active' | 'Inactive';

/** Kiểu giảm giá — theo phần trăm hay số tiền cố định. */
export type VoucherDiscountType = 'Percent' | 'FixedAmount';

/** Một voucher (mã giảm giá) — khớp hình dạng dự kiến của bảng Vouchers. */
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

export interface VoucherApi {
  /** Danh sách voucher + tìm kiếm + lọc trạng thái + phân trang. */
  list: (params: VoucherListParams) => Promise<VoucherListResult>;
  create: (payload: VoucherPayload) => Promise<Voucher>;
  update: (id: string, payload: VoucherPayload) => Promise<Voucher>;
  /** Bật / tắt áp dụng voucher — trả về voucher đã đổi trạng thái. */
  updateStatus: (id: string, status: VoucherStatus) => Promise<Voucher>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì backend Vouchers chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có endpoint voucher nên nhánh này tạm chưa viết: nối axiosClient vào các
// endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: VoucherApi = {
  async list() {
    throw new Error('Backend Vouchers chưa có — chưa thể tải danh sách voucher thật.');
  },
  async create() {
    throw new Error('Backend Vouchers chưa có — chưa thể thêm voucher thật.');
  },
  async update() {
    throw new Error('Backend Vouchers chưa có — chưa thể cập nhật voucher thật.');
  },
  async updateStatus() {
    throw new Error('Backend Vouchers chưa có — chưa thể đổi trạng thái voucher thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Mốc thời gian giả — `daysFromNow` âm là quá khứ, dương là tương lai. */
function mockIsoDate(daysFromNow: number): string {
  const date = new Date();
  date.setDate(date.getDate() + daysFromNow);
  date.setHours(0, 0, 0, 0);
  return date.toISOString();
}

function mockCreatedAt(daysAgo: number): string {
  const date = new Date();
  date.setDate(date.getDate() - daysAgo);
  return date.toISOString();
}

/** Dựng một voucher giả hoàn chỉnh. */
function buildMockVoucher(
  id: string,
  code: string,
  name: string,
  discountType: VoucherDiscountType,
  discountValue: number,
  minOrderValue: number,
  maxDiscount: number | null,
  quantity: number,
  usedCount: number,
  validFromOffset: number,
  validUntilOffset: number,
  status: VoucherStatus,
  createdDaysAgo: number,
  updatedDaysAgo: number | null,
): Voucher {
  return {
    id,
    code,
    name,
    discountType,
    discountValue,
    minOrderValue,
    maxDiscount,
    quantity,
    usedCount,
    validFrom: mockIsoDate(validFromOffset),
    validUntil: mockIsoDate(validUntilOffset),
    status,
    createdAt: mockCreatedAt(createdDaysAgo),
    updatedAt: updatedDaysAgo === null ? null : mockCreatedAt(updatedDaysAgo),
  };
}

/** Kho dữ liệu giả — biến module để thêm/sửa/đổi trạng thái vẫn "nhớ" như backend thật. */
let MOCK_VOUCHERS: Voucher[] = [
  buildMockVoucher('vc-1', 'SUMMER10', 'Giảm giá mùa hè 10%', 'Percent', 10, 0, 20000, 500, 128, -10, 20, 'Active', 30, 5),
  buildMockVoucher('vc-2', 'NEWUSER20', 'Ưu đãi khách hàng mới 20%', 'Percent', 20, 0, 30000, 1000, 342, -30, 60, 'Active', 25, null),
  buildMockVoucher('vc-3', 'FIX20K', 'Giảm 20.000đ cho đơn từ 100.000đ', 'FixedAmount', 20000, 100000, null, 300, 45, -5, 15, 'Active', 12, 3),
  buildMockVoucher('vc-4', 'STUDENT15', 'Học sinh - sinh viên giảm 15%', 'Percent', 15, 0, 15000, 800, 211, -20, 40, 'Active', 40, 8),
  buildMockVoucher('vc-5', 'VIP10K', 'Khách VIP giảm 10.000đ', 'FixedAmount', 10000, 0, null, 150, 87, -15, 25, 'Active', 18, null),
  buildMockVoucher('vc-6', 'WEEKEND5K', 'Cuối tuần giảm 5.000đ', 'FixedAmount', 5000, 50000, null, 400, 0, -1, 30, 'Inactive', 9, 2),
  buildMockVoucher('vc-7', 'RAINY25', 'Ngày mưa giảm 25%', 'Percent', 25, 100000, 25000, 250, 12, 1, 45, 'Active', 6, null),
  buildMockVoucher('vc-8', 'TET30', 'Mừng Tết giảm 30%', 'Percent', 30, 200000, 50000, 200, 200, -60, -10, 'Inactive', 55, null),
];

function generateId(): string {
  return `voucher-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

/** Kiểm tra trùng mã — bỏ qua chính voucher đang sửa. */
function codeExists(code: string, ignoreId?: string): boolean {
  const normalized = code.trim().toUpperCase();
  return MOCK_VOUCHERS.some(
    (voucher) => voucher.id !== ignoreId && voucher.code.toUpperCase() === normalized,
  );
}

const mock: VoucherApi = {
  async list({ search, status, page, pageSize }) {
    await delay(300);

    const needle = (search ?? '').trim().toLowerCase();
    const filtered = MOCK_VOUCHERS.filter((voucher) => {
      const matchSearch =
        !needle ||
        voucher.code.toLowerCase().includes(needle) ||
        voucher.name.toLowerCase().includes(needle);
      const matchStatus = !status || voucher.status === status;
      return matchSearch && matchStatus;
    })
      // Mới tạo trước — cùng lối thứ tự sắp xếp `createdAt` giảm dần của mọi màn quản trị.
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt));

    const start = (page - 1) * pageSize;
    return {
      items: filtered.slice(start, start + pageSize).map((voucher) => ({ ...voucher })),
      total: filtered.length,
      page,
      pageSize,
    };
  },

  async create(payload) {
    await delay(400);

    if (codeExists(payload.code)) {
      throw new Error('Mã voucher đã tồn tại.');
    }

    const voucher: Voucher = {
      id: generateId(),
      code: payload.code.trim().toUpperCase(),
      name: payload.name.trim(),
      discountType: payload.discountType,
      discountValue: payload.discountValue,
      minOrderValue: payload.minOrderValue,
      maxDiscount: payload.discountType === 'Percent' ? (payload.maxDiscount ?? null) : null,
      quantity: payload.quantity,
      usedCount: 0,
      validFrom: payload.validFrom,
      validUntil: payload.validUntil,
      status: 'Active',
      createdAt: new Date().toISOString(),
      updatedAt: null,
    };

    MOCK_VOUCHERS = [voucher, ...MOCK_VOUCHERS];
    return { ...voucher };
  },

  async update(id, payload) {
    await delay(400);

    const index = MOCK_VOUCHERS.findIndex((voucher) => voucher.id === id);
    if (index === -1) throw new Error('Không tìm thấy voucher cần sửa.');

    if (codeExists(payload.code, id)) {
      throw new Error('Mã voucher đã tồn tại.');
    }

    const updated: Voucher = {
      ...MOCK_VOUCHERS[index],
      code: payload.code.trim().toUpperCase(),
      name: payload.name.trim(),
      discountType: payload.discountType,
      discountValue: payload.discountValue,
      minOrderValue: payload.minOrderValue,
      maxDiscount: payload.discountType === 'Percent' ? (payload.maxDiscount ?? null) : null,
      quantity: payload.quantity,
      validFrom: payload.validFrom,
      validUntil: payload.validUntil,
      updatedAt: new Date().toISOString(),
    };

    MOCK_VOUCHERS = MOCK_VOUCHERS.map((voucher) => (voucher.id === id ? updated : voucher));
    return { ...updated };
  },

  async updateStatus(id, status) {
    await delay(300);

    const index = MOCK_VOUCHERS.findIndex((voucher) => voucher.id === id);
    if (index === -1) throw new Error('Không tìm thấy voucher cần đổi trạng thái.');

    const updated: Voucher = {
      ...MOCK_VOUCHERS[index],
      status,
      updatedAt: new Date().toISOString(),
    };

    MOCK_VOUCHERS = MOCK_VOUCHERS.map((voucher) => (voucher.id === id ? updated : voucher));
    return { ...updated };
  },
};

const voucherApi: VoucherApi = USE_MOCK_DATA ? mock : api;

export default voucherApi;
