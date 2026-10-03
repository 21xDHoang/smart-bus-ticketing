import type { TagProps } from 'antd';
import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API phản ánh — nối với story 24 "Gửi phản ánh" (Sprint 2).
//
// Backend CHƯA có bảng Feedbacks/FeedbackReplies cũng như endpoint phản ánh: migrate
// bảng là task của Vàng Thị Dăm, API danh sách phản ánh của hành khách là task của
// Nguyễn Duy Kiên. Vì vậy file này dựng theo đúng mẫu của monthlyPassApi.ts: khai báo
// kiểu + bảng nhãn/màu trạng thái, có nhánh dữ liệu giả để màn hình chạy được ngay, và
// nhánh API thật viết sẵn để khi backend xong chỉ cần đổi cờ `USE_MOCK_DATA`.
//
// Hợp đồng endpoint DỰ KIẾN (kebab-case, danh từ số nhiều — quy ước D của nhóm):
//   GET /api/feedbacks/me?status=<FeedbackStatus> → Feedback[] (mảng trần, mới nhất trước)
//
// "Mảng trần" chứ không phải { items, total } cùng lý do GET /monthly-passes/me: một
// hành khách chỉ có vài phản ánh, phân trang là nghi thức thừa. `status` bỏ trống = lấy
// tất cả trạng thái.
// -----------------------------------------------------------------------------

/** Loại phản ánh — khớp cột varchar Type của bảng Feedbacks (dự kiến). */
export type FeedbackType = 'Complaint' | 'Review' | 'Suggestion' | 'Other';

/** Trạng thái xử lý — vòng đời của một phản ánh từ lúc gửi tới khi nhà xe phản hồi. */
export type FeedbackStatus = 'Pending' | 'Processing' | 'Resolved' | 'Rejected';

/** Một phản ánh của hành khách, trả về bởi GET /feedbacks/me. */
export interface Feedback {
  id: string;

  /** Chuyến được phản ánh. null khi phản ánh không gắn với chuyến cụ thể. */
  tripId: string | null;

  /** Tên tuyến của chuyến được phản ánh — chỉ để HIỂN THỊ, ghép từ bảng Trips/Routes
   *  (cùng lối `userFullName` của GET /audit-logs). null khi `tripId` là null. */
  routeName: string | null;

  /** Giờ khởi hành của chuyến (ISO 8601) — chỉ để hiển thị. null khi `tripId` là null. */
  departureTime: string | null;

  type: FeedbackType;

  /** Mức độ hài lòng 1..5. null khi khách không chấm sao. */
  rating: number | null;

  /** Nội dung phản ánh — text tự do. */
  content: string;

  status: FeedbackStatus;

  /** Phản hồi mới nhất từ nhà xe. null khi chưa được phản hồi. */
  adminReply: string | null;

  createdAt: string;

  /** null khi phản ánh chưa từng được sửa (đổi trạng thái / phản hồi). */
  updatedAt: string | null;
}

interface FeedbackMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: TagProps['color'];
}

export const FEEDBACK_TYPE_META: Record<FeedbackType, FeedbackMeta> = {
  Complaint: { label: 'Khiếu nại', color: 'red' },
  Review: { label: 'Đánh giá', color: 'geekblue' },
  Suggestion: { label: 'Góp ý', color: 'green' },
  Other: { label: 'Khác', color: 'default' },
};

