import axiosClient from './axiosClient';
import type { Trip } from './tripApi';

// -----------------------------------------------------------------------------
// API phân công tài xế — phần frontend của task 120 "Component lọc chuyến chưa phân
// công + phân công hàng loạt" (story 14, Dương Thị Hạnh).
//
// Backend ĐÃ có (task 113 của Kiên): TripResponse mang sẵn driverId/driverName, và
// PATCH /routes/{routeId}/trips/driver-assignment gán một tài xế cho nhiều chuyến cùng lúc.
// File này gọi thẳng API thật — nhánh dữ liệu giả ở dưới giữ lại theo tiền lệ stopApi.ts /
// adminUserApi.ts / fareApi.ts, chỉ để dựng giao diện khi cần mà không có backend.
//
// Endpoint dùng ở đây:
//   GET   /drivers?isActive=true                          → danh sách tài xế đang hoạt động
//   PATCH /routes/{routeId}/trips/driver-assignment       → gán 1 tài xế cho nhiều chuyến
// -----------------------------------------------------------------------------

/** Bật nhánh dữ liệu giả để dựng giao diện khi không có backend — đang nối API thật. */
const USE_MOCK = false;

/** Tài xế cho ô chọn — rút gọn từ GET /drivers, chỉ đủ để hiển thị. */
export interface DriverOption {
  id: string;
  fullName: string;
  phoneNumber: string;
}

/**
 * Chuyến kèm thông tin phân công tài xế — `TripResponse` đã trả sẵn 2 trường này, ở đây
 * khai báo lại thành BẮT BUỘC (đã chuẩn hoá về null) để bảng/lọc không phải bận tâm
 * chuyện undefined.
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
 * Gán một tài xế cho NHIỀU chuyến cùng lúc. Trả về số chuyến THỰC SỰ được đổi tài xế.
 */
export async function bulkAssignDriver(
  routeId: string,
  tripIds: string[],
  driverId: string,
): Promise<number> {
  if (tripIds.length === 0) return 0;

  if (USE_MOCK) {
    await delay(400);
    tripIds.forEach((id) => mockAssignments.set(id, driverId));
    return tripIds.length;
  }

  // PATCH /routes/{routeId}/trips/driver-assignment — tối đa 200 chuyến một lô, và trùng
  // lịch chỉ là CẢNH BÁO trong response (vẫn 200) chứ không chặn: luồng điều hành được phép
  // cố ý chấp nhận trùng. `assignedCount` là số chuyến thực đổi — gọi lại y hệt lần hai trả 0
  // vì chuyến đã đúng tài xế đó từ trước, nên thông báo "đã phân công cho N chuyến" luôn đúng.
  const result = await axiosClient.patch<DriverAssignmentResponse, DriverAssignmentResponse>(
    `/routes/${routeId}/trips/driver-assignment`,
    { tripIds, driverId },
  );

  return result.assignedCount;
}

/**
 * Kết quả PATCH /routes/{routeId}/trips/driver-assignment — khớp `DriverAssignmentResponse`
 * của backend. Ở đây chỉ dùng `assignedCount`; `items` (chi tiết từng chuyến kèm cảnh báo
 * trùng lịch) để dành cho màn hình nào cần hiện chi tiết.
 */
interface DriverAssignmentResponse {
  driverId: string;
  driverName: string;
  /** Số chuyến THỰC SỰ được đổi tài xế — chuyến đã đúng tài xế đó từ trước không tính. */
  assignedCount: number;
  items: unknown[];
}

/**
 * Chuẩn hoá danh sách chuyến thành `AssignableTrip`. API thật luôn kèm `driverId`/`driverName`
 * (null khi chưa phân công), ở đây chỉ quy `undefined` về `null` để bộ lọc "chỉ chưa phân
 * công" và cột "Tài xế" đọc đúng.
 *
 * Ở nhánh giả: chuyến chưa từng được phân công trong phiên thì so le chẵn/lẻ theo vị trí để
 * có cả hai trạng thái mà demo bộ lọc.
 */
export function enrichTripsWithDriver(trips: Trip[]): AssignableTrip[] {
  if (!USE_MOCK) {
    return trips.map((trip) => ({
      ...trip,
      driverId: trip.driverId ?? null,
      driverName: trip.driverName ?? null,
    }));
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
