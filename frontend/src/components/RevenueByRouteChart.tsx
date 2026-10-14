import { useState } from 'react';
import { theme, Typography } from 'antd';
import type { RouteRevenue } from '../api/revenueDashboardApi';
import { compactVnd, niceTicks } from './ui/viz';
import { formatVnd } from './ui/format';

const { Text } = Typography;

// Biểu đồ ngang: lề trái rộng để chứa tên tuyến (dài), lề phải chừa chỗ cho giá trị ở đầu
// mỗi thanh. Chiều cao tính theo số tuyến nên viewBox đổi theo dữ liệu.
const W = 760;
const MARGIN = { left: 240, right: 88, top: 12, bottom: 44 };
const ROW_H = 44;
const BAR_H = 22;
const PLOT_W = W - MARGIN.left - MARGIN.right;

/** Hình thanh bo tròn đúng một đầu — đầu phải (đầu dữ liệu) bo 4px, đầu trái (trục gốc) vuông. */
function barPath(x: number, y: number, width: number, height: number, radius: number): string {
  const r = Math.min(radius, width / 2, height / 2);
  const right = x + width;
  const bottom = y + height;
  return [
    `M${x},${y}`,
    `L${right - r},${y}`,
    `Q${right},${y} ${right},${y + r}`,
    `L${right},${bottom - r}`,
    `Q${right},${bottom} ${right - r},${bottom}`,
    `L${x},${bottom}`,
    'Z',
  ].join(' ');
}

/**
 * Biểu đồ thanh ngang "Doanh thu theo tuyến" (US 19) — so sánh độ lớn giữa các tuyến. Một
 * thước đo duy nhất nên mọi thanh dùng chung một sắc `colorPrimary`; danh tính từng thanh do
 * NHÃN tên tuyến ở trục y đảm nhận, không phải màu. Màu lấy qua `theme.useToken()` nên tự đổi
 * theo chế độ sáng/tối. Rê chuột lên thanh hiện chú giải; bảng dữ liệu do màn hình cha bật.
 */
export default function RevenueByRouteChart({ data }: { data: RouteRevenue[] }) {
  const { token } = theme.useToken();
  const [hover, setHover] = useState<number | null>(null);

  if (data.length === 0) return null;

  const H = MARGIN.top + data.length * ROW_H + MARGIN.bottom;
  const maxRevenue = Math.max(...data.map((item) => item.revenue));
  const ticks = niceTicks(maxRevenue, 4);
  const xMax = ticks[ticks.length - 1];

  const xOf = (value: number) => MARGIN.left + (value / xMax) * PLOT_W;
  const centerYOf = (index: number) => MARGIN.top + index * ROW_H + ROW_H / 2;

  return (
    <div style={{ position: 'relative' }}>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label="Biểu đồ doanh thu theo tuyến"
        style={{ width: '100%', height: 'auto', display: 'block' }}
      >
        {/* Lưới dọc + nhãn trục x (doanh thu). */}
        {ticks.map((tick) => {
          const x = xOf(tick);
          return (
            <g key={tick}>
              <line
                x1={x}
                x2={x}
                y1={MARGIN.top}
                y2={H - MARGIN.bottom}
                stroke={token.colorBorderSecondary}
                strokeWidth={1}
              />
              <text
                x={x}
                y={H - MARGIN.bottom + 18}
                textAnchor="middle"
                fontSize={12}
                fill={token.colorTextTertiary}
              >
                {compactVnd(tick)}
              </text>
            </g>
          );
        })}

        {data.map((item, index) => {
          const centerY = centerYOf(index);
          const barTop = centerY - BAR_H / 2;
          const width = Math.max(0, xOf(item.revenue) - MARGIN.left);
          const highlighted = hover === index;

          return (
            <g key={item.routeCode}>
              {/* Tên tuyến ở trục y. */}
              <text
                x={MARGIN.left - 12}
                y={centerY + 4}
                textAnchor="end"
                fontSize={13}
                fill={token.colorText}
              >
                {item.routeName}
              </text>

              {/* Vùng bắt rê lớn hơn thanh — cả thanh lẫn khoảng trống quanh đều trỏ vào cùng một chú giải. */}
              <rect
                x={MARGIN.left}
                y={centerY - ROW_H / 2}
                width={PLOT_W}
                height={ROW_H}
                fill="transparent"
                onPointerEnter={() => setHover(index)}
                onPointerLeave={() => setHover(null)}
              />

              {/* Thanh doanh thu. */}
              <path
                d={barPath(MARGIN.left, barTop, width, BAR_H, 4)}
                fill={token.colorPrimary}
                opacity={hover === null || highlighted ? 1 : 0.55}
              />

              {/* Giá trị ở đầu thanh. */}
              <text
                x={xOf(item.revenue) + 8}
                y={centerY + 4}
                fontSize={12}
                fill={token.colorTextSecondary}
                style={{ fontVariantNumeric: 'tabular-nums' }}
              >
                {compactVnd(item.revenue)}
              </text>
            </g>
          );
        })}
      </svg>

      {hover !== null && (
        <div
          style={{
            position: 'absolute',
            top: 0,
            left: `${(xOf(data[hover].revenue) / W) * 100}%`,
            transform: 'translateX(-50%)',
            pointerEvents: 'none',
            background: token.colorBgElevated,
            border: `1px solid ${token.colorBorderSecondary}`,
            borderRadius: token.borderRadiusSM,
            boxShadow: token.boxShadowSecondary,
            padding: '8px 10px',
            minWidth: 180,
            maxWidth: 240,
            zIndex: 2,
          }}
        >
          <Text type="secondary" style={{ display: 'block', fontSize: 12 }}>
            Tuyến {data[hover].routeCode}
          </Text>
          <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
            {formatVnd(data[hover].revenue)}
          </Text>
        </div>
      )}
    </div>
  );
}
