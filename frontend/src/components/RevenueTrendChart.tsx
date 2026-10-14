import { useRef, useState } from 'react';
import { theme, Typography } from 'antd';
import dayjs from 'dayjs';
import type { DailyRevenue } from '../api/revenueDashboardApi';
import { compactVnd, niceTicks, sparseIndices } from './ui/viz';
import { formatVnd } from './ui/format';

const { Text } = Typography;

// Kích thước viewBox cố định — SVG co giãn theo chiều rộng thẻ, giữ nguyên tỉ lệ. `T` là đỉnh
// vùng vẽ (chừa chỗ cho nhãn trục y), `B` chừa chỗ cho nhãn trục x.
const W = 760;
const H = 300;
const MARGIN = { left: 64, right: 20, top: 16, bottom: 40 };
const PLOT_W = W - MARGIN.left - MARGIN.right;
const PLOT_H = H - MARGIN.top - MARGIN.bottom;

/**
 * Biểu đồ đường + vùng "Doanh thu theo ngày" (US 19) — vẽ SVG thủ công để không phải thêm
 * thư viện biểu đồ (package.json là file của Băng). Một chuỗi doanh thu duy nhất → dùng một
 * sắc `colorPrimary`, không cần chú giải (tiêu đề đã nêu tên chuỗi). Màu lấy qua
 * `theme.useToken()` nên tự đổi theo chế độ sáng/tối.
 *
 * Tương tác: rê chuột hiện vạch đứng bám theo ngày gần nhất + chú giải giá trị; bảng dữ liệu
 * (chế độ "Xem bảng") do màn hình cha bật — đường dẫn truy cập thay thế cho người không dùng
 * chuột hoặc không phân biệt được màu.
 */
export default function RevenueTrendChart({ data }: { data: DailyRevenue[] }) {
  const { token } = theme.useToken();
  const svgRef = useRef<SVGSVGElement>(null);
  const [hover, setHover] = useState<number | null>(null);

  if (data.length === 0) return null;

  const maxRevenue = Math.max(...data.map((item) => item.revenue));
  const ticks = niceTicks(maxRevenue, 4);
  const yMax = ticks[ticks.length - 1];

  const xOf = (index: number) =>
    data.length <= 1
      ? MARGIN.left + PLOT_W / 2
      : MARGIN.left + (index / (data.length - 1)) * PLOT_W;

  const yOf = (value: number) =>
    MARGIN.top + PLOT_H - (value / yMax) * PLOT_H;

  const linePath = data
    .map((item, index) => `${index === 0 ? 'M' : 'L'}${xOf(index).toFixed(1)},${yOf(item.revenue).toFixed(1)}`)
    .join(' ');

  const baselineY = MARGIN.top + PLOT_H;
  const areaPath = `${linePath} L${xOf(data.length - 1).toFixed(1)},${baselineY.toFixed(1)} L${xOf(0).toFixed(1)},${baselineY.toFixed(1)} Z`;

  const labelIndices = sparseIndices(data.length, Math.min(5, data.length));
  const lastIndex = data.length - 1;
  const hovered = hover === null ? null : Math.max(0, Math.min(lastIndex, hover));

  const handleMove = (event: React.PointerEvent<SVGSVGElement>) => {
    const svg = svgRef.current;
    if (!svg) return;
    const rect = svg.getBoundingClientRect();
    const ratio = (event.clientX - rect.left) / rect.width;
    const plotRatio = (ratio - MARGIN.left / W) / (PLOT_W / W);
    const index = Math.round(Math.min(1, Math.max(0, plotRatio)) * lastIndex);
    setHover(index);
  };

  return (
    <div style={{ position: 'relative' }}>
      <svg
        ref={svgRef}
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label="Biểu đồ doanh thu theo ngày"
        style={{ width: '100%', height: 'auto', display: 'block' }}
        onPointerMove={handleMove}
        onPointerLeave={() => setHover(null)}
      >
        {/* Lưới ngang + nhãn trục y. */}
        {ticks.map((tick) => {
          const y = yOf(tick);
          return (
            <g key={tick}>
              <line
                x1={MARGIN.left}
                x2={W - MARGIN.right}
                y1={y}
                y2={y}
                stroke={token.colorBorderSecondary}
                strokeWidth={1}
              />
              <text
                x={MARGIN.left - 8}
                y={y + 4}
                textAnchor="end"
                fontSize={12}
                fill={token.colorTextTertiary}
              >
                {compactVnd(tick)}
              </text>
            </g>
          );
        })}

        {/* Vùng phủ dưới đường — màu nền nhạt của primary, đọc như lớp rửa chứ không chói. */}
        <path d={areaPath} fill={token.colorPrimaryBg} />

        {/* Đường doanh thu. */}
        <path
          d={linePath}
          fill="none"
          stroke={token.colorPrimary}
          strokeWidth={2}
          strokeLinejoin="round"
          strokeLinecap="round"
        />

        {/* Chấm đầu cuối — 2px viền màu nền để không chìm khi đè lên lưới. */}
        <circle
          cx={xOf(lastIndex)}
          cy={yOf(data[lastIndex].revenue)}
          r={4}
          fill={token.colorPrimary}
          stroke={token.colorBgContainer}
          strokeWidth={2}
        />

        {/* Nhãn trục x — chỉ vài ngày trải đều. */}
        {labelIndices.map((index) => (
          <text
            key={index}
            x={xOf(index)}
            y={H - MARGIN.bottom + 18}
            textAnchor="middle"
            fontSize={12}
            fill={token.colorTextTertiary}
          >
            {dayjs(data[index].date).format('DD/MM')}
          </text>
        ))}

        {/* Vạch đứng bám theo ngày đang rê — tìm ngày chứ không bắt rê trúng đường 2px. */}
        {hovered !== null && (
          <line
            x1={xOf(hovered)}
            x2={xOf(hovered)}
            y1={MARGIN.top}
            y2={baselineY}
            stroke={token.colorBorder}
            strokeWidth={1}
          />
        )}
      </svg>

      {hovered !== null && (
        <div
          style={{
            position: 'absolute',
            top: 0,
            left: `${(xOf(hovered) / W) * 100}%`,
            transform: 'translateX(-50%)',
            pointerEvents: 'none',
            background: token.colorBgElevated,
            border: `1px solid ${token.colorBorderSecondary}`,
            borderRadius: token.borderRadiusSM,
            boxShadow: token.boxShadowSecondary,
            padding: '8px 10px',
            minWidth: 148,
            maxWidth: 220,
            zIndex: 2,
          }}
        >
          <Text type="secondary" style={{ display: 'block', fontSize: 12 }}>
            {dayjs(data[hovered].date).format('DD/MM/YYYY')}
          </Text>
          <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
            {formatVnd(data[hovered].revenue)}
          </Text>
          <Text type="secondary" style={{ display: 'block', fontSize: 12 }}>
            {data[hovered].tickets.toLocaleString('vi-VN')} vé
          </Text>
        </div>
      )}
    </div>
  );
}
