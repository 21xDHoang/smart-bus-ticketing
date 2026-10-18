import axiosClient from './axiosClient';
import type { AppError } from './axiosClient';

// -----------------------------------------------------------------------------
// API "Vé của tôi" — vé điện tử (story 4 "Vé điện tử QR").
//
// Hai endpoint thật của backend (docs/api-contract.md, mục "Vé điện tử — /tickets"):
//   GET /tickets/me   → mọi vé của chính người gọi (Paid / Used / Cancelled), mảng trần
//   GET /tickets/{id} → một vé; vé của người khác hoặc id sai đều trả 404
//
// Phạm vi "của tôi" nằm trong truy vấn theo UserId ở backend — FE không có tham số nào để
// đọc vé người khác. Hình dạng dưới đây khớp `Dtos/Tickets/TicketResponse.cs`; tên tuyến,
// điểm đi/đến, số ghế và tên trạm là tên hiển thị backend nối sẵn, không phải gọi thêm API.
//
// Vé được phát hành khi giao dịch thanh toán Success (IPN hoặc job đối soát), nên danh sách
// này chỉ có vé thật — không còn dữ liệu giả như bản nháp trước đây.
// -----------------------------------------------------------------------------

/** Trạng thái LƯU của vé — do các nghiệp vụ đặt: thanh toán → Paid, soát vé → Used, huỷ → Cancelled. */
export type TicketStatus = 'Paid' | 'Used' | 'Cancelled';

/** Một vé điện tử — khớp `TicketResponse` của backend. */
export interface Ticket {
  id: string;

  /** Mã vé = nội dung mã QR soát vé. Duy nhất toàn hệ thống. */
  code: string;

  /** Mã tuyến hiển thị — "01", "B10". */
  routeCode: string;

  routeName: string;

  origin: string;

  destination: string;

  /** Giờ khởi hành của chuyến (ISO 8601). Căn cứ suy "hết hạn": chuyến đi rồi mà chưa soát vé. */
  departureTime: string;

  seatNumber: string;

  /** Tên trạm lên — null khi vé không gắn trạm cụ thể (lên ở bến đầu). Chỉ để hiển thị (A8.1). */
  boardingStopName: string | null;

  /** Tên trạm xuống — cùng tính chất hiển thị như `boardingStopName`. */
  alightingStopName: string | null;

  /** Giá đã thanh toán (VND) — ảnh chụp lúc phát hành, không tra lại bảng giá. */
  price: number;

  status: TicketStatus;

  /** Thời điểm phát hành (tiền đã về) — FE hiển thị "ngày mua". */
  createdAt: string;

  /** Thời điểm soát vé — chỉ có khi `status = 'Used'`. */
  usedAt: string | null;
}

export interface TicketApi {
  /** Vé điện tử của chính người gọi. Không vé nào → mảng rỗng, không phải 404. */
  list: () => Promise<Ticket[]>;
  /** Một vé theo `id` — để màn hình "Mã QR vé" mở trực tiếp một vé trong danh sách.
      Không tìm thấy (vé không thuộc người gọi, hoặc id sai) → `null`, không phải lỗi. */
  get: (id: string) => Promise<Ticket | null>;
}

const ticketApi: TicketApi = {
  list: () => axiosClient.get<Ticket[], Ticket[]>('/tickets/me'),

  async get(id) {
    try {
      return await axiosClient.get<Ticket, Ticket>(`/tickets/${id}`);
    } catch (err) {
      // 404 là câu trả lời hợp lệ ("không có vé này"), không phải lỗi màn hình: màn gọi
      // phân biệt `null` với lỗi tải để hiện đúng trạng thái.
      if ((err as AppError).status === 404) return null;
      throw err;
    }
  },
};

export default ticketApi;
