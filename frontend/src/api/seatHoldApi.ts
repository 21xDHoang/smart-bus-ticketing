import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API giữ chỗ — nối với nhóm endpoint SeatHolds (US 3 "Tự động giữ chỗ trong 10 phút").
//
// Backend CHƯA có endpoint giữ chỗ — "API kiểm tra trạng thái giữ chỗ theo mã phiên" và
// "API gia hạn thời gian giữ chỗ" là task của Trần Trung Hiếu, hợp đồng chưa vào
// docs/api-contract.md. Nhưng Dăm ĐÃ migrate xong bảng SeatHolds (docs/26-csdl-so-do-ghe.md),
// nên kiểu dưới đây bám theo đúng cột thật của entity để sau chỉ đổi cờ USE_MOCK_DATA:
//   SeatHold: (TripId, SeatId, UserId) + SessionCode + Status + ExpiresAt
//
// Một phiên giữ NHIỀU ghế = nhiều dòng SeatHold cùng SessionCode (cột KHÔNG unique — xem
// docs/26-csdl-so-do-ghe.md §1). Vì vậy đối tượng trả về cho frontend là MỘT PHIÊN
// (SeatHoldSession) gom các ghế lại, không phải một dòng — mọi thao tác (tra trạng thái,
// gia hạn, đếm ngược, nhả cả phiên) đều hỏi theo SessionCode.
//
// Hợp đồng DỰ KIẾN — chưa chốt, đặt kebab-case theo quy ước D1:
//   GET  /seat-holds/{sessionCode}        → SeatHoldSession  (kiểm tra trạng thái theo mã phiên)
//   POST /seat-holds/{sessionCode}/extend → SeatHoldSession  (gia hạn thời gian giữ chỗ, tối đa 1 lần)
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
   * Còn lượt gia hạn không. Cột SeatHolds KHÔNG có cột đếm số lần — giá trị này do backend
   * tính khi trả phiên (ví dụ so `expiresAt` với mốc giữ ban đầu, hoặc bổ sung cột sau).
   * Frontend chỉ dùng để tắt nút "Gia hạn" khi đã dùng hết lượt.
   */
  canExtend: boolean;
}

export interface SeatHoldApi {
  /** Kiểm tra trạng thái giữ chỗ theo mã phiên (task của Hiếu). */
  getSession: (sessionCode: string) => Promise<SeatHoldSession>;

  /** Gia hạn thời gian giữ chỗ — backend chặn lần thứ hai (tối đa 1 lần, US 3). */
  extend: (sessionCode: string) => Promise<SeatHoldSession>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì endpoint chưa có (xem ghi chú
// đầu file); sau khi Hiếu chốt hợp đồng thì đổi xuống `false` — nhánh `api` đã viết sẵn.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: SeatHoldApi = {
  getSession: (sessionCode) =>
    axiosClient.get<SeatHoldSession, SeatHoldSession>(`/seat-holds/${sessionCode}`),

  extend: (sessionCode) =>
    axiosClient.post<SeatHoldSession, SeatHoldSession>(`/seat-holds/${sessionCode}/extend`),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
// Một phiên giả trong bộ nhớ để modal "thông báo hết hạn giữ chỗ + nút gia hạn" có gì để demo.
// `expiresAt` đặt rất gần hiện tại để mở lên là thấy ngay trạng thái "sắp hết hạn".

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

const mock: SeatHoldApi = {
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
