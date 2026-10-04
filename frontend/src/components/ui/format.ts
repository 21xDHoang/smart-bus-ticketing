import dayjs from 'dayjs';

// Định dạng hiển thị dùng chung — trước đây mỗi màn tự chép lại một bản, chỉ cần một màn
// sửa là lệch nhau. Chỗ này chỉ định dạng để HIỂN THỊ; logic nghiệp vụ (biên ngày, múi giờ
// Việt Nam…) không nằm ở đây.

/** Cỡ trang cho mọi bảng phân trang — trước đây mỗi màn tự khai một mảng giống hệt nhau. */
export const PAGE_SIZE_OPTIONS = [10, 20, 50];

/** Tiền VND — ví dụ 200000 → "200.000 đ". Thống nhất một kiểu cho cả app (không dùng "₫"). */
export function formatVnd(price: number): string {
  return `${price.toLocaleString('vi-VN')} đ`;
}

/** Ngày — hợp đồng trả ISO 8601 UTC, chỉ cần phần ngày: DD/MM/YYYY. */
export function formatDate(iso: string): string {
  return dayjs(iso).format('DD/MM/YYYY');
}

/** Ngày + giờ: DD/MM/YYYY HH:mm. */
export function formatDateTime(iso: string): string {
  return dayjs(iso).format('DD/MM/YYYY HH:mm');
}

/** Giờ trong ngày: HH:mm. */
export function formatTime(iso: string): string {
  return dayjs(iso).format('HH:mm');
}
