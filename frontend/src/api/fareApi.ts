import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API bảng giá vé — nối với FaresController (task story 12 của Hoàng).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Bảng giá vé":
//   GET    /routes/{routeId}/fares          → Fare[]
//   GET    /routes/{routeId}/fares/{id}     → Fare
//   POST   /routes/{routeId}/fares          → { passengerType, price } → Fare (201)
//   PUT    /routes/{routeId}/fares/{id}     → { price } → Fare
//   DELETE /routes/{routeId}/fares/{id}     → 204 No Content
//
// Backend /routes/{routeId}/fares ĐÃ có (FaresController). Riêng danh sách tuyến
// (/routes) CHƯA có endpoint trong api-contract.md — còn "đang chờ bổ sung" (Hiếu).
// Vì vậy màn hình chạy bằng dữ liệu giả cho tới khi /routes xong; đổi USE_MOCK = false
// là phần giá vé tự nối API thật.
// -----------------------------------------------------------------------------

/** Mã đối tượng hành khách — khớp enum PassengerType của backend, đúng thứ tự khai báo. */
export type PassengerType = 'Standard' | 'Student' | 'Senior' | 'Child' | 'Disabled';

export interface Fare {
  id: string;
  routeId: string;
  passengerType: PassengerType;
  /** Giá vé (VND), tối đa 2 chữ số thập phân. */
  price: number;
  createdAt: string;
  updatedAt: string | null;
}

/** Body khi thêm một dòng giá — POST /routes/{routeId}/fares. */
export interface FarePayload {
  passengerType: PassengerType;
  price: number;
}

/** Body khi sửa giá — PUT /routes/{routeId}/fares/{id} (chỉ đổi giá, không đổi đối tượng). */
export interface UpdateFarePayload {
  price: number;
}

/** Tuyến cho ô chọn — mock cho tới khi /routes có endpoint thật. */
export interface RouteOption {
  id: string;
  /** Mã tuyến hiển thị cho hành khách, ví dụ "01", "B10". */
  code: string;
  /** Tên tuyến, ví dụ "Bến Thành — Chợ Lớn". */
  name: string;
}

export interface PassengerTypeMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: string;
  /** Đối tượng ưu đãi (US 17) hay giá phổ thông. */
  isDiscount: boolean;
}

/** Nhãn + màu cho từng đối tượng, theo đúng thứ tự enum (Standard → Disabled). */
export const PASSENGER_TYPE_META: Record<PassengerType, PassengerTypeMeta> = {
  Standard: { label: 'Người lớn', color: 'blue', isDiscount: false },
  Student: { label: 'Học sinh, sinh viên', color: 'green', isDiscount: true },
  Senior: { label: 'Người cao tuổi', color: 'purple', isDiscount: true },
  Child: { label: 'Trẻ em', color: 'cyan', isDiscount: true },
  Disabled: { label: 'Người khuyết tật', color: 'orange', isDiscount: true },
};

/** Danh sách đối tượng cho ô chọn, đúng thứ tự hiển thị của bảng giá. */
export const PASSENGER_TYPE_OPTIONS: { value: PassengerType; label: string }[] = (
  ['Standard', 'Student', 'Senior', 'Child', 'Disabled'] as PassengerType[]
).map((value) => ({ value, label: PASSENGER_TYPE_META[value].label }));

/** Nhãn an toàn cho mã lạ (backend có thể thêm đối tượng sau này). */
export function getPassengerTypeLabel(code: string): string {
  return PASSENGER_TYPE_META[code as PassengerType]?.label ?? (code || 'Không xác định');
}

