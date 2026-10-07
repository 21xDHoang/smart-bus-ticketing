// -----------------------------------------------------------------------------
// API cấu hình sơ đồ ghế theo loại xe — nối với story 2 "Chọn vị trí ghế" (Sprint 3),
// màn hình "Admin cấu hình sơ đồ ghế theo loại xe" (task của Dương Thị Hạnh).
//
// Hình dạng dữ liệu bám theo entity backend do Vàng Thị Dăm đang dựng (nhánh
// feature/2-migrate-seats-seatlayouts-seatholds): SeatLayout = { busType, numberOfFloors,
// totalSeats } + danh sách Seat { floor, rowIndex, columnIndex, seatType, seatNumber }.
// Backend/contract CHƯA có endpoint cấu hình sơ đồ ghế nên file này chạy dữ liệu giả
// (USE_MOCK_DATA = true) — cùng lối màn "Gửi phản ánh" (feedbackSubmitApi.ts) và "Đăng ký vé
// tháng" (monthlyPassApi.ts) đã làm khi backend chưa sẵn sàng. Khi backend + contract có,
// chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Dăm/Hiếu chốt vào api-contract.md — CHƯA cam kết):
//   GET    /seat-layouts        → danh sách sơ đồ (lọc theo `search`)
//   POST   /seat-layouts        → thêm sơ đồ
//   PUT    /seat-layouts/{id}   → sửa sơ đồ
//   DELETE /seat-layouts/{id}   → xoá sơ đồ
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

/** Loại ghế — khớp enum SeatType của backend (Standard | Vip). */
export type SeatType = 'Standard' | 'Vip';

/** Một ghế của sơ đồ — khớp entity Seat backend (đã mở rộng ở Sprint 3). */
export interface Seat {
  id: string;

  /** Tầng của ghế — 1 hoặc 2, không vượt quá numberOfFloors của sơ đồ. */
  floor: number;

  /** Hàng của ghế trong tầng — 1 là hàng đầu tiên (đánh lại từ 1 ở mỗi tầng). */
  rowIndex: number;

  /** Cột của ghế trong hàng — 1 là cột trái cùng. */
  columnIndex: number;

  seatType: SeatType;

  /** Số ghế hiển thị — "A1", "B12". */
  seatNumber: string;
}

/** Một sơ đồ ghế theo loại xe — khớp entity SeatLayout backend. */
export interface SeatLayout {
  id: string;

  /** Loại xe áp dụng — "Xe buýt 45 chỗ", "Xe buýt điện". Khoá nghiệp vụ: mỗi loại xe một sơ đồ. */
  busType: string;

  /** Số tầng của xe — 1 hoặc 2. */
  numberOfFloors: number;

  /** Tổng số ghế của sơ đồ, bằng đúng số dòng `seats`. */
  totalSeats: number;

  /** Ghế của sơ đồ — mỗi ghế một toạ độ (tầng/hàng/cột) + loại ghế. */
  seats: Seat[];

  createdAt: string;

  /** null khi chưa sửa lần nào. */
  updatedAt: string | null;
}

/** Một ghế trong body thêm/sửa sơ đồ (chưa có id — backend/mock tự sinh). */
export interface SeatPayload {
  floor: number;
  rowIndex: number;
  columnIndex: number;
  seatType: SeatType;
  seatNumber: string;
}

/** Body khi thêm/sửa sơ đồ ghế. */
export interface SeatLayoutPayload {
  busType: string;
  numberOfFloors: number;
  seats: SeatPayload[];
}

/** Chữ cái của một hàng (0 → "A", 25 → "Z"). */
export function rowLetter(rowIndex: number): string {
  return String.fromCharCode(65 + rowIndex);
}

/** Số hiệu ghế — hàng A cột 1 → "A1". Hàng đánh lại từ A ở mỗi tầng (khớp RowIndex backend). */
export function seatNumber(rowIndex: number, columnIndex: number): string {
  return `${rowLetter(rowIndex)}${columnIndex + 1}`;
}

