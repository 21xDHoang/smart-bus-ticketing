import axiosClient from './axiosClient';
import type { SeatHoldSession } from './seatHoldApi';

// -----------------------------------------------------------------------------
// API nhả ghế khi hết hạn — endpoint POST /seat-holds/{sessionCode}/release (US 3).
//
// Backend ĐÃ có (SeatHoldReleaseController — Phùng Duy Hoàng, task *"API nhả ghế khi hết hạn
// hoặc khách huỷ thao tác"*). Hợp đồng đã chốt trong docs/api-contract.md, mục "Giữ chỗ —
// /seat-holds". Đây là endpoint mà màn hình gọi khi đồng hồ đếm ngược về 0 mà phiên vẫn đang
// mở (task *"Tự động nhả ghế và quay về trang chọn ghế khi hết hạn"* — Dương Thị Hạnh): trả
// ghế về sơ đồ ngay, không đợi job nền SeatHoldExpiryBackgroundService quét.
//
// File này là module API RIÊNG của task nhả ghế — không sửa seatHoldApi.ts (file dùng chung
// của Băng, đang có getSession/extend). Khi Băng nối luồng giữ chỗ vào màn sơ đồ ghế, chỉ cần
// đổi cờ USE_MOCK_DATA ở đây sang false để gọi endpoint thật.
//
// Nhả là thao tác KẾT THÚC nên idempotent (hợp đồng): gọi lại bao nhiêu lần vẫn 200, kể cả khi
// phiên đã Expired/Released do job nền quét trước vài giây. Vì vậy chỗ gọi nhả không cần lo lỗi
// trùng — đây cũng là lý do hook nhả ghế gọi mà không kiểm tra kết quả.
// -----------------------------------------------------------------------------

/** Bề mặt gọi API nhả ghế — chỉ có một thao tác, đúng phạm vi của task. */
export interface SeatHoldReleaseApi {
  /** Nhả cả phiên giữ chỗ theo mã phiên — trả về phiên với `status = 'Released'`. */
  release: (sessionCode: string) => Promise<SeatHoldSession>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì luồng giữ chỗ chưa nối vào màn
// sơ đồ ghế (chưa có phiên thật để nhả); cùng trạng thái với seatHoldApi.ts. Sau khi Băng nối
// luồng thì đổi xuống `false` — nhánh `api` đã viết sẵn đúng hợp đồng.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (USE_MOCK_DATA = false) --------
const api: SeatHoldReleaseApi = {
  release: (sessionCode) =>
    axiosClient.post<SeatHoldSession, SeatHoldSession>(`/seat-holds/${sessionCode}/release`),
};

// -------- Dữ liệu giả (USE_MOCK_DATA = true) --------
// Trả về một phiên đã nhả để hook nhả ghế có gì để gọi. Giá trị trả về không được dùng — việc
// nhả là best-effort, màn hình quay về chọn ghế bất kể kết quả — nên chỉ cần đủ hình dạng.
const mock: SeatHoldReleaseApi = {
  async release(sessionCode) {
    await delay(300);
    return {
      sessionCode,
      tripId: 'trip-demo-1',
      seatNumbers: ['A1', 'A2'],
      status: 'Released',
      expiresAt: new Date().toISOString(),
      canExtend: false,
    };
  },
};

const seatHoldReleaseApi: SeatHoldReleaseApi = USE_MOCK_DATA ? mock : api;

export default seatHoldReleaseApi;
