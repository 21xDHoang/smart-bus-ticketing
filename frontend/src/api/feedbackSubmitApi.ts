import type { TagProps } from 'antd';
import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API gửi phản ánh — nối với story 24 "Gửi phản ánh" (Sprint 2), màn hình
// "Gửi phản ánh / đánh giá chuyến đi" (task của Dương Thị Hạnh).
//
// Backend CHƯA có endpoint gửi phản ánh: "API gửi phản ánh: chọn chuyến, loại phản ánh,
// nội dung, đính kèm ảnh" là task của Trần Trung Hiếu, vẫn ghi "Chưa làm" ở sheet Sprint 2.
// Vì vậy file này dựng theo đúng mẫu của monthlyPassApi.ts: khai báo kiểu + bảng nhãn/màu
// loại phản ánh, có nhánh dữ liệu giả để màn hình chạy được ngay, và nhánh API thật viết
// sẵn để khi backend xong chỉ cần đổi cờ `USE_MOCK_DATA`.
//
// Hợp đồng endpoint DỰ KIẾN (kebab-case, danh từ số nhiều — quy ước D của nhóm):
//   POST /feedbacks → gửi một phản ánh mới → Feedback (201, status khởi tạo là `New`)
//
// Hình dạng entity khớp mục "Phản ánh — /feedbacks" trong docs/api-contract.md — nguồn
// chân lý duy nhất. LƯU Ý khác với feedbackApi.ts (màn "Phản ánh của tôi" của Băng, đang
// chạy dữ liệu giả cũ): loại đúng theo contract là `Complaint` / `Compliment` / `Suggestion`
// và trạng thái là `New` / `InProgress` / `Resolved`. File này bám theo contract, không bám
// theo dữ liệu giả của màn hình danh sách.
// -----------------------------------------------------------------------------

/** Loại phản ánh — khớp cột varchar Type của bảng Feedbacks (docs/api-contract.md). */
export type FeedbackType = 'Complaint' | 'Compliment' | 'Suggestion';

/** Trạng thái xử lý — vòng đời từ lúc gửi tới khi nhà xe xử lý xong. */
export type FeedbackStatus = 'New' | 'InProgress' | 'Resolved';

/** Một chuyến trong ô chọn "chọn chuyến" — thông tin hành khách cần để nhận ra chuyến. */
export interface FeedbackTripOption {
  id: string;

  /** Mã tuyến hiển thị — "01", "B10". */
  routeCode: string;

  routeName: string;

  /** Giờ khởi hành (ISO 8601) — để phân biệt các chuyến cùng tuyến. */
  departureTime: string;
}

/** Body khi gửi một phản ánh mới — POST /feedbacks (dự kiến). */
export interface SubmitFeedbackPayload {
  /** Chuyến bị phản ánh. `null` là hợp lệ — phản ánh về tuyến/dịch vụ/ứng dụng. */
  tripId: string | null;

  type: FeedbackType;

  /** Nội dung phản ánh — text tự do, bắt buộc. */
  content: string;

  /** Mức độ hài lòng 1..5. `null` khi khách không chấm sao. */
  rating: number | null;

  /** Ảnh đính kèm. `null` khi không đính kèm. */
  attachmentUrl: string | null;
}

/** Phản ánh vừa gửi, trả về bởi POST /feedbacks — chỉ cần đủ để hiện màn hình thành công. */
export interface SubmittedFeedback {
  id: string;

  tripId: string | null;

  type: FeedbackType;

  content: string;

  attachmentUrl: string | null;

  rating: number | null;

  /** Luôn là `New` ngay sau khi gửi. */
  status: FeedbackStatus;

  createdAt: string;
}

interface FeedbackTypeMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: TagProps['color'];
}

export const FEEDBACK_TYPE_META: Record<FeedbackType, FeedbackTypeMeta> = {
  Complaint: { label: 'Khiếu nại', color: 'red' },
  Compliment: { label: 'Khen ngợi', color: 'green' },
  Suggestion: { label: 'Góp ý', color: 'geekblue' },
};