/** Khoá định danh một ghế trong lưới — "1-2-3" = tầng 1, hàng 2, cột 3 (đều đếm từ 1). */
export function seatKey(floor: number, rowIndex: number, columnIndex: number): string {
  return `${floor}-${rowIndex}-${columnIndex}`;
}

/** Dựng danh sách ghế (SeatPayload) từ kích thước lưới + tập ghế VIP. */
export function buildSeats(
  numberOfFloors: number,
  rowsPerFloor: number,
  columnsPerRow: number,
  vipKeys: string[],
): SeatPayload[] {
  const vipSet = new Set(vipKeys);
  const seats: SeatPayload[] = [];

  for (let floor = 1; floor <= numberOfFloors; floor += 1) {
    for (let row = 1; row <= rowsPerFloor; row += 1) {
      for (let col = 1; col <= columnsPerRow; col += 1) {
        const key = seatKey(floor, row, col);
        seats.push({
          floor,
          rowIndex: row,
          columnIndex: col,
          seatType: vipSet.has(key) ? 'Vip' : 'Standard',
          seatNumber: seatNumber(row - 1, col - 1),
        });
      }
    }
  }

  return seats;
}

/** Toàn bộ khoá ghế hợp lệ của một lưới — để lọc ghế VIP đã chọn nhưng không còn tồn tại. */
export function seatKeysOf(
  numberOfFloors: number,
  rowsPerFloor: number,
  columnsPerRow: number,
): string[] {
  return buildSeats(numberOfFloors, rowsPerFloor, columnsPerRow, []).map((seat) =>
    seatKey(seat.floor, seat.rowIndex, seat.columnIndex),
  );
}

/** Từ một sơ đồ đã lưu, suy lại kích thước lưới + tập ghế VIP để nạp vào form khi sửa. */
export function layoutGridConfig(layout: Pick<SeatLayout, 'numberOfFloors' | 'seats'>): {
  rowsPerFloor: number;
  columnsPerRow: number;
  vipKeys: string[];
} {
  const rowsPerFloor = layout.seats.reduce((max, seat) => Math.max(max, seat.rowIndex), 0);
  const columnsPerRow = layout.seats.reduce((max, seat) => Math.max(max, seat.columnIndex), 0);
  const vipKeys = layout.seats
    .filter((seat) => seat.seatType === 'Vip')
    .map((seat) => seatKey(seat.floor, seat.rowIndex, seat.columnIndex));
  return { rowsPerFloor, columnsPerRow, vipKeys };
}

/** Số ghế VIP của một sơ đồ. */
export function vipSeatCount(layout: Pick<SeatLayout, 'seats'>): number {
  return layout.seats.filter((seat) => seat.seatType === 'Vip').length;
}

