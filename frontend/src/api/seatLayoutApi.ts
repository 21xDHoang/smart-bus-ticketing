import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API cấu hình sơ đồ ghế theo loại xe — nối với SeatLayoutsController (story 2
// "Cấu hình sơ đồ ghế theo loại xe (số tầng, số ghế, ghế VIP)", Sprint 3).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Sơ đồ ghế — /seat-layouts":
//   GET    /seat-layouts?search=  → danh sách sơ đồ (không phân trang)
//   POST   /seat-layouts          → tạo sơ đồ + sinh ghế
//   PUT    /seat-layouts/{id}     → sửa sơ đồ + đồng bộ ghế
//   DELETE /seat-layouts/{id}     → xoá sơ đồ (204)
//
// KHÁC các module CRUD khác ở một điểm: lưu sơ đồ KHÔNG chỉ ghi một dòng — backend sinh
// luôn ghế thật cho mọi xe cùng `busType` đang chưa có ghế (mục "Lưu sơ đồ là ghế có ngay
// (tự sinh)" của hợp đồng). Vì vậy response của POST/PUT mang thêm `busesSeated`. Giao diện
// không bao giờ tự dựng danh sách ghế rồi gửi lên — ghế thuộc về XE, không thuộc sơ đồ.
// -----------------------------------------------------------------------------

/** Trần các tham số của một sơ đồ ghế — khớp ràng buộc dự kiến của màn hình. */
export const SEAT_LAYOUT_LIMITS = {
  /** Xe buýt thành phố tối đa 2 tầng. */
  maxFloors: 2,
  /** Mỗi tầng tối đa 13 hàng → mỗi tầng đủ bảng chữ cái A..M đặt tên hàng. */
  maxRowsPerFloor: 13,
  /** Số ghế thực mỗi hàng tối đa 6 (lối đi giữa chỉ là khoảng trống vẽ, không phải cột). */
  maxColumnsPerRow: 6,
} as const;

/**
 * Một sơ đồ ghế theo loại xe — khớp nguyên hình dạng `SeatLayoutResponse` của backend
 * (KHÔNG có `seats[]`: ghế thuộc về từng xe, xem cột Seats của entity Seat).
 */
export interface SeatLayout {
  id: string;

  /** Loại xe áp dụng — "Xe buýt 45 chỗ", "Xe buýt điện". Khoá nghiệp vụ: mỗi loại xe một sơ đồ. */
  busType: string;

  /** Số tầng của xe — 1 hoặc 2. */
  numberOfFloors: number;

  /** Số hàng ghế trên mỗi tầng. */
  rowsPerFloor: number;

  /** Số ghế trên mỗi hàng. */
  columnsPerRow: number;

  /** Tổng số ghế = số tầng × số hàng × số cột — backend kiểm khớp sức chứa của mọi xe cùng loại. */
  totalSeats: number;

  /** Toạ độ ghế VIP, mỗi phần tử "tầng-hàng-cột" đếm từ 1 ("1-1-1" = tầng 1, hàng 1, cột 1). */
  vipSeatPositions: string[];

  /**
   * Số xe vừa được SINH GHẾ trong lượt lưu này. Chỉ có nghĩa ở response của POST/PUT —
   * GET luôn trả 0 vì lúc đọc thì không sinh gì cả.
   */
  busesSeated: number;

  createdAt: string;

  /** null khi chưa sửa lần nào. */
  updatedAt: string | null;
}

/** Body khi thêm/sửa sơ đồ ghế. Ghế không gửi lên — backend tự sinh từ lưới + tập VIP. */
export interface SeatLayoutPayload {
  busType: string;
  numberOfFloors: number;
  rowsPerFloor: number;
  columnsPerRow: number;
  vipSeatPositions: string[];
}

/** Chữ cái của một hàng (0 → "A", 25 → "Z"). */
export function rowLetter(rowIndex: number): string {
  return String.fromCharCode(65 + rowIndex);
}

/**
 * Nhãn ghế hiển thị — tầng 1 hàng 1 cột 1 của xe một tầng → "A1"; xe HAI TẦNG thêm tiền tố
 * tầng → "T1-A1", "T2-A1".
 *
 * Chép đúng luật đánh mã của backend (MaGhe ở SeatLayoutService/SampleDataSeeder): hàng đánh
 * lại từ A ở mỗi tầng, nên xe hai tầng BẮT BUỘC có tiền tố tầng — không thì hai ghế khác
 * nhau cùng ra nhãn "A1", mà (BusId, SeatNumber) là duy nhất. Tham số đếm từ 1 như toạ độ
 * trong CSDL, và phải truyền đúng `numberOfFloors` của sơ đồ để biết có tiền tố hay không.
 */
export function seatLabel(
  floor: number,
  rowIndex: number,
  columnIndex: number,
  numberOfFloors: number,
): string {
  const nhan = `${rowLetter(rowIndex - 1)}${columnIndex}`;
  return numberOfFloors > 1 ? `T${floor}-${nhan}` : nhan;
}

/** Khoá định danh một ghế trong lưới — "1-2-3" = tầng 1, hàng 2, cột 3 (đều đếm từ 1). */
export function seatKey(floor: number, rowIndex: number, columnIndex: number): string {
  return `${floor}-${rowIndex}-${columnIndex}`;
}

/** Toàn bộ khoá ghế hợp lệ của một lưới — để lọc ghế VIP đã chọn nhưng không còn tồn tại. */
export function seatKeysOf(
  numberOfFloors: number,
  rowsPerFloor: number,
  columnsPerRow: number,
): string[] {
  const keys: string[] = [];

  for (let floor = 1; floor <= numberOfFloors; floor += 1) {
    for (let row = 1; row <= rowsPerFloor; row += 1) {
      for (let col = 1; col <= columnsPerRow; col += 1) {
        keys.push(seatKey(floor, row, col));
      }
    }
  }

  return keys;
}

/** Từ một sơ đồ đã lưu, lấy kích thước lưới + tập ghế VIP để nạp vào form khi sửa. */
export function layoutGridConfig(
  layout: Pick<SeatLayout, 'rowsPerFloor' | 'columnsPerRow' | 'vipSeatPositions'>,
): {
  rowsPerFloor: number;
  columnsPerRow: number;
  vipKeys: string[];
} {
  return {
    rowsPerFloor: layout.rowsPerFloor,
    columnsPerRow: layout.columnsPerRow,
    vipKeys: layout.vipSeatPositions,
  };
}

/** Số ghế VIP của một sơ đồ. */
export function vipSeatCount(layout: Pick<SeatLayout, 'vipSeatPositions'>): number {
  return layout.vipSeatPositions.length;
}

export interface SeatLayoutApi {
  /** Danh sách sơ đồ ghế, lọc theo loại xe (không phân biệt hoa thường). */
  list: (search?: string) => Promise<SeatLayout[]>;
  create: (payload: SeatLayoutPayload) => Promise<SeatLayout>;
  /**
   * Sửa sơ đồ — KHÔNG gửi `busType`: loại xe là khoá nghiệp vụ (unique index), đổi khoá là
   * xoá-rồi-tạo nên hợp đồng cố ý bỏ khỏi body sửa (cùng lối UpdateFareRequest).
   */
  update: (id: string, payload: Omit<SeatLayoutPayload, 'busType'>) => Promise<SeatLayout>;
  /** Xoá sơ đồ — 204 không body; còn ghế trỏ về thì backend trả 409. */
  remove: (id: string) => Promise<void>;
}

const seatLayoutApi: SeatLayoutApi = {
  // GET /api/seat-layouts?search= — chỉ gửi tham số khi thực sự có từ khóa.
  list: (search) =>
    axiosClient.get<SeatLayout[], SeatLayout[]>('/seat-layouts', {
      params: search ? { search } : {},
    }),

  // POST /api/seat-layouts — 201 + Location, response mang `busesSeated`.
  create: (payload) => axiosClient.post<SeatLayout, SeatLayout>('/seat-layouts', payload),

  // PUT /api/seat-layouts/{id} — 409 khi sơ đồ đang có ghế bị giữ chỗ/bán.
  update: (id, payload) => axiosClient.put<SeatLayout, SeatLayout>(`/seat-layouts/${id}`, payload),

  // DELETE /api/seat-layouts/{id} — 204 không body (cùng lối /stops, /fares).
  remove: (id) => axiosClient.delete<never, void>(`/seat-layouts/${id}`),
};

export default seatLayoutApi;