/** Danh sách loại phản ánh cho ô chọn, theo đúng thứ tự khai báo ở FEEDBACK_TYPE_META. */
export const FEEDBACK_TYPE_OPTIONS: { value: FeedbackType; label: string }[] = (
  Object.keys(FEEDBACK_TYPE_META) as FeedbackType[]
).map((value) => ({ value, label: FEEDBACK_TYPE_META[value].label }));

/** Nhãn + màu trạng thái — chỉ cần `New` để vẽ thẻ "Mới" trên màn hình thành công. */
export const FEEDBACK_STATUS_META: Record<FeedbackStatus, FeedbackTypeMeta> = {
  New: { label: 'Mới', color: 'processing' },
  InProgress: { label: 'Đang xử lý', color: 'warning' },
  Resolved: { label: 'Đã xử lý', color: 'success' },
};

/** Lấy nhãn + màu cho một mã loại phản ánh; mã lạ vẫn hiển thị được thay vì crash. */
export function getFeedbackTypeMeta(type: string): FeedbackTypeMeta {
  return FEEDBACK_TYPE_META[type as FeedbackType] ?? { label: type, color: 'default' };
}

export interface FeedbackSubmitApi {
  /** Danh sách chuyến đã đi gần đây của hành khách cho ô "chọn chuyến". */
  listMyRecentTrips: () => Promise<FeedbackTripOption[]>;

  /** Gửi một phản ánh mới. */
  submit: (payload: SubmitFeedbackPayload) => Promise<SubmittedFeedback>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì endpoint gửi phản ánh chưa có
// (xem ghi chú đầu file). Đổi thành `false` khi Hiếu xong API gửi phản ánh.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: FeedbackSubmitApi = {
  // Chưa có endpoint công khai để hành khách liệt kê chuyến đã đi (GET /trips nằm sau
  // policy ManagerOrAbove — xem docs/api-contract.md). Khi backend bổ sung nguồn chuyến
  // cho màn hình này thì gọi ở đây. Chưa có contract nên không dựng lời gọi để tránh đặt
  // tên endpoint bịa (quy ước D: danh từ số nhiều, kebab-case).
  async listMyRecentTrips() {
    throw new Error('Chưa có API danh sách chuyến của hành khách. Xem docs/api-contract.md.');
  },

  // POST /feedbacks — body { tripId, type, content, rating, attachmentUrl }. Backend đọc
  // userId từ JWT và khởi tạo status = New. Endpoint là DỰ KIẾN, chờ Hiếu chốt trong contract.
  submit: (payload) => axiosClient.post<SubmittedFeedback, SubmittedFeedback>('/feedbacks', payload),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
function mockDate(daysAgo: number, hoursAgo = 0): string {
  const date = new Date();
  date.setDate(date.getDate() - daysAgo);
  date.setHours(date.getHours() - hoursAgo);
  return date.toISOString();
}

// Vài chuyến "đã đi gần đây" giả để ô chọn chuyến có dữ liệu; tên tuyến khớp monthlyPassApi
// và feedbackApi để nhất quán khi demo.
const MOCK_TRIPS: FeedbackTripOption[] = [
  {
    id: 'trip-01',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    departureTime: mockDate(1, 2),
  },
  {
    id: 'trip-02',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    departureTime: mockDate(2),
  },
  {
    id: 'trip-03',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    departureTime: mockDate(4),
  },
  {
    id: 'trip-04',
    routeCode: 'B10',
    routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    departureTime: mockDate(6),
  },
];

const mock: FeedbackSubmitApi = {
  async listMyRecentTrips() {
    await delay(400);
    return MOCK_TRIPS.map((trip) => ({ ...trip }));
  },

  async submit(payload) {
    await delay(600);

    // Giả lập lỗi "không tìm thấy chuyến" như backend sẽ trả khi tripId không có thật.
    if (payload.tripId && !MOCK_TRIPS.some((trip) => trip.id === payload.tripId)) {
      throw new Error('Không tìm thấy chuyến đã chọn.');
    }

    return {
      id: `fb-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      tripId: payload.tripId,
      type: payload.type,
      content: payload.content,
      attachmentUrl: payload.attachmentUrl,
      rating: payload.rating,
      status: 'New',
      createdAt: new Date().toISOString(),
    };
  },
};

const feedbackSubmitApi: FeedbackSubmitApi = USE_MOCK_DATA ? mock : api;

export default feedbackSubmitApi;