export interface SeatLayoutApi {
  /** Danh sách sơ đồ ghế, lọc theo loại xe (không phân biệt hoa thường). */
  list: (search?: string) => Promise<SeatLayout[]>;
  create: (payload: SeatLayoutPayload) => Promise<SeatLayout>;
  update: (id: string, payload: SeatLayoutPayload) => Promise<SeatLayout>;
  remove: (id: string) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend SeatLayouts chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có endpoint cấu hình sơ đồ ghế (migration Sprint 3 của Dăm) nên nhánh này tạm
// chưa viết: nối axiosClient vào các endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: SeatLayoutApi = {
  async list() {
    throw new Error('Backend SeatLayouts chưa có — chưa thể tải sơ đồ ghế thật.');
  },
  async create() {
    throw new Error('Backend SeatLayouts chưa có — chưa thể lưu sơ đồ ghế thật.');
  },
  async update() {
    throw new Error('Backend SeatLayouts chưa có — chưa thể cập nhật sơ đồ ghế thật.');
  },
  async remove() {
    throw new Error('Backend SeatLayouts chưa có — chưa thể xoá sơ đồ ghế thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
function mockIsoDate(daysAgo: number): string {
  const date = new Date();
  date.setDate(date.getDate() - daysAgo);
  return date.toISOString();
}

/** Dựng một sơ đồ giả hoàn chỉnh (kèm danh sách ghế) từ kích thước + ghế VIP. */
function buildMockLayout(
  id: string,
  busType: string,
  numberOfFloors: number,
  rowsPerFloor: number,
  columnsPerRow: number,
  vipKeys: string[],
  daysAgo: number,
  updatedDaysAgo: number | null,
): SeatLayout {
  const seats: Seat[] = buildSeats(numberOfFloors, rowsPerFloor, columnsPerRow, vipKeys).map(
    (seat, index) => ({ ...seat, id: `${id}-seat-${index}` }),
  );

  return {
    id,
    busType,
    numberOfFloors,
    totalSeats: seats.length,
    seats,
    createdAt: mockIsoDate(daysAgo),
    updatedAt: updatedDaysAgo === null ? null : mockIsoDate(updatedDaysAgo),
  };
}

/** Kho dữ liệu giả — biến module để thêm/sửa/xoá trong phiên vẫn "nhớ" như backend thật. */
let MOCK_LAYOUTS: SeatLayout[] = [
  buildMockLayout(
    'layout-45',
    'Xe buýt 45 chỗ',
    1,
    9,
    5,
    ['1-1-1', '1-1-2', '1-2-1', '1-2-2'],
    12,
    2,
  ),
  buildMockLayout(
    'layout-2floors',
    'Xe buýt 2 tầng 60 chỗ',
    2,
    6,
    5,
    ['1-1-1', '1-1-2', '1-2-1', '1-2-2', '2-1-1', '2-1-2'],
    9,
    null,
  ),
  buildMockLayout('layout-20', 'Xe trung chuyển 20 chỗ', 1, 5, 4, ['1-1-1', '1-1-2'], 5, null),
];

function generateId(): string {
  return `layout-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

function cloneSeats(seats: Seat[]): Seat[] {
  return seats.map((seat) => ({ ...seat }));
}

const mock: SeatLayoutApi = {
  async list(search) {
    await delay(300);
    const needle = (search ?? '').trim().toLowerCase();
    return MOCK_LAYOUTS.filter(
      (layout) => !needle || layout.busType.toLowerCase().includes(needle),
    ).map((layout) => ({ ...layout, seats: cloneSeats(layout.seats) }));
  },

  async create(payload) {
    await delay(400);
    const seats: Seat[] = payload.seats.map((seat, index) => ({
      ...seat,
      id: `${generateId()}-seat-${index}`,
    }));
    const layout: SeatLayout = {
      id: generateId(),
      busType: payload.busType,
      numberOfFloors: payload.numberOfFloors,
      totalSeats: seats.length,
      seats,
      createdAt: new Date().toISOString(),
      updatedAt: null,
    };
    MOCK_LAYOUTS = [layout, ...MOCK_LAYOUTS];
    return { ...layout, seats: cloneSeats(layout.seats) };
  },

  async update(id, payload) {
    await delay(400);
    const index = MOCK_LAYOUTS.findIndex((layout) => layout.id === id);
    if (index === -1) throw new Error('Không tìm thấy sơ đồ ghế cần sửa.');

    const seats: Seat[] = payload.seats.map((seat, seatIndex) => ({
      ...seat,
      id: `${id}-seat-${seatIndex}`,
    }));
    const updated: SeatLayout = {
      ...MOCK_LAYOUTS[index],
      busType: payload.busType,
      numberOfFloors: payload.numberOfFloors,
      totalSeats: seats.length,
      seats,
      updatedAt: new Date().toISOString(),
    };
    MOCK_LAYOUTS = MOCK_LAYOUTS.map((layout) => (layout.id === id ? updated : layout));
    return { ...updated, seats: cloneSeats(updated.seats) };
  },

  async remove(id) {
    await delay(300);
    MOCK_LAYOUTS = MOCK_LAYOUTS.filter((layout) => layout.id !== id);
  },
};

const seatLayoutApi: SeatLayoutApi = USE_MOCK_DATA ? mock : api;

export default seatLayoutApi;
