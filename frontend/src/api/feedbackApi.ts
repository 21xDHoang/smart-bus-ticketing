import type { TagProps } from 'antd';
import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API phản ánh — phục vụ màn hình "Phản ánh của tôi" (story 24, Nguyễn Đình Băng).
// Màn "Gửi phản ánh" của Dương Thị Hạnh dùng feedbackSubmitApi.ts — hai file dùng
// CHUNG bộ nhãn/màu dưới đây (giá trị khớp nhau, cùng bám docs/api-contract.md).
//
// Endpoint đã có trên main — mục "Phản ánh của tôi — /feedbacks/me" của docs/api-contract.md:
//   GET /feedbacks/me?status=…   → MyFeedback[] (mảng trần, mới nhất trước;
//                                  danh sách có `replyCount`, KHÔNG có `replies`)
//   GET /feedbacks/me/{id}       → chi tiết: mảng `replies` (cũ → mới) thay cho `replyCount`
//
// Cả hai chỉ cần ĐĂNG NHẬP ([Authorize] trần, không giới hạn vai trò) và backend tự đọc
// userId từ JWT — hành khách chỉ thấy phản ánh của chính mình, không có tham số nào dò
// phản ánh của người khác. Phản ánh không tồn tại hoặc của người khác đều trả 404 như nhau.
//
// Loại/trạng thái đúng theo contract: Complaint / Compliment / Suggestion và
// New / InProgress / Resolved.
// -----------------------------------------------------------------------------

/** Loại phản ánh — khớp cột varchar Type của bảng Feedbacks. */
export type FeedbackType = 'Complaint' | 'Compliment' | 'Suggestion';

/** Trạng thái xử lý — vòng đời từ lúc gửi tới khi nhà xe xử lý xong. */
export type FeedbackStatus = 'New' | 'InProgress' | 'Resolved';

/** Một dòng trong luồng phản hồi của nhà xe — chỉ có ở chi tiết. */
export interface FeedbackReply {
  id: string;

  /** Quản lý đã phản hồi — ghép từ Users, chỉ để hiển thị. */
  userId: string;

  /** Chỉ để hiển thị. null khi không tìm thấy tài khoản. */
  userFullName: string | null;

  content: string;

  /** Bảng chỉ ghi thêm nên không có updatedAt. */
  createdAt: string;
}

/** Một phản ánh của hành khách — dòng trong GET /feedbacks/me. */
export interface MyFeedback {
  id: string;

  /** Chuyến bị phản ánh. null là hợp lệ — phản ánh về tuyến/dịch vụ/ứng dụng. */
  tripId: string | null;

  /** Mã tuyến của chuyến bị phản ánh — chỉ để HIỂN THỊ, ghép từ Trips → Routes.
   *  Cả ba trường chuyến đều null khi `tripId` là null (hành khách không tự tra được
   *  chuyến vì GET /trips/{id} nằm sau policy ManagerOrAbove). */
  routeCode: string | null;

  routeName: string | null;

  /** Giờ khởi hành của chuyến (ISO 8601). null khi `tripId` là null. */
  departureTime: string | null;

  type: FeedbackType;

  content: string;

  /** Ảnh đính kèm do hành khách gửi. null khi không đính kèm. */
  attachmentUrl: string | null;

  /** Mức độ hài lòng 1..5. null khi khách không chấm sao. */
  rating: number | null;

  status: FeedbackStatus;

  createdAt: string;

  /** Chỉ đổi khi `status` đổi; thêm phản hồi không chạm dòng này. null khi chưa từng đổi. */
  updatedAt: string | null;

  /** Số phản hồi của nhà xe — danh sách chỉ có con đếm, muốn đọc nội dung thì gọi `getMine`. */
  replyCount: number;
}

/** Chi tiết một phản ánh — GET /feedbacks/me/{id}: mang `replies` THAY CHO `replyCount`. */
export type MyFeedbackDetail = Omit<MyFeedback, 'replyCount'> & {
  /** Toàn bộ luồng phản hồi, sắp cũ → mới. Rỗng khi nhà xe chưa trả lời. */
  replies: FeedbackReply[];
};

interface FeedbackMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: TagProps['color'];
}

export const FEEDBACK_TYPE_META: Record<FeedbackType, FeedbackMeta> = {
  Complaint: { label: 'Khiếu nại', color: 'red' },
  Compliment: { label: 'Khen ngợi', color: 'green' },
  Suggestion: { label: 'Góp ý', color: 'geekblue' },
};

export const FEEDBACK_STATUS_META: Record<FeedbackStatus, FeedbackMeta> = {
  New: { label: 'Mới', color: 'processing' },
  InProgress: { label: 'Đang xử lý', color: 'warning' },
  Resolved: { label: 'Đã xử lý', color: 'success' },
};

/** Danh sách trạng thái cho bộ lọc, theo đúng thứ tự vòng đời phản ánh. */
export const FEEDBACK_STATUS_OPTIONS = (Object.keys(FEEDBACK_STATUS_META) as FeedbackStatus[]).map(
  (value) => ({ value, label: FEEDBACK_STATUS_META[value].label }),
);

// Lấy nhãn + màu cho một mã loại/trạng thái. Mã lạ (backend bổ sung sau này) vẫn hiển
// thị được: trả đúng mã đó kèm màu mặc định thay vì crash.
export function getFeedbackTypeMeta(type: string): FeedbackMeta {
  return FEEDBACK_TYPE_META[type as FeedbackType] ?? { label: type, color: 'default' };
}

export function getFeedbackStatusMeta(status: string): FeedbackMeta {
  return FEEDBACK_STATUS_META[status as FeedbackStatus] ?? { label: status, color: 'default' };
}

