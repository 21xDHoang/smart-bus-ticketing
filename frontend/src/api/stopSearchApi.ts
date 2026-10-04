import axiosClient from './axiosClient';
import type { Stop } from './stopApi';

// -----------------------------------------------------------------------------
// Gợi ý trạm dừng cho hành khách (GET /stops/search) — endpoint CÔNG KHAI của story 1
// (StopsSearchController), cùng lối RouteSearchController: không gắn [Authorize] nên ai cũng gọi
// được, kể cả khách chưa đăng nhập.
//
// Tách khỏi stopApi.ts (GET /stops của màn quản lý trạm — [Authorize(ManagerOrAbove)]): hai bề
// mặt, hai mức quyền, hai module. Gọi stopApi.list() từ màn hình của hành khách là 403.
//
// Backend khớp cả TÊN lẫn ĐỊA CHỈ, không phân biệt hoa thường và không phân biệt dấu — hành
// khách gõ "ben thanh" vẫn ra "Bến Thành". Bỏ trống từ khoá = đầu danh sách theo tên.
// Hợp đồng đầy đủ ở mục "Tra cứu trạm dừng — /stops/search" của docs/api-contract.md.
// -----------------------------------------------------------------------------

export interface StopSearchApi {
  /** Trạm khớp từ khoá, tối đa 10 dòng (trần mặc định của backend). */
  search: (keyword: string) => Promise<Stop[]>;
}

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: StopSearchApi = {
  search: (keyword) => axiosClient.get<Stop[], Stop[]>('/stops/search', { params: { keyword } }),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
// Vài trạm quanh TP.HCM, khớp vùng dữ liệu của CSDL chung để nhìn giao diện cho quen mắt.
const MOCK_STOPS: Stop[] = [
  { id: 'stop-1', name: 'Bến Thành', address: 'Quận 1, TP.HCM', latitude: 10.7719, longitude: 106.698 },
  { id: 'stop-2', name: 'Chợ Lớn', address: 'Quận 5, TP.HCM', latitude: 10.754, longitude: 106.6634 },
  { id: 'stop-3', name: 'Bến xe Miền Đông', address: 'Bình Thạnh, TP.HCM', latitude: 10.8142, longitude: 106.7106 },
  { id: 'stop-4', name: 'Bến xe Miền Tây', address: 'Bình Tân, TP.HCM', latitude: 10.7398, longitude: 106.6186 },
  { id: 'stop-5', name: 'Suối Tiên', address: 'Quận 9, TP.HCM', latitude: 10.8486, longitude: 106.8002 },
  { id: 'stop-6', name: 'Đại học Quốc gia', address: 'Thủ Đức, TP.HCM', latitude: 10.8701, longitude: 106.8028 },
  { id: 'stop-7', name: 'Công viên 23/9', address: 'Quận 1, TP.HCM', latitude: 10.7679, longitude: 106.6921 },
];

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Bỏ dấu tiếng Việt để bản giả khớp GIỐNG backend thật ("ben thanh" ra "Bến Thành"). */
function stripDiacritics(text: string): string {
  // NFD tách nguyên âm có dấu thành nguyên âm + dấu tổ hợp (combining diacritical marks,
  // dải U+0300–U+036F) — bỏ các ký tự trong dải đó là xong phần dấu thanh.
  // Viết bằng vòng lặp thay vì regex escape cho dễ đọc trong trình soạn thảo.
  let result = '';
  for (const character of text.normalize('NFD')) {
    const code = character.codePointAt(0) ?? 0;
    if (code >= 0x0300 && code <= 0x036f) continue;

    // đ/Đ không tách được bằng NFD (là ký tự độc lập) nên phải thay tay.
    if (character === 'đ') result += 'd';
    else if (character === 'Đ') result += 'D';
    else result += character;
  }

  return result;
}

const mock: StopSearchApi = {
  async search(keyword) {
    await delay(200);

    const needle = stripDiacritics(keyword.trim().toLowerCase());
    if (needle.length === 0) return MOCK_STOPS.slice(0, 10);

    return MOCK_STOPS.filter((stop) =>
      stripDiacritics(`${stop.name} ${stop.address}`.toLowerCase()).includes(needle),
    );
  },
};

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `false` — endpoint gợi ý trạm đã có.
const USE_MOCK_DATA = false;

const stopSearchApi: StopSearchApi = USE_MOCK_DATA ? mock : api;

export default stopSearchApi;
