// -----------------------------------------------------------------------------
// API "Màn hình cấu hình mẫu báo cáo (chọn cột, khoảng thời gian)" — phục vụ task
// US 21 "Xuất báo cáo Excel/PDF", Sprint 5, dòng "Màn hình cấu hình mẫu báo cáo"
// (Dương Thị Hạnh). Một MẪU báo cáo = loại báo cáo + tập cột được chọn + khoảng
// thời gian, để khi xuất file (task của Hoàng / nút xuất của Thịnh) chỉ cần lấy đúng
// mẫu đã cấu hình thay vì chọn lại cột và ngày mỗi lần.
//
// ⚠️ KHÔNG có bảng CSDL cho mẫu báo cáo: backlog US 21 KHÔNG có dòng migrate nào cho
// bảng này (khác NotificationTemplates ở docs/37), và luật nhóm cấm tự thêm bảng/migration.
// Nên file này lưu vào localStorage của trình duyệt — cấu hình là lựa chọn riêng của người
// soạn báo cáo, không phải dữ liệu nghiệp vụ cần chia sẻ giữa các máy. Khi nhóm chốt thêm
// bảng + endpoint (đi qua Dăm + api-contract.md) thì chỉ cần thay phần lưu trữ, giữ nguyên
// hình dạng `ReportTemplate`.
// -----------------------------------------------------------------------------

import dayjs from 'dayjs';

/** Loại báo cáo — khớp ba bề mặt báo cáo đang có của US 19/20/23. */
export type ReportType = 'Revenue' | 'FillRate' | 'AuditLog';

/** Một cột của báo cáo — `key` là khoá ổn định để lưu vào mẫu, `label` để hiển thị. */
export interface ReportColumn {
  key: string;
  label: string;
}

export interface ReportTypeMeta {
  label: string;
  description: string;
  columns: ReportColumn[];
}

/** Một mẫu báo cáo đã cấu hình. */
export interface ReportTemplate {
  id: string;
  name: string;
  reportType: ReportType;
  /** Các khoá cột được chọn, theo đúng thứ tự hiển thị trên báo cáo. */
  columns: string[];
  /** Ngày bắt đầu khoảng thời gian (yyyy-MM-dd, lịch Việt Nam), tính cả. */
  from: string;
  /** Ngày kết thúc khoảng thời gian (yyyy-MM-dd), tính cả. */
  to: string;
  createdAt: string;
  updatedAt: string | null;
}

/** Body khi thêm/sửa mẫu — `id`/`createdAt`/`updatedAt` do tầng lưu trữ giữ. */
export interface ReportTemplatePayload {
  name: string;
  reportType: ReportType;
  columns: string[];
  from: string;
  to: string;
}

/**
 * Cột có sẵn theo từng loại báo cáo — bám theo đúng cột đang hiển thị ở các màn báo cáo
 * hiện tại: RevenueByRoutePage, FillRateReportPage, AuditLogPage. Khi các màn đó đổi cột
 * thì cập nhật ở đây.
 */
export const REPORT_TYPE_META: Record<ReportType, ReportTypeMeta> = {
  Revenue: {
    label: 'Doanh thu',
    description: 'Doanh thu bán vé theo tuyến.',
    columns: [
      { key: 'routeName', label: 'Tuyến' },
      { key: 'payments', label: 'Số giao dịch' },
      { key: 'revenue', label: 'Doanh thu' },
      { key: 'sharePct', label: 'Tỉ trọng' },
    ],
  },
  FillRate: {
    label: 'Tỷ lệ lấp đầy',
    description: 'Tỷ lệ lấp đầy ghế theo từng chuyến.',
    columns: [
      { key: 'departureTime', label: 'Khởi hành' },
      { key: 'tripCode', label: 'Mã chuyến' },
      { key: 'routeName', label: 'Tuyến' },
      { key: 'busPlate', label: 'Biển số' },
      { key: 'capacity', label: 'Sức chứa' },
      { key: 'passengers', label: 'Vé đã bán' },
      { key: 'fillRatePct', label: 'Tỷ lệ lấp đầy' },
    ],
  },
  AuditLog: {
    label: 'Nhật ký hoạt động',
    description: 'Nhật ký thao tác của người dùng trong hệ thống.',
    columns: [
      { key: 'createdAt', label: 'Thời gian' },
      { key: 'user', label: 'Người thao tác' },
      { key: 'action', label: 'Hành động' },
      { key: 'target', label: 'Đối tượng' },
      { key: 'ipAddress', label: 'Địa chỉ IP' },
    ],
  },
};

/** Ba loại theo đúng thứ tự hiển thị — dùng cho ô chọn và nhãn trong bảng. */
export const REPORT_TYPES: ReportType[] = ['Revenue', 'FillRate', 'AuditLog'];

/** Nhãn + khoá cột theo loại — tiện cho ô chọn cột trong form. */
export function columnsOf(type: ReportType): ReportColumn[] {
  return REPORT_TYPE_META[type].columns;
}

/** Khoảng thời gian mặc định: hôm nay (lịch Việt Nam) — cùng lối defaultFillRateFilter. */
export function defaultReportRange(): { from: string; to: string } {
  const today = dayjs().format('YYYY-MM-DD');
  return { from: today, to: today };
}

export interface ReportTemplateApi {
  list: () => Promise<ReportTemplate[]>;
  create: (payload: ReportTemplatePayload) => Promise<ReportTemplate>;
  update: (id: string, payload: ReportTemplatePayload) => Promise<ReportTemplate>;
  remove: (id: string) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Tầng lưu trữ — localStorage, không có backend (xem chú thích đầu file). Mọi lần đọc/ghi
// đều bọc try/catch: localStorage có thể bị chặn (private window) hoặc đầy — lúc đó trả mảng
// rỗng / ném lỗi có lời rõ, không để màn hình trắng.
// ---------------------------------------------------------------------------

const STORAGE_KEY = 'smartBus.reportTemplates.v1';

function readAll(): ReportTemplate[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as ReportTemplate[]) : [];
  } catch {
    return [];
  }
}

function writeAll(templates: ReportTemplate[]): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(templates));
}

const nextId = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `tpl-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

const reportTemplateApi: ReportTemplateApi = {
  async list() {
    await delay(120);
    return readAll().sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  },

  async create(payload) {
    await delay(200);
    const templates = readAll();

    const template: ReportTemplate = {
      id: nextId(),
      name: payload.name.trim(),
      reportType: payload.reportType,
      columns: [...payload.columns],
      from: payload.from,
      to: payload.to,
      createdAt: new Date().toISOString(),
      updatedAt: null,
    };

    writeAll([template, ...templates]);
    return template;
  },

  async update(id, payload) {
    await delay(200);
    const templates = readAll();
    const index = templates.findIndex((template) => template.id === id);

    if (index === -1) {
      throw new Error('Không tìm thấy mẫu báo cáo cần sửa.');
    }

    const updated: ReportTemplate = {
      ...templates[index],
      name: payload.name.trim(),
      reportType: payload.reportType,
      columns: [...payload.columns],
      from: payload.from,
      to: payload.to,
      updatedAt: new Date().toISOString(),
    };

    templates[index] = updated;
    writeAll(templates);
    return updated;
  },

  async remove(id) {
    await delay(150);
    writeAll(readAll().filter((template) => template.id !== id));
  },
};

export default reportTemplateApi;
