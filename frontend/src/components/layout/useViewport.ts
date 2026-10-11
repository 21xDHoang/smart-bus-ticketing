import { Grid } from 'antd';

/**
 * Kích thước màn hình hiện tại. `screens.md === false` = hẹp hơn 768px.
 *
 * Lần render đầu tiên antd trả `screens` rỗng (chưa đo được) — cố ý coi là desktop để không
 * nháy giao diện điện thoại trên máy tính. Khung app chỉ ĐỔI PHẦN TỬ CÓ/KHÔNG theo giá trị
 * này, không đổi loại phần tử gốc — đổi gốc sẽ remount trang con và nuốt trạng thái đang
 * đặt vé (ghế đã chọn, đồng hồ giữ chỗ, kết nối SignalR).
 */
export function useViewport(): { isMobile: boolean } {
  const screens = Grid.useBreakpoint();
  return { isMobile: screens.md === false };
}
