import type { TagProps } from 'antd';

// -----------------------------------------------------------------------------
// API soát vé — nối với story 15 "Soát vé QR" (Sprint 4), phục vụ task "Màn hình quét QR
// bằng camera thiết bị di động" (Nguyễn Đình Băng).
//
// Backend CHƯA có endpoint soát vé (docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md là BÀN GIAO
// chờ Trần Trung Hiếu chốt + code — task Sprint 4 dòng 15 "API soát vé: nhận mã QR, xác thực
// chữ ký, trả kết quả hợp lệ/không"). Nên file này chạy dữ liệu giả (USE_MOCK_DATA = true) —
// cùng lối ticketApi.ts / ticketRequestApi.ts khi backend chưa sẵn sàng. Khi contract có, viết
// thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (docs/29 §3 — CHƯA cam kết, Hiếu chốt):
//   POST /tickets/validate  body { code, tripId? }
//   → 200 { valid, reason, ticket }   (hợp lệ hay không đều là 200, không phải 4xx)
//
// Mã QR vé là chuỗi ký số dạng "SBT1:{ticketId}.{blob}" (docs/30-service-ma-qr-ky-so.md) —
// máy quét chỉ gửi nguyên chuỗi lên, server tự kiểm chữ ký + tra vé.
// -----------------------------------------------------------------------------

/** Lý do vé không hợp lệ — khớp `reason` trong đề xuất hợp đồng docs/29 §3. */
export type TicketValidationReason =
  | 'NotFound'
  | 'Cancelled'
  | 'AlreadyUsed'
  | 'WrongTrip'
  | 'Expired';

/** Vé được soát — chỉ có khi `valid = true` (hoặc `reason` cần kèm vé để đối chiếu). */
export interface ValidatedTicket {
  id: string;

  /** Mã vé = nội dung mã QR. */
  code: string;

  routeCode: string;

  routeName: string;

  /** Giờ khởi hành của chuyến (ISO 8601). */
  departureTime: string;

  seatNumber: string;

  /** Tên trạm lên — để phụ xe đối chiếu khách lên đúng trạm. */
  boardingStopName: string;

  /** Tên trạm xuống. */
  alightingStopName: string;

  /** Họ tên hành khách — ghép từ Users, null khi không tìm thấy tài khoản. */
  passengerName: string | null;

  status: string;
}

/** Kết quả một lượt soát vé — "vé không hợp lệ" cũng là câu trả lời thành công, không phải lỗi. */
export interface TicketValidationResult {
  valid: boolean;

  reason: TicketValidationReason | null;

  /** Vé soát được. null khi `reason = 'NotFound'` (không tra ra vé). */
  ticket: ValidatedTicket | null;
}

interface ReasonMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: TagProps['color'];
}

/** Nhãn + màu cho từng lý do không hợp lệ — để màn hình hiện một câu rõ ràng, không in mã thô. */
export const TICKET_VALIDATION_REASON_META: Record<TicketValidationReason, ReasonMeta> = {
  NotFound: { label: 'Mã vé không tồn tại', color: 'red' },
  Cancelled: { label: 'Vé đã bị hủy', color: 'volcano' },
  AlreadyUsed: { label: 'Vé đã được soát rồi', color: 'orange' },
  WrongTrip: { label: 'Vé không thuộc chuyến này', color: 'gold' },
  Expired: { label: 'Vé đã hết hiệu lực', color: 'default' },
};

/** Lấy nhãn + màu cho một mã lý do; mã lạ (backend bổ sung sau) vẫn hiển thị được. */
export function getTicketValidationReasonMeta(reason: string): ReasonMeta {
  return TICKET_VALIDATION_REASON_META[reason as TicketValidationReason] ?? {
    label: reason,
    color: 'default',
  };
}

export interface TicketValidationApi {
  /** Soát một mã QR vé: trả kết quả hợp lệ/không kèm vé (nếu tra được). */
  validate: (code: string) => Promise<TicketValidationResult>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — endpoint soát vé chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có POST /tickets/validate nên nhánh này tạm chưa viết: nối axiosClient vào
// endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: TicketValidationApi = {
  async validate() {
    throw new Error('Backend soát vé chưa có — chưa thể kiểm tra mã QR thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Mốc giờ giả — `offsetHours` dương là tương lai, âm là quá khứ. */
function mockDeparture(offsetHours: number): string {
  const date = new Date();
  date.setHours(date.getHours() + offsetHours, 30, 0, 0);
  return date.toISOString();
}

/** Dựng một vé giả từ mã QR — tên tuyến/ghế cố định để demo, id lấy từ chuỗi mã. */
function mockTicket(code: string): ValidatedTicket {
  // "SBT1:{guid}.{blob}" — phần giữa tiền tố và dấu chấm là ticketId.
  const ticketId = code.slice(5, code.indexOf('.'));

  return {
    id: ticketId,
    code,
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    departureTime: mockDeparture(2),
    seatNumber: 'A12',
    boardingStopName: 'Bến xe Mỹ Đình',
    alightingStopName: 'Bến xe Gia Lâm',
    passengerName: 'Nguyễn Văn Khách',
    status: 'Paid',
  };
}

const mock: TicketValidationApi = {
  async validate(code) {
    await delay(500);

    // Mã vé thật có tiền tố "SBT1:" (docs/30); chuỗi khác coi như không tồn tại. Đủ để demo
    // luồng quét → hợp lệ / không hợp lệ. Các lý do khác (đã dùng, đã huỷ…) đợi backend thật.
    if (code.startsWith('SBT1:')) {
      return { valid: true, reason: null, ticket: mockTicket(code) };
    }

    return { valid: false, reason: 'NotFound', ticket: null };
  },
};

const ticketValidationApi: TicketValidationApi = USE_MOCK_DATA ? mock : api;

export default ticketValidationApi;
