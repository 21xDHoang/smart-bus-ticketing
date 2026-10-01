import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API đội xe — nối với BusesController (story 14, Trần Trung Hiếu).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Xe buýt — /buses":
//   GET /buses?search=&status=&page=&pageSize=
//     → { items: Bus[], total, page, pageSize }
//
// BusesController ĐÃ có thật nên file này KHÔNG có nhánh dữ liệu giả: gọi thẳng API.
// -----------------------------------------------------------------------------

/** Trạng thái xe — khớp enum BusStatus của backend, đúng thứ tự khai báo. */
export type BusStatus = 'Active' | 'Maintenance' | 'Inactive';

/** Một xe buýt — khớp `BusResponse` của backend, xem docs/api-contract.md. */
export interface Bus {
  id: string;
  /** Biển số xe — "29B-123.45". Duy nhất toàn hệ thống. */
  licensePlate: string;
  /** Loại xe — "Xe buýt 45 chỗ", "Xe buýt điện". */
  busType: string;
  /** Sức chứa theo số ghế. */
  capacity: number;
  status: BusStatus;
  createdAt: string;
  /** null khi xe chưa được sửa lần nào. */
  updatedAt: string | null;
}

export interface BusListParams {
  /** Tìm theo biển số hoặc loại xe — không phân biệt hoa thường. */
  search?: string;
  /** Bỏ trống = lấy cả ba trạng thái. */
  status?: BusStatus;
  page: number;
  pageSize: number;
}

export interface BusListResult {
  /** Chỉ là trang hiện tại, không phải toàn bộ. */
  items: Bus[];
  /** Tổng số dòng KHỚP BỘ LỌC (không phải số dòng trong `items`) — dùng để vẽ phân trang. */
  total: number;
  page: number;
  pageSize: number;
}

interface BusStatusMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: string;
}

export const BUS_STATUS_META: Record<BusStatus, BusStatusMeta> = {
  Active: { label: 'Đang khai thác', color: 'success' },
  Maintenance: { label: 'Bảo dưỡng', color: 'warning' },
  Inactive: { label: 'Ngừng khai thác', color: 'default' },
};

/**
 * Trần `pageSize` của GET /buses là 100 — `[Range(1, 100)]` trong ListBusesRequest,
 * xem docs/api-contract.md. Đây là số lớn nhất lấy được trong một lượt gọi.
 */
const BUS_PICKER_PAGE_SIZE = 100;

/** Một xe trong ô chọn — rút gọn từ Bus, chỉ đủ để hiển thị. */
export interface BusOption {
  id: string;
  licensePlate: string;
  busType: string;
  capacity: number;
}

/**
 * GET /api/buses — danh sách xe + tìm kiếm + lọc trạng thái + phân trang.
 *
 * Chỉ gửi tham số lọc khi thực sự có giá trị: backend coi `status` không khớp mã nào là
 * "lọc ra rỗng" chứ KHÔNG báo lỗi, nên lỡ gửi `status=` rỗng là bảng âm thầm trắng.
 */
export function fetchBuses(params: BusListParams): Promise<BusListResult> {
  const query: Record<string, string | number> = { page: params.page, pageSize: params.pageSize };

  if (params.search) query.search = params.search;
  if (params.status) query.status = params.status;

  return axiosClient.get<BusListResult, BusListResult>('/buses', { params: query });
}

/**
 * Danh sách xe ĐANG KHAI THÁC cho ô "chọn xe" — lấy hết từ GET /buses?status=Active.
 *
 * Chỉ xe `Active` mới gán được vào chuyến: điều kiện `busId` của POST/PUT
 * /routes/{routeId}/trips kiểm tra trạng thái `Active` (docs/api-contract.md), nên ô chọn
 * không cần loại xe bảo dưỡng hay ngừng khai thác.
 */
export async function fetchActiveBusOptions(): Promise<BusOption[]> {
  const options: BusOption[] = [];

  // Lặp theo trang cho tới khi đủ `total`: một trang tối đa 100 xe, nếu chỉ lấy một trang
  // thì xe thứ 101 trở đi âm thầm biến mất khỏi ô chọn mà không báo lỗi.
  for (let page = 1; ; page += 1) {
    const { items, total } = await fetchBuses({
      status: 'Active',
      page,
      pageSize: BUS_PICKER_PAGE_SIZE,
    });

    options.push(
      ...items.map(({ id, licensePlate, busType, capacity }) => ({
        id,
        licensePlate,
        busType,
        capacity,
      })),
    );

    // `items.length === 0` là chốt chặn để không lặp vô hạn nếu `total` sai.
    if (options.length >= total || items.length === 0) break;
  }

  return options;
}
