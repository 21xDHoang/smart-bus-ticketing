import { Radio, Typography, theme } from 'antd';
import type { RadioChangeEvent } from 'antd';
import type { TicketChangeNewTripOption } from '../api/ticketChangeRequestApi';
import { priceDiffOf, timeDiffLabel } from './newTripCompare';
import { formatDate, formatTime, formatVnd } from './ui/format';
import { RouteBadge } from './ui';

const { Text } = Typography;

/** Sắc thái chip chênh lệch dùng để tô màu — 'same' của so sánh giá quy về 'neutral'. */
type DiffChipTone = 'more' | 'less' | 'neutral';

/** Chip nhỏ hiển thị chênh lệch giờ/giá, màu lấy từ token để đổi bảng màu là đổi theo. */
function DiffChip({ label, tone }: { label: string; tone: DiffChipTone }) {
  const { token } = theme.useToken();

  const palette =
    tone === 'more'
      ? { bg: token.colorWarningBg, text: token.colorWarningText, border: token.colorWarningBorder }
      : tone === 'less'
        ? { bg: token.colorSuccessBg, text: token.colorSuccessText, border: token.colorSuccessBorder }
        : { bg: token.colorFillTertiary, text: token.colorTextSecondary, border: token.colorBorderSecondary };

  return (
    <span
      className="tabular-nums"
      style={{
        display: 'inline-flex',
        padding: '2px 8px',
        borderRadius: token.borderRadiusSM,
        border: `1px solid ${palette.border}`,
        background: palette.bg,
        color: palette.text,
        fontSize: 12,
        fontWeight: 600,
        whiteSpace: 'nowrap',
      }}
    >
      {label}
    </span>
  );
}

interface NewTripSelectorProps {
  /** Chuyến đang chọn — antd Form truyền qua chính prop này (`name="newTripId"`). */
  value?: string;
  onChange?: (id: string) => void;

  /** Giờ khởi hành của chuyến hiện tại (vé cũ) — mốc so sánh GIỜ. */
  currentDepartureTime: string;
  /** Giá vé chuyến hiện tại (VND) — mốc so sánh GIÁ. */
  currentPrice: number;

  options: TicketChangeNewTripOption[];
  disabled?: boolean;
}

// Component "chọn chuyến mới khi đổi vé + so sánh giờ và giá" (story 5 "Hủy & Đổi vé",
// Sprint 4, task của Nguyễn Đình Băng): liệt kê các chuyến thay thế, mỗi chuyến so GIỜ
// (khởi hành sớm/muộn hơn bao lâu) và GIÁ (trả thêm / hoàn lại / ngang giá) với chuyến hiện
// tại. Đây là chỗ thay thế cho ô `<Select>` tạm của màn "Gửi yêu cầu huỷ / đổi vé" (Hạnh) —
// hợp đồng submit không đổi: Form vẫn gửi `newTripId` + `newSeatId` (seatId nằm trong option).
export default function NewTripSelector({
  value,
  onChange,
  currentDepartureTime,
  currentPrice,
  options,
  disabled = false,
}: NewTripSelectorProps) {
  const { token } = theme.useToken();

  const handleChange = (e: RadioChangeEvent) => {
    onChange?.(e.target.value as string);
  };

  if (options.length === 0) {
    return <Text type="secondary">Không có chuyến thay thế để chọn.</Text>;
  }

  return (
    <div>
      {/* Mốc so sánh — chuyến hiện tại của vé. Các thẻ bên dưới so GIỜ & GIÁ với dòng này. */}
      <Text type="secondary" style={{ fontSize: 12 }}>
        So với chuyến hiện tại: khởi hành{' '}
        <Text strong className="tabular-nums" style={{ fontSize: 12 }}>
          {formatTime(currentDepartureTime)}
        </Text>{' '}
        {formatDate(currentDepartureTime)} · giá{' '}
        <Text strong className="tabular-nums" style={{ fontSize: 12 }}>
          {formatVnd(currentPrice)}
        </Text>
      </Text>

      <Radio.Group
        value={value}
        onChange={handleChange}
        disabled={disabled}
        style={{ display: 'flex', flexDirection: 'column', gap: 10, width: '100%', marginTop: 10 }}
      >
        {options.map((trip) => {
          const selected = trip.id === value;
          const price = priceDiffOf(trip.price, currentPrice);
          const time = timeDiffLabel(trip.departureTime, currentDepartureTime);

          return (
            <Radio
              key={trip.id}
              value={trip.id}
              style={{
                display: 'flex',
                alignItems: 'center',
                width: '100%',
                marginInlineStart: 0,
                marginInlineEnd: 0,
                padding: '14px 16px',
                border: `1px solid ${selected ? token.colorPrimary : token.colorBorderSecondary}`,
                borderRadius: token.borderRadiusLG,
                background: selected ? token.colorPrimaryBg : token.colorBgContainer,
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap', width: '100%' }}>
                {/* Tuyến + ghế muốn đổi sang. */}
                <div style={{ flex: 1, minWidth: 180 }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                    <RouteBadge code={trip.routeCode} size="sm" />
                    <Text strong>{trip.routeName}</Text>
                  </div>
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    Ghế {trip.seatNumber}
                  </Text>
                </div>

                {/* Giờ khởi hành + chênh giờ so với chuyến hiện tại. */}
                <div style={{ minWidth: 120 }}>
                  <div className="tabular-nums" style={{ fontSize: 16, fontWeight: 700, color: token.colorTextHeading }}>
                    {formatTime(trip.departureTime)}
                  </div>
                  <Text type="secondary" className="tabular-nums" style={{ fontSize: 12 }}>
                    {formatDate(trip.departureTime)}
                  </Text>
                  <div style={{ marginTop: 4 }}>
                    <DiffChip label={time} tone="neutral" />
                  </div>
                </div>

                {/* Giá + chênh giá so với chuyến hiện tại. */}
                <div style={{ textAlign: 'right', minWidth: 120 }}>
                  <div className="tabular-nums" style={{ fontSize: 16, fontWeight: 700, color: token.colorTextHeading }}>
                    {formatVnd(trip.price)}
                  </div>
                  <div style={{ marginTop: 4 }}>
                    <DiffChip
                      label={price.label}
                      tone={price.tone === 'more' ? 'more' : price.tone === 'less' ? 'less' : 'neutral'}
                    />
                  </div>
                </div>
              </div>
            </Radio>
          );
        })}
      </Radio.Group>
    </div>
  );
}
