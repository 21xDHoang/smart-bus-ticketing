import { useMemo } from 'react';
import type { ReactNode } from 'react';
import { Empty, theme, Typography } from 'antd';

const { Text } = Typography;

/** Một ô của biểu đồ: số lượt khách LÊN XE tại một trạm trong một giờ. */
export interface PassengerDensityCell {
  /** Khoá trạm — dùng làm danh tính của hàng, không hiển thị. */
  stationId: string;
  /** Tên trạm hiển thị ở cột đầu. */
  stationName: string;
  /** Giờ trong ngày, 0–23, theo giờ Việt Nam (UTC+7). */
  hour: number;
  /** Số lượt khách lên xe tại trạm trong giờ đó. */
  passengers: number;
}

/** Một hàng của biểu đồ — cho phép màn hình cha giữ đúng thứ tự trạm của tuyến. */
export interface PassengerDensityStation {
  id: string;
  name: string;
}

export interface PassengerDensityHeatmapProps {
  data: readonly PassengerDensityCell[];
  /**
   * Thứ tự trạm hiển thị. Bỏ trống thì lấy từ `data` và sắp theo tên.
   * Truyền vào khi cần giữ thứ tự trạm trên tuyến thay vì thứ tự chữ cái.
   */
  stations?: readonly PassengerDensityStation[];
  /** Các giờ cần vẽ; mặc định đủ 24 giờ. Giờ ngoài 0–23 bị bỏ. */
  hours?: readonly number[];
  /** Trần màu; bỏ trống thì lấy giá trị lớn nhất trong dữ liệu. */
  maxPassengers?: number;
  /** Thay cho câu mặc định khi không có dữ liệu. */
  emptyText?: ReactNode;
}

const ALL_HOURS = Array.from({ length: 24 }, (_, hour) => hour);
const HOUR_COL_WIDTH = 36;
const STATION_COL_WIDTH = 200;

/**
 * Biểu đồ nhiệt "mật độ hành khách theo giờ và trạm" — US 20, task *"Component biểu đồ nhiệt mật
 * độ hành khách theo giờ và trạm"* (Sprint 5 dòng 56 — Hoàng Văn Thịnh; phiên làm việc này Dăm
 * nhận làm hộ theo yêu cầu).
 *
 * Vẽ bằng bảng HTML + CSS, **không thêm thư viện biểu đồ**: component chỉ hiển thị dữ liệu đã
 * tổng hợp, không tự gọi API. Cách này giữ đúng lối của các biểu đồ tự vẽ khác trong repo
 * (`RevenueByRouteChart`, `viz.ts` ghi rõ "không dùng thư viện ngoài để không phải đụng
 * package.json") và cho bảng một ngữ nghĩa thật: trình đọc màn hình đọc được hàng/cột, còn màu
 * chỉ là kênh phụ — mỗi ô đều có số và tooltip, không phụ thuộc vào việc phân biệt màu.
 *
 * Đơn vị dữ liệu là **lượt khách lên xe**, không phải vé bán: `TicketValidations.BoardingStopId`
 * mới là trạm khách thực sự lên (doc-comment của entity ghi rõ nó khác `Ticket.BoardingStopId` —
 * trạm khách chọn lúc mua). Hiện **chưa có endpoint trả dữ liệu theo trạm**; màn hình cha sẽ
 * truyền mảng đã tổng hợp vào khi API đó có (bàn giao ở `docs/51`).
 *
 * Màu lấy qua `theme.useToken()` nên tự đổi theo chế độ sáng/tối; ô càng đậm càng nhiều khách.
 */
