import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API "Màn hình Admin quản lý mẫu thông báo" — phục vụ task Sprint 5 dòng 24 (US 10
// "Thông báo trạm"), Dương Thị Hạnh. Nối với NotificationTemplatesController.
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Màn quản lý mẫu thông báo":
//   GET /notification-templates          → NotificationTemplate[]
//   PUT /notification-templates/{type}   → { title, body, isActive } → NotificationTemplate
//
// Mẫu là phần GHI ĐÈ (docs/37 §2.1): bảng chỉ chứa các dòng đã soạn, loại chưa soạn KHÔNG xuất
// hiện trong GET. Màn hình tự vẽ đủ TÁM loại từ NOTIFICATION_TYPE_META rồi ghép dòng đã soạn vào.
// -----------------------------------------------------------------------------

/**
 * Loại thông báo — TÁM giá trị của `NotificationType` (backend/SmartBus.Api/Entities/
 * NotificationType.cs). Backend trả chuỗi chứ không trả số, nên union này khớp thẳng JSON.
 */
export type NotificationTemplateType =
  | 'CancellationApproved'
  | 'CancellationRejected'
  | 'TicketChangeApproved'
  | 'TicketChangeRejected'
  | 'RefundSucceeded'
  | 'RefundFailed'
  | 'TripDelayed'
  | 'TripScheduleChanged';

/** Một mẫu đã soạn — khớp entity NotificationTemplate. */
export interface NotificationTemplate {
  id: string;
  type: NotificationTemplateType;
  title: string;
  body: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

/** Body khi soạn/sửa mẫu — PUT /notification-templates/{type}. */
export interface NotificationTemplatePayload {
  title: string;
  body: string;
  isActive: boolean;
}

/**
 * Chỗ trống hợp lệ theo từng loại — bản soi gương của `NotificationTemplateService.ValidPlaceholders`
 * (backend). Dùng để vẽ gợi ý trong form; nguồn thật để kiểm tra vẫn là backend (lỗi sai chỗ trống
 * trả về kèm `errors.title`/`errors.body`).
 */
export type PlaceholderToken =
  | 'RejectReason'
  | 'RouteName'
  | 'OldDepartureTime'
  | 'NewDepartureTime'
  | 'OldArrivalTime'
  | 'NewArrivalTime';

/** Giải nghĩa ngắn từng chỗ trống để form hiển thị đọc được. */
export const PLACEHOLDER_HINTS: Record<PlaceholderToken, string> = {
  RejectReason: 'lý do từ chối do quản lý nhập',
  RouteName: 'tên tuyến',
  OldDepartureTime: 'giờ khởi hành cũ',
  NewDepartureTime: 'giờ khởi hành mới',
  OldArrivalTime: 'giờ tới dự kiến cũ',
  NewArrivalTime: 'giờ tới dự kiến mới',
};

export interface NotificationTypeMeta {
  /** Nhãn tiếng Việt — khớp bảng "Tám loại thông báo" trong api-contract.md. */
  label: string;
  /** Mô tả ngắn để dòng trong bảng hiểu loại này báo khi nào. */
  description: string;
  /** Các chỗ trống được dùng trong Title/Body của loại này. Rỗng = không dùng chỗ trống. */
  allowedPlaceholders: PlaceholderToken[];
}

export const NOTIFICATION_TYPE_META: Record<NotificationTemplateType, NotificationTypeMeta> = {
  CancellationApproved: {
    label: 'Đã duyệt yêu cầu huỷ vé',
    description: 'Báo cho hành khách khi yêu cầu huỷ vé được duyệt.',
    allowedPlaceholders: [],
  },
  CancellationRejected: {
    label: 'Từ chối yêu cầu huỷ vé',
    description: 'Báo cho hành khách khi yêu cầu huỷ vé bị từ chối.',
    allowedPlaceholders: ['RejectReason'],
  },
  TicketChangeApproved: {
    label: 'Đã duyệt yêu cầu đổi chuyến',
    description: 'Báo cho hành khách khi yêu cầu đổi chuyến được duyệt.',
    allowedPlaceholders: [],
  },
  TicketChangeRejected: {
    label: 'Từ chối yêu cầu đổi chuyến',
    description: 'Báo cho hành khách khi yêu cầu đổi chuyến bị từ chối.',
    allowedPlaceholders: ['RejectReason'],
  },
  RefundSucceeded: {
    label: 'Đã hoàn tiền',
    description: 'Báo khi khoản hoàn tiền đã thực hiện thành công.',
    allowedPlaceholders: [],
  },
  RefundFailed: {
    label: 'Hoàn tiền thất bại',
    description: 'Báo khi khoản hoàn tiền hết lịch thử lại vẫn chưa thực hiện được.',
    allowedPlaceholders: [],
  },
  TripDelayed: {
    label: 'Chuyến xe bị trễ',
    description: 'Báo cho hành khách khi chuyến bị lùi giờ khởi hành hoặc giờ tới.',
    allowedPlaceholders: [
      'RouteName',
      'OldDepartureTime',
      'NewDepartureTime',
      'OldArrivalTime',
      'NewArrivalTime',
    ],
  },
  TripScheduleChanged: {
    label: 'Lịch trình chuyến xe thay đổi',
    description: 'Báo khi lịch trình chuyến đổi giờ (sớm hơn, hoặc thêm/bỏ mốc tới).',
    allowedPlaceholders: [
      'RouteName',
      'OldDepartureTime',
      'NewDepartureTime',
      'OldArrivalTime',
      'NewArrivalTime',
    ],
  },
};

/** Tám loại theo đúng thứ tự enum — dùng làm thứ tự dòng trong bảng. */
export const NOTIFICATION_TYPES: NotificationTemplateType[] = [
  'CancellationApproved',
  'CancellationRejected',
  'TicketChangeApproved',
  'TicketChangeRejected',
  'RefundSucceeded',
  'RefundFailed',
  'TripDelayed',
  'TripScheduleChanged',
];

export interface NotificationTemplateApi {
  list: () => Promise<NotificationTemplate[]>;
  upsert: (type: NotificationTemplateType, payload: NotificationTemplatePayload) => Promise<NotificationTemplate>;
}

const notificationTemplateApi: NotificationTemplateApi = {
  list: () => axiosClient.get<NotificationTemplate[], NotificationTemplate[]>('/notification-templates'),

  // PUT là upsert theo type: chưa có mẫu cho loại đó thì tạo, có rồi thì sửa.
  upsert: (type, payload) =>
    axiosClient.put<NotificationTemplate, NotificationTemplate>(`/notification-templates/${type}`, payload),
};

export default notificationTemplateApi;