export const FEEDBACK_STATUS_META: Record<FeedbackStatus, FeedbackMeta> = {
  Pending: { label: 'Chờ xử lý', color: 'default' },
  Processing: { label: 'Đang xử lý', color: 'processing' },
  Resolved: { label: 'Đã xử lý', color: 'success' },
  Rejected: { label: 'Từ chối', color: 'error' },
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
  listMyFeedbacks: (status?: FeedbackStatus) => Promise<Feedback[]>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì endpoint phản ánh chưa có
// (xem ghi chú đầu file). Đổi thành `false` khi Kiên xong API danh sách phản ánh.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: FeedbackApi = {
  // GET /feedbacks/me?status=… — danh sách phản ánh của chính người gọi. Backend tự đọc
  // userId từ JWT nên không cần truyền; chỉ gửi `status` khi thực sự có lọc.
  listMyFeedbacks: (status) => {
    const params: Record<string, string> = {};
    if (status) params.status = status;

    return axiosClient.get<Feedback[], Feedback[]>('/feedbacks/me', { params });
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
function mockDate(daysAgo: number, hoursAgo = 0): string {
  const date = new Date();
  date.setDate(date.getDate() - daysAgo);
  date.setHours(date.getHours() - hoursAgo);
  return date.toISOString();
}

// Vài phản ánh giả phủ đủ 4 trạng thái và các loại, để màn hình "phản ánh của tôi" dựng
// được giao diện kể cả khi backend chưa xong. Tên tuyến khớp monthlyPassApi để nhất quán.
const MOCK_FEEDBACKS: Feedback[] = [
  {
    id: 'fb-1',
    tripId: 'trip-1',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    departureTime: mockDate(6, 1),
    type: 'Complaint',
    rating: null,
    content: 'Xe xuất phát trễ 25 phút so với lịch, không có thông báo gì tại trạm.',
    status: 'Resolved',
    adminReply:
      'Cảm ơn bạn đã phản ánh. Chúng tôi đã làm việc với tài xế và điều chỉnh lịch chạy để hạn chế trễ giờ.',
    createdAt: mockDate(6, 1),
    updatedAt: mockDate(4),
  },
  {
    id: 'fb-2',
    tripId: 'trip-2',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    departureTime: mockDate(3),
    type: 'Review',
    rating: 4,
    content: 'Tài xế lái êm, xe sạch sẽ. Sẽ tiếp tục sử dụng dịch vụ.',
    status: 'Processing',
    adminReply: null,
    createdAt: mockDate(3),
    updatedAt: null,
  },
  {
    id: 'fb-3',
    tripId: null,
    routeName: null,
    departureTime: null,
    type: 'Suggestion',
    rating: null,
    content: 'Đề xuất thêm trạm dừng gần khu đô thị Đại học Quốc gia Hà Nội.',
    status: 'Pending',
    adminReply: null,
    createdAt: mockDate(1, 2),
    updatedAt: null,
  },
  {
    id: 'fb-4',
    tripId: 'trip-4',
    routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    departureTime: mockDate(12),
    type: 'Complaint',
    rating: 1,
    content: 'Máy lạnh không hoạt động, thái độ phục vụ của phụ xe chưa tốt.',
    status: 'Rejected',
    adminReply:
      'Chúng tôi đã kiểm tra và không ghi nhận sự cố máy lạnh trên chuyến này. Phản ánh chưa đủ cơ sở để tiếp nhận.',
    createdAt: mockDate(12),
    updatedAt: mockDate(10),
  },
  {
    id: 'fb-5',
    tripId: 'trip-5',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    departureTime: mockDate(8, 3),
    type: 'Review',
    rating: 5,
    content: 'Rất hài lòng với dịch vụ, xe đúng giờ và nhân viên thân thiện.',
    status: 'Resolved',
    adminReply: 'Cảm ơn bạn đã tin tưởng sử dụng dịch vụ của chúng tôi.',
    createdAt: mockDate(8, 3),
    updatedAt: mockDate(7),
  },
  {
    id: 'fb-6',
    tripId: null,
    routeName: null,
    departureTime: null,
    type: 'Other',
    rating: null,
    content: 'Muốn hỏi về chính sách giảm giá cho sinh viên.',
    status: 'Processing',
    adminReply: 'Chúng tôi đang kiểm tra hồ sơ ưu đãi và sẽ phản hồi trong 24 giờ.',
    createdAt: mockDate(0, 5),
    updatedAt: mockDate(0, 3),
  },
];

const mock: FeedbackApi = {
  async listMyFeedbacks(status) {
    await delay(400);

    const list = status ? MOCK_FEEDBACKS.filter((item) => item.status === status) : MOCK_FEEDBACKS;

    // Trả bản sao để màn hình không vô tình sửa mảng gốc.
    return list.map((item) => ({ ...item }));
  },
};

const feedbackApi: FeedbackApi = USE_MOCK_DATA ? mock : api;

export default feedbackApi;
