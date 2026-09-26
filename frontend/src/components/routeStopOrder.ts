import type { RouteStop } from '../api/routeStopApi';

/**
 * Đưa phần tử ở `fromIndex` tới vị trí `toIndex`; các phần tử ở giữa dồn lại một bậc.
 *
 * Nghĩa của thao tác là "phần tử được kéo CHIẾM VỊ TRÍ của dòng bị thả lên": kéo dòng 1
 * thả vào dòng 3 của [A, B, C, D] cho ra [B, C, A, D] — A thành dòng 3, đúng chỗ con trỏ.
 *
 * Hàm thuần, không đụng React: nhận mảng cũ, trả mảng mới, không sửa mảng đầu vào. Nhờ
 * vậy kiểm chứng được bằng jsdom — jsdom không cài đặt `DataTransfer` nên không mô phỏng
 * nổi một cú kéo-thả thật.
 *
 * Nằm ở file riêng chứ không để chung `RouteStopBoard.tsx`: file có cả component lẫn hàm
 * thường sẽ phá Fast Refresh (cảnh báo `react(only-export-components)` của oxlint).
 */
export function moveStop(stops: RouteStop[], fromIndex: number, toIndex: number): RouteStop[] {
  const outOfRange =
    fromIndex < 0 || toIndex < 0 || fromIndex >= stops.length || toIndex >= stops.length;

  // Ra ngoài khoảng hoặc đứng yên tại chỗ thì không có gì để đổi — trả nguyên mảng cũ để
  // cha không phải render lại vô ích.
  if (outOfRange || fromIndex === toIndex) return stops;

  const next = [...stops];
  const [moved] = next.splice(fromIndex, 1);
  next.splice(toIndex, 0, moved);

  return next;
}