export interface FareApi {
  /** Danh sách tuyến cho ô chọn — mock tới khi /routes có endpoint thật. */
  listRoutes: () => Promise<RouteOption[]>;
  list: (routeId: string) => Promise<Fare[]>;
  create: (routeId: string, payload: FarePayload) => Promise<Fare>;
  update: (routeId: string, id: string, payload: UpdateFarePayload) => Promise<Fare>;
  remove: (routeId: string, id: string) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Backend /routes/{routeId}/fares ĐÃ có (FaresController). Nhưng màn hình còn cần
// ô chọn tuyến mà /routes chưa có — nên tạm chạy toàn bộ bằng dữ liệu giả cho tới khi
// /routes xong (xem docs/01-kien-truc.md: "API xong chỉ đổi chỗ gọi").
const USE_MOCK = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- DỮ LIỆU GIẢ (MOCK) --------

const MOCK_ROUTES: RouteOption[] = [
  { id: 'route-01', code: '01', name: 'Bến Thành — Chợ Lớn' },
  { id: 'route-08', code: '08', name: 'Bến xe Miền Đông — Bến xe Miền Tây' },
  { id: 'route-14', code: '14', name: 'Bến xe buýt Sài Gòn — Đại học Quốc gia' },
];

/** Giá theo từng tuyến. route-01 có đủ 5 đối tượng, route-08 chỉ 2, route-14 trống. */
const MOCK_FARES: Record<string, Fare[]> = {
  'route-01': [
    { id: 'fare-01-1', routeId: 'route-01', passengerType: 'Standard', price: 8000, createdAt: '2026-09-25T02:00:00Z', updatedAt: null },
    { id: 'fare-01-2', routeId: 'route-01', passengerType: 'Student', price: 4000, createdAt: '2026-09-25T02:00:00Z', updatedAt: null },
    { id: 'fare-01-3', routeId: 'route-01', passengerType: 'Senior', price: 4000, createdAt: '2026-09-25T02:00:00Z', updatedAt: '2026-09-25T03:10:00Z' },
    { id: 'fare-01-4', routeId: 'route-01', passengerType: 'Child', price: 3000, createdAt: '2026-09-25T02:00:00Z', updatedAt: null },
    { id: 'fare-01-5', routeId: 'route-01', passengerType: 'Disabled', price: 3000, createdAt: '2026-09-25T02:00:00Z', updatedAt: null },
  ],
  'route-08': [
    { id: 'fare-08-1', routeId: 'route-08', passengerType: 'Standard', price: 6000, createdAt: '2026-09-24T10:00:00Z', updatedAt: null },
    { id: 'fare-08-2', routeId: 'route-08', passengerType: 'Student', price: 3000, createdAt: '2026-09-24T10:00:00Z', updatedAt: null },
  ],
  'route-14': [],
};

const nextId = () => `fare-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

/** Sắp xếp theo thứ tự khai báo enum — giống FareService.ListByRouteAsync. */
const PASSENGER_ORDER: PassengerType[] = ['Standard', 'Student', 'Senior', 'Child', 'Disabled'];

function sortFares(fares: Fare[]): Fare[] {
  return [...fares].sort(
    (a, b) =>
      PASSENGER_ORDER.indexOf(a.passengerType) - PASSENGER_ORDER.indexOf(b.passengerType),
  );
}

// -------- Gọi API thật (dùng khi USE_MOCK = false) --------
const api: FareApi = {
  // /routes CHƯA có endpoint thật — xem ghi chú đầu file.
  listRoutes: async () => {
    throw new Error('Danh sách tuyến chưa có endpoint /routes — cần backend (Hiếu).');
  },

  // GET /api/routes/{routeId}/fares
  list: (routeId) => axiosClient.get<Fare[], Fare[]>(`/routes/${routeId}/fares`),

  // POST /api/routes/{routeId}/fares
  create: (routeId, payload) =>
    axiosClient.post<Fare, Fare>(`/routes/${routeId}/fares`, payload),

  // PUT /api/routes/{routeId}/fares/{id}
  update: (routeId, id, payload) =>
    axiosClient.put<Fare, Fare>(`/routes/${routeId}/fares/${id}`, payload),

  // DELETE /api/routes/{routeId}/fares/{id} — 204 No Content
  remove: (routeId, id) => axiosClient.delete<never, void>(`/routes/${routeId}/fares/${id}`),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK = true) --------
const mock: FareApi = {
  async listRoutes() {
    await delay(250);
    return MOCK_ROUTES.map((route) => ({ ...route }));
  },

  async list(routeId) {
    await delay(400);
    return sortFares(MOCK_FARES[routeId] ?? []);
  },

  async create(routeId, payload) {
    await delay(400);
    const routeFares = MOCK_FARES[routeId] ?? [];
    if (routeFares.some((fare) => fare.passengerType === payload.passengerType)) {
      // Giả lập 409 của backend — cùng thông báo FareService.DuplicateFareMessage.
      const error = new Error('Tuyến này đã có giá vé cho đối tượng đó') as Error & {
        status?: number;
      };
      error.status = 409;
      throw error;
    }
    const created: Fare = {
      id: nextId(),
      routeId,
      passengerType: payload.passengerType,
      price: payload.price,
      createdAt: new Date().toISOString(),
      updatedAt: null,
    };
    MOCK_FARES[routeId] = sortFares([...routeFares, created]);
    return created;
  },

  async update(routeId, id, payload) {
    await delay(400);
    const routeFares = MOCK_FARES[routeId] ?? [];
    const index = routeFares.findIndex((fare) => fare.id === id);
    if (index === -1) {
      throw new Error('Không tìm thấy giá vé cần sửa.');
    }
    routeFares[index] = { ...routeFares[index], price: payload.price, updatedAt: new Date().toISOString() };
    MOCK_FARES[routeId] = sortFares(routeFares);
    return routeFares[index];
  },

  async remove(routeId, id) {
    await delay(400);
    MOCK_FARES[routeId] = (MOCK_FARES[routeId] ?? []).filter((fare) => fare.id !== id);
  },
};

// Chỉ cần đổi USE_MOCK ở trên để chuyển giữa dữ liệu giả và API thật.
const fareApi: FareApi = USE_MOCK ? mock : api;

export default fareApi;
