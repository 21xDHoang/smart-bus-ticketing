import type { Refund } from '../api/refundAdminApi';
import {
  getRefundSourceMeta,
  getRefundStatusMeta,
} from '../api/refundAdminApi';
import { formatDateTime } from './ui/format';

// Xuất danh sách hoàn tiền ra CSV — phần "xuất danh sách" của task "Component lọc và xuất
// danh sách hoàn tiền" (story 8, Nguyễn Đình Băng).
//
// Backend chưa có endpoint xuất thật (dự kiến `GET /admin/refunds/export`), nên file này
// sinh CSV ngay ở phía trình duyệt từ danh sách đã tải qua `refundAdminApi.exportList`.
// Khi endpoint xuất thật có trong api-contract.md thì đổi chỗ gọi sang tải blob từ server —
// bảng cột dưới đây vẫn giữ nguyên để hai bên nói cùng một chuyện.

/** Một ô trong CSV — nếu chứa dấu phẩy/nháy kép/xuống dòng thì bọc nháy kép và nhân đôi nháy. */
function escapeCsvCell(value: string | number): string {
  const text = String(value);
  return /[",\n\r]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** Dựng nội dung CSV (kèm BOM để Excel mở đúng dấu tiếng Việt) từ danh sách hoàn tiền. */
function refundsToCsv(items: Refund[]): string {
  const header = [
    'Mã hoàn tiền',
    'Nguồn gốc',
    'Hành khách',
    'Mã vé',
    'Tuyến',
    'Đi',
    'Đến',
    'Giờ khởi hành',
    'Số ghế',
    'Số tiền hoàn (VND)',
    'Cổng hoàn',
    'Mã giao dịch',
    'Trạng thái',
    'Số lần thử lại',
    'Tạo lúc',
  ];

  const rows = items.map((item) => [
    item.refundCode,
    getRefundSourceMeta(item.source).label,
    item.passengerName,
    item.ticketCode,
    `${item.routeCode} — ${item.routeName}`,
    item.origin,
    item.destination,
    formatDateTime(item.departureTime),
    item.seatNumber,
    // Số nguyên trần (không có dấu phẩy nghìn, không có "₫") để phần mềm bảng tính nhận là số.
    item.amount,
    item.methodName,
    item.paymentCode,
    getRefundStatusMeta(item.status).label,
    item.retryCount,
    formatDateTime(item.createdAt),
  ]);

  return [header, ...rows].map((row) => row.map(escapeCsvCell).join(',')).join('\r\n');
}

/** Đẩy danh sách hoàn tiền xuống trình duyệt dưới dạng file CSV tải về. */
export function downloadRefundsCsv(items: Refund[]): void {
  const blob = new Blob([`﻿${refundsToCsv(items)}`], {
    type: 'text/csv;charset=utf-8;',
  });
  const url = URL.createObjectURL(blob);

  const link = document.createElement('a');
  link.href = url;
  // Tên file có ngày để không ghi đè lẫn nhau khi xuất nhiều lần trong ngày.
  link.download = `hoan-tien-${new Date().toISOString().slice(0, 10)}.csv`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);

  URL.revokeObjectURL(url);
}
