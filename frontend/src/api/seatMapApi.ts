import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API sơ đồ ghế — nối với nhóm endpoint của User Story 2 "Chọn vị trí ghế" (Sprint 3).
//
// Backend CHƯA có endpoint "lấy sơ đồ ghế theo chuyến + trạng thái từng ghế" — task của
// Trần Trung Hiếu, hợp đồng chưa vào docs/api-contract.md. Nhưng Dăm ĐÃ migrate xong bảng
// (nhánh feature/2-migrate-seats-seatlayouts-seatholds, docs/26-csdl-so-do-ghe.md), nên kiểu
// dưới đây bám theo đúng cột thật của entity để sau chỉ đổi cờ USE_MOCK_DATA:
//   Seat:   Id, SeatNumber, Floor, RowIndex, ColumnIndex, SeatType (Standard|Vip)
//   SeatLayout: NumberOfFloors, TotalSeats, BusType (mẫu của một LOẠI xe)
//   SeatHold: (TripId, SeatId) + Status Holding → trạng thái "đang giữ" của ghế
//
// Trạng thái ghế (Available/Held/Paid) là SUY RA theo CHUYẾN từ SeatHolds + Tickets — không
// phải cột của Seats. Giá ghế (price) là DỰ KIẾN: phần "giá theo ghế + tổng tiền tạm tính"
// là task của Dương Thị Hạnh, chưa có hợp đồng fare VIP.
//
// Hợp đồng DỰ KIẾN — GET /trips/{tripId}/seats (công khai cho hành khách):
//   {
//     "tripId": "…",
//     "busType": "Hyundai County 29 chỗ",
//     "floors": 1,
//     "pricePerSeat": 8000,      // giá phổ thông của chuyến (VND)
//     "vipSurcharge": 5000,      // phụ trội ghế VIP (VND) — DỰ KIẾN
//     "seats": [ … ]
//   }
// -----------------------------------------------------------------------------

/** Trạng thái một ghế trong một chuyến — suy ra từ SeatHolds/Tickets, chưa chốt tên chính thức. */
export type SeatStatus = 'Available' | 'Held' | 'Paid';

/** Loại ghế — khớp enum SeatType của backend (lưu chuỗi, quy ước A3). */
export type SeatType = 'Standard' | 'Vip';

/** Một ghế trong sơ đồ — tên cột bám theo entity Seat (Sprint 3, Dăm). */
export interface SeatMapSeat {
  /** Khoá ghế — Seat.Id. */
  id: string;

  /** Số ghế hiển thị cho hành khách — "A1", "B2" (chuỗi ngắn, chữ + số). */
  seatNumber: string;

  /** Tầng xe, bắt đầu từ 1 — khớp Seat.Floor, không vượt quá SeatLayout.NumberOfFloors. */
  floor: number;

  /** Hàng ghế trong tầng, 1 là hàng đầu — khớp Seat.RowIndex. */
  rowIndex: number;

  /** Cột ghế trong hàng, 1 là cột trái cùng — khớp Seat.ColumnIndex. */
  columnIndex: number;

  /** Ghế phổ thông hay VIP — khớp Seat.SeatType. */
  seatType: SeatType;

  status: SeatStatus;

  /** Giá ghế này (VND) — đã gồm phụ trội VIP (DỰ KIẾN, xem ghi chú đầu file). */
  price: number;
}

/** Kết quả GET /trips/{tripId}/seats — toàn bộ sơ đồ ghế của một chuyến. */
export interface TripSeatMap {
  tripId: string;
  busType: string;
  floors: number;
  pricePerSeat: number;
  vipSurcharge: number;
  seats: SeatMapSeat[];
}

