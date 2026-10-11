import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API giữ chỗ — nối với nhóm endpoint SeatHolds (US 3 "Tự động giữ chỗ trong 10 phút").
//
// Cả BỐN endpoint của bề mặt đã có trên backend và hợp đồng đã chốt trong docs/api-contract.md,
// mục "Giữ chỗ — /seat-holds":
//   POST /seat-holds                      → 201 SeatHoldSession (tạo phiên — Nguyễn Duy Kiên)
//   GET  /seat-holds/{sessionCode}        → SeatHoldSession     (tra trạng thái — Trần Trung Hiếu)
//   POST /seat-holds/{sessionCode}/extend → SeatHoldSession     (gia hạn, tối đa 1 lần — Hiếu)
//   POST /seat-holds/{sessionCode}/release → SeatHoldSession    (nhả ghế — Phùng Duy Hoàng,
//                                                                ở module riêng seatHoldReleaseApi.ts)
//
// Một phiên giữ NHIỀU ghế = nhiều dòng SeatHold cùng SessionCode (cột KHÔNG unique — xem
// docs/26-csdl-so-do-ghe.md §1). Vì vậy đối tượng trả về cho frontend là MỘT PHIÊN
// (SeatHoldSession) gom các ghế lại, không phải một dòng — mọi thao tác (tra trạng thái,
// gia hạn, đếm ngược, nhả cả phiên) đều hỏi theo SessionCode.
//
// Luồng: màn chọn ghế (SeatMapPage) gọi `create` với cả nhóm ghế khách vừa chọn TRƯỚC khi
// chuyển sang màn chọn phương thức thanh toán, và chuyển theo `sessionCode` vừa sinh; phiên
// sống suốt bước thanh toán (đếm ngược, gia hạn, tự nhả khi hết hạn ở PaymentMethodPage).
//
// "Gia hạn" dùng động từ "extend" chứ không "renew" như vé tháng: vé tháng gia hạn là GHI
// THÊM một dòng mới nối đuôi kỳ cũ, còn giữ chỗ gia hạn là CẬP NHẬT ExpiresAt trên chính các
// dòng đang giữ (docs/26 §1). Khác bản chất nên khác từ.
// -----------------------------------------------------------------------------

/** Trạng thái một lượt giữ ghế — khớp enum SeatHoldStatus của backend (lưu chuỗi, quy ước A3). */
export type SeatHoldStatus = 'Holding' | 'Confirmed' | 'Expired' | 'Released';

/** Thời gian giữ chỗ mặc định — 10 phút (US 3). */
export const SEAT_HOLD_DURATION_SECONDS = 10 * 60;

/**
 * Một phiên giữ chỗ — gom các ghế cùng `SessionCode` thành một đối tượng để hiển thị.
 * `status` và `expiresAt` giống nhau trên mọi dòng cùng phiên nên chỉ cần một chỗ.
 */
export interface SeatHoldSession {
  /** Mã phiên do API giữ ghế sinh ra — mọi thao tác theo phiên đều hỏi theo mã này. */
  sessionCode: string;

  tripId: string;

  /** Số ghế đang giữ trong phiên, ví dụ ["A1", "A2"] — chỉ để hiển thị cho hành khách. */
  seatNumbers: string[];

  status: SeatHoldStatus;

  /** Thời điểm hết hạn giữ chỗ (ISO 8601). */
  expiresAt: string;

  /**
   * Còn lượt gia hạn không — luật "tối đa 1 lần" của US 3. Cột SeatHolds KHÔNG có cột đếm số
   * lần; backend quyết định giá trị này khi trả phiên. Frontend chỉ dùng để tắt nút "Gia hạn"
   * khi đã dùng hết lượt.
   */
  canExtend: boolean;
}

/** Body của POST /seat-holds — đúng hai trường của hợp đồng; người giữ suy từ token đăng nhập. */
export interface SeatHoldCreateRequest {
  tripId: string;

  /** Id các ghế đang chọn trên sơ đồ — KHÔNG phải số ghế hiển thị (seatNumber). */
  seatIds: string[];
}

