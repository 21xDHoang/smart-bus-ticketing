import type { PassengerType } from './fareApi';

// -----------------------------------------------------------------------------
// Bảng giá mẫu (fare table) — khái niệm CHỈ có ở frontend, chưa có entity/endpoint ở backend.
//
// Mục đích: người quản lý tạo sẵn một "bảng giá" (tên + giá cho đủ 5 đối tượng hành khách) rồi
// áp nguyên bảng đó vào một tuyến thay vì phải thêm từng dòng giá một. Khi áp, màn hình gọi lại
// các endpoint sẵn có của FaresController (POST/PUT /routes/{routeId}/fares) cho từng đối tượng.
//
// Vì backend chưa có bảng FareTable nên lưu ở localStorage để sống qua các lần refresh (cùng
// lối colorMode). Hệ quả: bảng giá mẫu là THEO TRÌNH DUYỆT, không chia sẻ giữa các máy. Khi
// backend bổ sung entity + endpoint, chuyển các hàm dưới đây sang gọi API thật.
// -----------------------------------------------------------------------------

/** Một bảng giá mẫu — tên + giá đủ 5 đối tượng hành khách. */
export interface FareTable {
  id: string;
  name: string;
  /** Giá (VND) theo từng đối tượng — đủ 5 khoá của PassengerType. */
  prices: Record<PassengerType, number>;
  createdAt: string;
}

/** Dữ liệu nhập khi tạo bảng giá mẫu. */
export interface FareTablePayload {
  name: string;
  prices: Record<PassengerType, number>;
}

const STORAGE_KEY = 'sbt-fare-tables';

function readAll(): FareTable[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as FareTable[]) : [];
  } catch {
    // Riêng tư chặn localStorage hoặc dữ liệu hỏng — coi như chưa có bảng nào.
    return [];
  }
}

function writeAll(tables: FareTable[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(tables));
  } catch {
    // Không ghi được thì bảng mới chỉ sống trong phiên này.
  }
}

/** Danh sách bảng giá mẫu, bảng tạo trước đứng trước. */
export function listFareTables(): FareTable[] {
  return readAll();
}

/** Tạo một bảng giá mẫu mới, trả về bảng đã lưu. */
export function createFareTable(payload: FareTablePayload): FareTable {
  const table: FareTable = {
    id: `ft-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    name: payload.name.trim(),
    prices: { ...payload.prices },
    createdAt: new Date().toISOString(),
  };
  writeAll([...readAll(), table]);
  return table;
}

/** Xoá một bảng giá mẫu theo id. */
export function removeFareTable(id: string): void {
  writeAll(readAll().filter((table) => table.id !== id));
}