export interface FeedbackApi {
  /** Danh sách phản ánh của chính người gọi, mới nhất trước. `status` bỏ trống = tất cả. */
  listMyFeedbacks: (status?: FeedbackStatus) => Promise<MyFeedback[]>;

  /** Chi tiết một phản ánh của chính người gọi, kèm luồng phản hồi cũ → mới. */
  getMine: (id: string) => Promise<MyFeedbackDetail>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đã bật API thật — hai endpoint đã có trên main
// (xem ghi chú đầu file). Nhánh giả giữ làm đường lùi khi cần dựng giao diện lúc mất mạng:
// đổi cờ này thành `true` là quay lại được, không phải chạm phần nào khác.
const USE_MOCK_DATA = false;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: FeedbackApi = {
  // Backend tự đọc userId từ JWT nên không cần truyền; chỉ gửi `status` khi thực sự có lọc.
  listMyFeedbacks: (status) => {
    const params: Record<string, string> = {};
    if (status) params.status = status;

    return axiosClient.get<MyFeedback[], MyFeedback[]>('/feedbacks/me', { params });
  },

  // 404 kèm "Không tìm thấy phản ánh" cho cả ca id không tồn tại lẫn ca của hành khách khác —
  // hai ca cố ý trả cùng một câu (không phân biệt để khỏi xác nhận id nào có thật trên hệ thống).
  getMine: (id) => axiosClient.get<MyFeedbackDetail, MyFeedbackDetail>(`/feedbacks/me/${id}`),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
function mockDate(daysAgo: number, hoursAgo = 0): string {
  const date = new Date();
  date.setDate(date.getDate() - daysAgo);
  date.setHours(date.getHours() - hoursAgo);
  return date.toISOString();
}

// Vài phản ánh giả phủ đủ 3 trạng thái và 3 loại, để màn hình dựng được giao diện kể cả
// khi backend chưa sẵn sàng. Tên tuyến khớp monthlyPassApi để nhất quán khi demo.
const MOCK_FEEDBACKS: MyFeedback[] = [
  {
    id: 'fb-1',
    tripId: 'trip-1',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    departureTime: mockDate(6, 1),
    type: 'Complaint',
    content: 'Xe xuất phát trễ 25 phút so với lịch, không có thông báo gì tại trạm.',
    attachmentUrl: null,
    rating: null,
    status: 'Resolved',
    createdAt: mockDate(6, 1),
    updatedAt: mockDate(4),
    replyCount: 2,
  },
  {
    id: 'fb-2',
    tripId: 'trip-2',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    departureTime: mockDate(3),
    type: 'Compliment',
    content: 'Tài xế lái êm, xe sạch sẽ. Sẽ tiếp tục sử dụng dịch vụ.',
    attachmentUrl: null,
    rating: 5,
    status: 'InProgress',
    createdAt: mockDate(3),
    updatedAt: mockDate(2),
    replyCount: 1,
  },
  {
    id: 'fb-3',
    tripId: null,
    routeCode: null,
    routeName: null,
    departureTime: null,
    type: 'Suggestion',
    content: 'Đề xuất thêm trạm dừng gần khu đô thị Đại học Quốc gia Hà Nội.',
    attachmentUrl: null,
    rating: null,
    status: 'New',
    createdAt: mockDate(1, 2),
    updatedAt: null,
    replyCount: 0,
  },
];

/** Luồng phản hồi giả theo id — chỉ trả ở `getMine`, đúng như contract (danh sách không có). */
const MOCK_REPLIES: Record<string, FeedbackReply[]> = {
  'fb-1': [
    {
      id: 'reply-1a',
      userId: 'user-manager',
      userFullName: 'Trần Thị B',
      content: 'Nhà xe xin lỗi vì sự cố hôm đó, đã nhắc nhở tài xế và hoàn 20% giá vé.',
      createdAt: mockDate(5),
    },
    {
      id: 'reply-1b',
      userId: 'user-manager',
      userFullName: 'Trần Thị B',
      content: 'Đã hoàn tiền vào ví của bạn. Cảm ơn bạn đã kiên nhẫn chờ.',
      createdAt: mockDate(4),
    },
  ],
  'fb-2': [
    {
      id: 'reply-2a',
      userId: 'user-manager',
      userFullName: 'Trần Thị B',
      content: 'Cảm ơn bạn đã tin tưởng và dành lời khen cho tài xế. Chúc bạn nhiều chuyến đi vui vẻ!',
      createdAt: mockDate(2),
    },
  ],
};

/** Bỏ `replyCount`, gắn `replies` — dựng đúng hình dạng chi tiết của nhánh giả. */
function toDetail(item: MyFeedback, replies: FeedbackReply[]): MyFeedbackDetail {
  return {
    id: item.id,
    tripId: item.tripId,
    routeCode: item.routeCode,
    routeName: item.routeName,
    departureTime: item.departureTime,
    type: item.type,
    content: item.content,
    attachmentUrl: item.attachmentUrl,
    rating: item.rating,
    status: item.status,
    createdAt: item.createdAt,
    updatedAt: item.updatedAt,
    replies,
  };
}

const mock: FeedbackApi = {
  async listMyFeedbacks(status) {
    await delay(400);

    const list = status ? MOCK_FEEDBACKS.filter((item) => item.status === status) : MOCK_FEEDBACKS;

    // Trả bản sao để màn hình không vô tình sửa mảng gốc.
    return list.map((item) => ({ ...item }));
  },

  async getMine(id) {
    await delay(300);

    const found = MOCK_FEEDBACKS.find((item) => item.id === id);
    if (!found) throw new Error('Không tìm thấy phản ánh');

    return toDetail(found, MOCK_REPLIES[id] ?? []);
  },
};

const feedbackApi: FeedbackApi = USE_MOCK_DATA ? mock : api;

export default feedbackApi;
