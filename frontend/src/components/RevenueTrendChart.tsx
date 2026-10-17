import { Area } from '@ant-design/charts';
import { theme } from 'antd';
import dayjs from 'dayjs';
import type { DailyRevenue } from '../api/revenueDashboardApi';
import { useColorMode } from './ui/colorMode';
import { formatVnd } from './ui/format';
import { compactVnd } from './ui/viz';

/**
 * Biểu đồ đường + vùng "Doanh thu theo thời gian" (US 19) — vẽ bằng @ant-design/charts (thư
 * viện biểu đồ chính thức của Ant Design, dựa trên G2). Thay cho bản SVG vẽ tay trước đây: bản
 * này tận dụng sẵn tooltip, trục, co giãn responsive và animation của thư viện, nên không phải
 * tự tính toạ độ như trước.
 *
 * Một chuỗi doanh thu duy nhất → dùng một sắc `colorPrimary` cho đường, nền nhạt
 * `colorPrimaryBg` cho vùng, không cần chú giải (tiêu đề thẻ đã nêu tên chuỗi). Màu lấy qua
 * `theme.useToken()` nên tự đổi theo chế độ sáng/tối; theme của G2 cũng được bật theo `mode`
 * để tooltip và các phần chưa ghi đè không bị lệch nền. Bảng dữ liệu (chế độ "Xem bảng") do
 * màn hình cha bật — đường dẫn truy cập thay thế cho người không dùng chuột hoặc không phân
 * biệt được màu.
 */
export default function RevenueTrendChart({ data }: { data: DailyRevenue[] }) {
  const { token } = theme.useToken();
  const { mode } = useColorMode();

  if (data.length === 0) return null;

  return (
    <Area
      data={data}
      xField="date"
      yField="revenue"
      height={300}
      autoFit
      theme={{ type: mode === 'dark' ? 'dark' : 'classic' }}
      scale={{
        x: { type: 'time', nice: true },
        y: { nice: true, domainMin: 0 },
      }}
      axis={{
        x: {
          tickCount: 5,
          labelFormatter: (date: string | Date) => dayjs(date).format('DD/MM'),
          labelFill: token.colorTextTertiary,
          labelFontSize: 12,
          tick: false,
          line: false,
          grid: false,
        },
        y: {
          labelFormatter: (value: number) => compactVnd(Number(value)),
          labelFill: token.colorTextTertiary,
          labelFontSize: 12,
          tick: false,
          line: false,
          grid: true,
          gridStroke: token.colorBorderSecondary,
          gridLineWidth: 1,
        },
      }}
      style={{
        fill: token.colorPrimaryBg,
        fillOpacity: 1,
        stroke: token.colorPrimary,
        lineWidth: 2,
      }}
      tooltip={{
        title: (d: DailyRevenue) => dayjs(d.date).format('DD/MM/YYYY'),
        items: [
          {
            name: 'Doanh thu',
            field: 'revenue',
            color: token.colorPrimary,
            valueFormatter: (value: number) => formatVnd(Number(value)),
          },
          {
            name: 'Vé bán',
            field: 'tickets',
            color: token.colorTextTertiary,
            valueFormatter: (value: number) => Number(value).toLocaleString('vi-VN'),
          },
        ],
      }}
    />
  );
}
