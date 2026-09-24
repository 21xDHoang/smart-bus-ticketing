import axiosClient from './axiosClient';

// Hợp đồng API /stops — xem docs/api-contract.md.
// Trường theo đúng contract: { id, name, address, latitude, longitude }
export interface Stop {
  id: string;
  name: string;
  address: string;
  latitude: number;
  longitude: number;
}

// Body khi thêm/sửa trạm (không có id — id do backend sinh).
export type StopPayload = Omit<Stop, 'id'>;

// Một đầu mối gọi API cho cả nhóm: import stopApi rồi gọi stopApi.list(), …
export interface StopApi {
  list: () => Promise<Stop[]>;
  create: (payload: StopPayload) => Promise<Stop>;
  update: (id: string, payload: StopPayload) => Promise<Stop>;
  remove: (id: string) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Backend /stops CHƯA xong (task của Hiếu). Nhóm C (Băng) dựng giao diện bằng
// DỮ LIỆU GIẢ trước — xem docs/01-kien-truc.md: "API xong chỉ đổi chỗ gọi".
// Khi backend có endpoint thật, đổi USE_MOCK_DATA = false là màn hình tự nối API.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// Dữ liệu giả — vài trạm xe buýt ở Hà Nội để nhìn giao diện cho thật.
let mockStops: Stop[] = [
  { id: 'stop-1', name: 'Bến xe Mỹ Đình', address: 'Số 20 Phạm Hùng, Nam Từ Liêm, Hà Nội', latitude: 21.0295, longitude: 105.7775 },
  { id: 'stop-2', name: 'Trạm Cầu Giấy', address: 'Số 1 Cầu Giấy, Hà Nội', latitude: 21.0307, longitude: 105.8034 },
  { id: 'stop-3', name: 'Trạm Kim Mã', address: 'Số 1 Kim Mã, Ba Đình, Hà Nội', latitude: 21.0315, longitude: 105.8134 },
  { id: 'stop-4', name: 'Trạm Giảng Võ', address: 'Số 148 Giảng Võ, Ba Đình, Hà Nội', latitude: 21.0247, longitude: 105.8157 },
  { id: 'stop-5', name: 'Trạm Bách Khoa', address: 'Số 1 Đại Cồ Việt, Hai Bà Trưng, Hà Nội', latitude: 21.0042, longitude: 105.8433 },
  { id: 'stop-6', name: 'Trạm Long Biên', address: 'Số 1 Yên Phụ, Ba Đình, Hà Nội', latitude: 21.046, longitude: 105.856 },
  { id: 'stop-7', name: 'Trạm Âu Cơ', address: 'Số 150 Âu Cơ, Tây Hồ, Hà Nội', latitude: 21.0621, longitude: 105.8321 },
  { id: 'stop-8', name: 'Trạm Ngã Tư Sở', address: 'Số 1 Nguyễn Trãi, Thanh Xuân, Hà Nội', latitude: 20.997, longitude: 105.808 },
];

const nextId = () => `stop-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: StopApi = {
  // GET /api/stops
  list: () => axiosClient.get<Stop[], Stop[]>('/stops'),

  // POST /api/stops — body { name, address, latitude, longitude }
  create: (payload) => axiosClient.post<Stop, Stop>('/stops', payload),

  // PUT /api/stops/{id}
  update: (id, payload) => axiosClient.put<Stop, Stop>(`/stops/${id}`, payload),

  // DELETE /api/stops/{id} — 204 No Content
  remove: (id) => axiosClient.delete<never, void>(`/stops/${id}`),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
const mock: StopApi = {
  async list() {
    await delay(400);
    return [...mockStops];
  },

  async create(payload) {
    await delay(400);
    const created: Stop = { ...payload, id: nextId() };
    mockStops = [created, ...mockStops];
    return created;
  },

  async update(id, payload) {
    await delay(400);
    const index = mockStops.findIndex((stop) => stop.id === id);
    if (index === -1) {
      throw new Error('Không tìm thấy trạm dừng cần sửa.');
    }
    mockStops[index] = { ...mockStops[index], ...payload };
    return mockStops[index];
  },

  async remove(id) {
    await delay(400);
    mockStops = mockStops.filter((stop) => stop.id !== id);
  },
};

// Chỉ cần đổi USE_MOCK_DATA ở trên để chuyển giữa dữ liệu giả và API thật.
const stopApi: StopApi = USE_MOCK_DATA ? mock : api;

export default stopApi;