export default function PassengerDensityHeatmap({
  data,
  stations,
  hours = ALL_HOURS,
  maxPassengers,
  emptyText = 'Chưa có dữ liệu mật độ hành khách theo trạm.',
}: PassengerDensityHeatmapProps) {
  const { token } = theme.useToken();

  const grid = useMemo(
    () => buildGrid(data, stations, hours, maxPassengers),
    [data, stations, hours, maxPassengers],
  );

  if (grid.stations.length === 0 || grid.hours.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={emptyText} />;
  }

  return (
    <div>
      <div style={{ overflowX: 'auto' }}>
        <table
          aria-label="Biểu đồ nhiệt mật độ hành khách theo giờ và trạm"
          style={{
            width: '100%',
            minWidth: STATION_COL_WIDTH + grid.hours.length * HOUR_COL_WIDTH,
            borderCollapse: 'separate',
            borderSpacing: 2,
          }}
        >
          <caption
            style={{
              captionSide: 'top',
              textAlign: 'left',
              paddingBottom: token.paddingXS,
            }}
          >
            <Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
              Số lượt khách lên xe tại mỗi trạm theo giờ (giờ Việt Nam).
            </Text>
          </caption>

          <thead>
            <tr>
              <th
                scope="col"
                style={{
                  ...headCellStyle(token),
                  width: STATION_COL_WIDTH,
                  minWidth: STATION_COL_WIDTH,
                  textAlign: 'left',
                  padding: `0 ${token.paddingXS}px`,
                }}
              >
                Trạm dừng
              </th>
              {grid.hours.map((hour) => (
                <th
                  key={hour}
                  scope="col"
                  style={{ ...headCellStyle(token), width: HOUR_COL_WIDTH }}
                >
                  {formatHour(hour)}
                </th>
              ))}
            </tr>
          </thead>

          <tbody>
            {grid.stations.map((station) => (
              <tr key={station.id}>
                <th
                  scope="row"
                  style={{
                    ...stationCellStyle(token),
                    width: STATION_COL_WIDTH,
                    minWidth: STATION_COL_WIDTH,
                    maxWidth: STATION_COL_WIDTH,
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    textAlign: 'left',
                    padding: `0 ${token.paddingXS}px`,
                  }}
                  title={station.name}
                >
                  {station.name}
                </th>
                {grid.hours.map((hour) => {
                  const value = grid.counts.get(cellKey(station.id, hour)) ?? 0;
                  const ratio = grid.max > 0 ? Math.min(1, value / grid.max) : 0;
                  const background = cellBackground(value, ratio, token);

                  return (
                    <td
                      key={hour}
                      style={{ padding: 0, background, borderRadius: token.borderRadiusSM }}
                      title={`${station.name} — ${formatHourRange(hour)}: ${formatFullCount(value)} lượt khách`}
                    >
                      <div
                        style={{
                          height: 30,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          fontSize: token.fontSizeSM,
                          fontVariantNumeric: 'tabular-nums',
                          color: ratio >= 0.55 && value > 0
                            ? token.colorTextLightSolid
                            : token.colorText,
                        }}
                      >
                        {value > 0 ? formatCompactCount(value) : ''}
                      </div>
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: token.paddingXS,
          marginTop: token.paddingSM,
        }}
      >
        <Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
          0
        </Text>
        <div
          style={{
            width: 160,
            height: 10,
            borderRadius: 5,
            background: `linear-gradient(90deg, ${token.colorFillQuaternary}, ${token.colorPrimary})`,
          }}
        />
        <Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
          {formatFullCount(grid.max)}
        </Text>
        <Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
          lượt khách
        </Text>
      </div>
    </div>
  );
}

/** Một lưới đã chuẩn hoá — tách khỏi phần vẽ để dễ đọc và giữ mọi phép gộp ở một chỗ. */
interface HeatmapGrid {
  stations: PassengerDensityStation[];
  hours: number[];
  /** Khoá `${stationId}:${hour}` → tổng lượt khách (dữ liệu trùng được cộng dồn). */
  counts: Map<string, number>;
  max: number;
}

function buildGrid(
  data: readonly PassengerDensityCell[],
  stations: readonly PassengerDensityStation[] | undefined,
  hours: readonly number[],
  maxPassengers: number | undefined,
): HeatmapGrid {
  const normalizedHours = [...new Set(hours)]
    .filter((hour) => Number.isInteger(hour) && hour >= 0 && hour <= 23)
    .sort((a, b) => a - b);

  const counts = new Map<string, number>();
  const discovered = new Map<string, string>();

  for (const cell of data) {
    if (!Number.isInteger(cell.hour) || cell.hour < 0 || cell.hour > 23) continue;

    const key = cellKey(cell.stationId, cell.hour);
    counts.set(key, (counts.get(key) ?? 0) + Math.max(0, cell.passengers));
    if (stations === undefined) {
      discovered.set(cell.stationId, cell.stationName);
    }
  }

  const rows =
    stations === undefined
      ? [...discovered.entries()]
          .map(([id, name]) => ({ id, name }))
          .sort((a, b) => a.name.localeCompare(b.name, 'vi'))
      : dedupeStations(stations);

  const rawMax = counts.size === 0 ? 0 : Math.max(...counts.values());
  // Trần màu luôn ≥ 1 để phép chia an toàn; trần do màn hình cha truyền vào được tôn trọng
  // (dùng khi nhiều biểu đồ cần chung thang màu để so sánh được với nhau).
  const max = Math.max(1, maxPassengers ?? rawMax);

  return { stations: rows, hours: normalizedHours, counts, max };
}

function dedupeStations(
  stations: readonly PassengerDensityStation[],
): PassengerDensityStation[] {
  const seen = new Set<string>();
  const rows: PassengerDensityStation[] = [];

  for (const station of stations) {
    if (seen.has(station.id)) continue;
    seen.add(station.id);
    rows.push({ id: station.id, name: station.name });
  }

  return rows;
}

function cellKey(stationId: string, hour: number): string {
  return `${stationId}:${hour}`;
}

/**
 * Nền ô: trộn `colorPrimary` với nền nhạt theo tỉ lệ giá trị/trần. Có sàn 18% để ô có khách
 * khác hẳn ô 0 — nếu không, khách rất ít sẽ gần như vô hình. `color-mix` được trình duyệt hiện
 * đại hỗ trợ; màu vẫn lấy từ token nên đổi theo chế độ sáng/tối.
 */
function cellBackground(
  value: number,
  ratio: number,
  token: ReturnType<typeof theme.useToken>['token'],
): string {
  if (value <= 0) return token.colorFillQuaternary;

  const percent = Math.round(18 + ratio * 82);
  return `color-mix(in srgb, ${token.colorPrimary} ${percent}%, ${token.colorFillQuaternary})`;
}

function headCellStyle(token: ReturnType<typeof theme.useToken>['token']) {
  return {
    background: token.colorFillAlter,
    color: token.colorTextSecondary,
    fontWeight: 500,
    fontSize: token.fontSizeSM,
    textAlign: 'center' as const,
    padding: `${token.paddingXXS}px 0`,
    borderRadius: token.borderRadiusSM,
  };
}

function stationCellStyle(token: ReturnType<typeof theme.useToken>['token']) {
  return {
    background: token.colorBgContainer,
    color: token.colorText,
    fontWeight: 500,
    fontSize: token.fontSizeSM,
    position: 'sticky' as const,
    left: 0,
    zIndex: 1,
  };
}

/** Nhãn cột: `00`…`23` kèm chữ "h" cho người đọc biết đây là giờ, không phải số thứ tự. */
function formatHour(hour: number): string {
  return `${String(hour).padStart(2, '0')}h`;
}

function formatHourRange(hour: number): string {
  const next = (hour + 1) % 24;
  return `${String(hour).padStart(2, '0')}:00–${String(next).padStart(2, '0')}:00`;
}

/** Số trong ô — ô hẹp nên rút gọn từ 1 000 trở lên, giữ nguyên dưới ngưỡng đó. */
function formatCompactCount(value: number): string {
  if (value < 1000) return String(value);
  return `${(value / 1000).toLocaleString('vi-VN', { maximumFractionDigits: 1 })}K`;
}

function formatFullCount(value: number): string {
  return value.toLocaleString('vi-VN');
}