export interface SeatMapApi {
  /** Lấy sơ đồ ghế + trạng thái từng ghế của một chuyến. */
  getSeatMap: (tripId: string) => Promise<TripSeatMap>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì endpoint chưa có (xem ghi chú
// đầu file); sau khi Hiếu chốt hợp đồng thì đổi xuống `false` — nhánh `api` đã viết sẵn.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: SeatMapApi = {
  getSeatMap: (tripId) =>
    axiosClient.get<TripSeatMap, TripSeatMap>(`/trips/${tripId}/seats`),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------

/** Số ghế trên mỗi hàng — dàn 2 + 2 (lối đi nằm giữa cột 2 và cột 3). */
const SEATS_PER_ROW = 4;

/** Hai mẫu xe giả, khớp sức chứa 29/45 đang dùng ở tripSearchApi.ts. */
interface MockBusLayout {
  capacity: number;
  floors: number;
  pricePerSeat: number;
  vipSurcharge: number;
  busType: string;
}

/** Chọn mẫu xe theo tripId — nhánh giả của tripSearchApi dùng `route-E01` cho xe 45 chỗ. */
function mockLayout(tripId: string): MockBusLayout {
  if (tripId.includes('E01')) {
    return {
      capacity: 45,
      floors: 2,
      pricePerSeat: 12000,
      vipSurcharge: 8000,
      busType: 'Xe buýt điện 45 chỗ',
    };
  }

  return {
    capacity: 29,
    floors: 1,
    pricePerSeat: 8000,
    vipSurcharge: 5000,
    busType: 'Hyundai County 29 chỗ',
  };
}

/** Vài ghế đang bị giữ / đã bán để demo trạng thái — phần còn lại là trống. */
const HELD_SEATS = new Set(['B1', 'C3', 'F2']);
const PAID_SEATS = new Set(['A2', 'A3', 'B2', 'D4']);

function statusOf(seatNumber: string): SeatStatus {
  if (PAID_SEATS.has(seatNumber)) return 'Paid';
  if (HELD_SEATS.has(seatNumber)) return 'Held';
  return 'Available';
}

/**
 * Sinh dàn ghế cho một mẫu xe: hàng tính từ đầu xe, chữ cái tăng dần A, B, C… Hai hàng đầu
 * tầng 1 là ghế VIP. Số ghế sinh ra khớp đúng `capacity` — hàng cuối có thể thiếu ghế (hàng
 * chỉ có 1 ghế) nên lưới vẽ đúng chỗ trống thay vì nhồi đủ 4.
 */
function generateSeats(layout: MockBusLayout): SeatMapSeat[] {
  const totalRows = Math.ceil(layout.capacity / SEATS_PER_ROW);
  const rowsPerFloor = Math.ceil(totalRows / layout.floors);

  return Array.from({ length: layout.capacity }, (_, index) => {
    const rowIndex0 = Math.floor(index / SEATS_PER_ROW); // 0-based
    const floor = Math.floor(rowIndex0 / rowsPerFloor) + 1;
    const columnIndex = (index % SEATS_PER_ROW) + 1;
    const rowLetter = String.fromCharCode(65 + rowIndex0); // A, B, C…
    const seatNumber = `${rowLetter}${columnIndex}`;
    const seatType: SeatType = floor === 1 && rowIndex0 < 2 ? 'Vip' : 'Standard';

    return {
      id: `seat-${seatNumber}-${floor}`,
      seatNumber,
      floor,
      rowIndex: rowIndex0 + 1,
      columnIndex,
      seatType,
      status: statusOf(seatNumber),
      price: layout.pricePerSeat + (seatType === 'Vip' ? layout.vipSurcharge : 0),
    };
  });
}

const mock: SeatMapApi = {
  async getSeatMap(tripId) {
    await delay(500);

    const layout = mockLayout(tripId);
    return {
      tripId,
      busType: layout.busType,
      floors: layout.floors,
      pricePerSeat: layout.pricePerSeat,
      vipSurcharge: layout.vipSurcharge,
      seats: generateSeats(layout),
    };
  },
};

const seatMapApi: SeatMapApi = USE_MOCK_DATA ? mock : api;

export default seatMapApi;
