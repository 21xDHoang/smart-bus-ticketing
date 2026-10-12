import axiosClient from './axiosClient';

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
// công khai nào cho nó (mọi endpoint voucher đều là quản trị ManagerOrAbove hoặc kiểm-tra-mã), nên
// hàm trả mảng rỗng ĐÚNG SỰ THẬT thay vì bịa mã minh hoạ. Nợ hợp đồng: cần một endpoint công khai
// "voucher đang khả dụng" trước khi màn Khuyến mãi có dữ liệu thật.
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

// Gọi thẳng API thật — `search`/`status` bỏ trống thì axios tự lược khỏi query string,
// backend nhận null = không lọc (ListVouchersRequest mọi trường đều nullable).
const voucherApi: VoucherApi = {
  // GET /api/vouchers?search=&status=&page=&pageSize=
  list: (params) =>
    axiosClient.get<VoucherListResult, VoucherListResult>('/vouchers', { params }),

  // Chưa có endpoint công khai trong hợp đồng (mọi endpoint voucher đều quản trị hoặc
  // kiểm-tra-mã) → trả rỗng đúng sự thật; hai màn gọi hàm này đã tự xử lý mảng rỗng
  // (HomePage ẩn mục khuyến mãi, VoucherAvailablePage hiện trạng thái "chưa có mã").
  listAvailable: async () => [],

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
