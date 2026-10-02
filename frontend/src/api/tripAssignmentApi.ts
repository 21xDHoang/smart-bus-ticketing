import axiosClient from './axiosClient';
import type { Trip } from './tripApi';

// -----------------------------------------------------------------------------
// API phân công tài xế — phần frontend của task 120 "Component lọc chuyến chưa phân
// công + phân công hàng loạt" (story 14, Dương Thị Hạnh).
//
// ⚠️ Backend gán tài xế (task 113 của Kiên) CHƯA có: TripResponse chưa trả driverId và
// chưa có endpoint gán tài xế vào chuyến. Vì vậy file này giữ nhánh dữ liệu giả
// (USE_MOCK) để dựng giao diện trước — đúng tiền lệ stopApi.ts / adminUserApi.ts /
// fareApi.ts: đổi cờ thành false là quay lại API thật khi backend xong.
//
// Khi Kiên xong task 113, hợp đồng dự kiến (cần chốt lại với Kiên trước khi bật):
//   GET /drivers?isActive=true   → danh sách tài xế đang hoạt động (ĐÃ có thật)
//   ... triển khai gán tài xế 1 chuyến + hàng loạt theo đúng contract Kiên đưa ra.
// -----------------------------------------------------------------------------

/** Bật nhánh dữ liệu giả khi backend gán tài xế chưa có — đổi false khi nối API thật. */
const USE_MOCK = true;

/** Tài xế cho ô chọn — rút gọn từ GET /drivers, chỉ đủ để hiển thị. */
export interface DriverOption {
  id: string;
  fullName: string;
  phoneNumber: string;
}

/**
 * Chuyến kèm thông tin phân công tài xế. Khi Kiên xong task 113, TripResponse sẽ mang
 * sẵn 2 trường này và file này bỏ lớp làm giàu ở dưới.
 */
export interface AssignableTrip extends Trip {
  /** null = chưa phân công tài xế. */
  driverId: string | null;
  /** Tên tài xế kèm sẵn để hiển thị; null khi chưa phân công. */
  driverName: string | null;
}

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// Dữ liệu giả — vài tài xế để nhìn giao diện cho thật.
const MOCK_DRIVERS: DriverOption[] = [
  { id: 'driver-1', fullName: 'Nguyễn Văn An', phoneNumber: '0912345678' },
  { id: 'driver-2', fullName: 'Trần Thị Bích', phoneNumber: '0987654321' },
  { id: 'driver-3', fullName: 'Lê Văn Cường', phoneNumber: '0901234567' },
];

// Bản đồ gán tài xế trong phiên (mock): tripId -> driverId | null. Cập nhật khi phân
// công hàng loạt để thao tác "dính" lại sau khi reload — đúng kiểu MOCK_FARES của fareApi.
const mockAssignments = new Map<string, string | null>();

/** Danh sách tài xế đang hoạt động cho ô chọn. */
export async function fetchDriverOptions(): Promise<DriverOption[]> {
  if (USE_MOCK) {
    await delay(300);
    return MOCK_DRIVERS.map((driver) => ({ ...driver }));
  }

  // GET /api/drivers?isActive=true — DriversController ĐÃ có thật (Trần Trung Hiếu, story 14).
  const { items } = await axiosClient.get<DriverListResult, DriverListResult>('/drivers', {
    params: { isActive: true, page: 1, pageSize: 100 },
  });

  return items;
}

interface DriverListResult {
  items: DriverOption[];
  total: number;
  page: number;
  pageSize: number;
}

/**
 * Gán một tài xế cho NHIỀU chuyến cùng lúc. Trả về số chuyến đã gán.
 */
export async function bulkAssignDriver(
  _routeId: string,
  tripIds: string[],
  driverId: string,
): Promise<number> {
  if (tripIds.length === 0) return 0;

  if (USE_MOCK) {
    await delay(400);
    tripIds.forEach((id) => mockAssignments.set(id, driverId));
    return tripIds.length;
  }

  // TODO(task 113 - Kiên): chưa có endpoint gán tài xế hàng loạt. Khi có thì gọi endpoint
  // thật ở đây (thay `_routeId` bằng tham số dùng trong URL) rồi trả số chuyến đã gán.
  throw new Error('Chưa có endpoint gán tài xế — chờ backend task 113 của Kiên.');
}

/**
 * Gắn thông tin tài xế vào danh sách chuyến. Ở nhánh giả: chuyến chưa từng được phân
 * công trong phiên thì so le chẵn/lẻ theo vị trí để có cả hai trạng thái mà demo bộ lọc.
 * Khi backend thật trả sẵn driverId/driverName thì đây chỉ là phép chuyển kiểu.
 */
export function enrichTripsWithDriver(trips: Trip[]): AssignableTrip[] {
  if (!USE_MOCK) {
    return trips as AssignableTrip[];
  }

  return trips.map((trip, index) => {
    const driver = mockDriverFor(trip, index);
    return {
      ...trip,
      driverId: driver?.id ?? null,
      driverName: driver?.fullName ?? null,
    };
  });
}

function mockDriverFor(trip: Trip, index: number): DriverOption | null {
  // Đã phân công thủ công trong phiên thì dùng đúng kết quả đó.
  if (mockAssignments.has(trip.id)) {
    const driverId = mockAssignments.get(trip.id)!;
    return MOCK_DRIVERS.find((driver) => driver.id === driverId) ?? null;
  }

  // Mặc định so le chẵn/lẻ theo vị trí — để bảng có cả chuyến "đã phân công" lẫn "chưa".
  return index % 2 === 0 ? MOCK_DRIVERS[index % MOCK_DRIVERS.length] : null;
}