export interface SeatHoldApi {
  /**
   * Giữ ghế tạm thời: sinh phiên cho NHÓM ghế khách vừa chọn và trả về mã phiên (task của
   * Kiên). Màn chọn ghế gọi đúng một lần với cả nhóm ghế rồi chuyển sang bước thanh toán kèm
   * `sessionCode`. Bấm khi ghế đã bị người khác giữ → lỗi 409 kèm số ghế vướng.
   */
  create: (request: SeatHoldCreateRequest) => Promise<SeatHoldSession>;

  /** Kiểm tra trạng thái giữ chỗ theo mã phiên (task của Hiếu). */
  getSession: (sessionCode: string) => Promise<SeatHoldSession>;

  /** Gia hạn thời gian giữ chỗ — backend chặn lần thứ hai (tối đa 1 lần, US 3). */
  extend: (sessionCode: string) => Promise<SeatHoldSession>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Để `false` — cả bốn endpoint của bề mặt SeatHolds
// đã có trên backend, hợp đồng đã chốt và luồng giữ chỗ đã nối vào màn chọn ghế (create được
// gọi thật). Đổi lên `true` khi cần phát triển giao diện offline (không chạy API).
const USE_MOCK_DATA = false;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: SeatHoldApi = {
  create: (request) => axiosClient.post<SeatHoldSession, SeatHoldSession>('/seat-holds', request),

  getSession: (sessionCode) =>
    axiosClient.get<SeatHoldSession, SeatHoldSession>(`/seat-holds/${sessionCode}`),

  extend: (sessionCode) =>
    axiosClient.post<SeatHoldSession, SeatHoldSession>(`/seat-holds/${sessionCode}/extend`),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
// Một phiên giả trong bộ nhớ để phát triển offline: `create` dựng phiên từ đúng nhóm ghế gửi
// lên (đánh số A1..An), getSession/extend thao tác trên phiên đó — modal "thông báo hết hạn +
// nút gia hạn" có gì để demo. `expiresAt` đặt rất gần hiện tại để mở lên là thấy ngay trạng
// thái "sắp hết hạn".

/** Phiên giả hết hạn sau N giây — đủ gần để demo câu cảnh báo "sắp hết hạn". */
const DEMO_EXPIRES_IN_SECONDS = 30;

function demoSession(): SeatHoldSession {
  return {
    sessionCode: 'hold-demo-1',
    tripId: 'trip-demo-1',
    seatNumbers: ['A1', 'A2'],
    status: 'Holding',
    expiresAt: new Date(Date.now() + DEMO_EXPIRES_IN_SECONDS * 1000).toISOString(),
    canExtend: true,
  };
}

let mockSession: SeatHoldSession | null = null;
let mockSessionCount = 0;

const mock: SeatHoldApi = {
  async create(request) {
    await delay(200);
    mockSessionCount += 1;
    mockSession = {
      sessionCode: `PHIEN-demo-${mockSessionCount}`,
      tripId: request.tripId,
      seatNumbers: request.seatIds.map((_, index) => `A${index + 1}`),
      status: 'Holding',
      expiresAt: new Date(Date.now() + DEMO_EXPIRES_IN_SECONDS * 1000).toISOString(),
      canExtend: true,
    };
    return { ...mockSession };
  },

  async getSession() {
    await delay(200);
    mockSession ??= demoSession();
    return { ...mockSession };
  },

  async extend() {
    await delay(500);
    mockSession ??= demoSession();

    // Bắt chước đúng luật "tối đa 1 lần" của backend: hết lượt thì từ chối như lỗi 409
    // (quy ước D2). Bản thật do backend trả 409, nhánh giả ném Error cùng nội dung.
    if (!mockSession.canExtend) {
      throw new Error('Phiên giữ chỗ đã gia hạn tối đa 1 lần.');
    }

    mockSession = {
      ...mockSession,
      expiresAt: new Date(Date.now() + SEAT_HOLD_DURATION_SECONDS * 1000).toISOString(),
      canExtend: false,
    };

    return { ...mockSession };
  },
};

const seatHoldApi: SeatHoldApi = USE_MOCK_DATA ? mock : api;

export default seatHoldApi;
