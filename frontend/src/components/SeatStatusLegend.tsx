import type { CSSProperties } from 'react';
import { Space, Typography, theme } from 'antd';
import { SEAT_STATUS_META, seatAppearance } from './seatStatus';
import type { SeatAppearance } from './seatStatus';

const { Text } = Typography;

interface LegendItem {
  key: string;
  label: string;
  style: CSSProperties;
  /** Mục VIP kèm chấm vàng — giải thích chấm ở góc phải ô ghế VIP. */
  vip?: boolean;
}

/** Ô màu nhỏ trong chú giải — chỉ lấy nền / viền từ bộ màu của ô ghế, không lấy con trỏ. */
function swatchStyle(appearance: SeatAppearance): CSSProperties {
  return {
    position: 'relative',
    display: 'inline-block',
    width: 18,
    height: 16,
    borderRadius: 4,
    background: appearance.background,
    border: appearance.border,
  };
}

/**
 * Chú giải màu cho sơ đồ ghế — đặt ở góc phải thẻ "Sơ đồ ghế".
 *
 * Bốn mục đầu giải thích màu ô ghế; "Đang chọn" là trạng thái thao tác (không phải trạng thái
 * ghế) nên không nằm trong SEAT_STATUS_META. Mục "VIP" giải thích chấm vàng — VIP là LOẠI ghế,
 * không phải trạng thái. Màu lấy từ seatAppearance, cùng nguồn với SeatStatusCell, nên chú
 * giải không bao giờ lệch màu so với ô ghế thật.
 */
export default function SeatStatusLegend() {
  const { token } = theme.useToken();

  const items: LegendItem[] = [
    {
      key: 'available',
      label: SEAT_STATUS_META.Available.label,
      style: swatchStyle(seatAppearance('Available', token)),
    },
    {
      key: 'selected',
      label: 'Đang chọn',
      style: swatchStyle(seatAppearance('Available', token, true)),
    },
    {
      key: 'held',
      label: SEAT_STATUS_META.Held.label,
      style: swatchStyle(seatAppearance('Held', token)),
    },
    {
      key: 'paid',
      label: SEAT_STATUS_META.Paid.label,
      style: swatchStyle(seatAppearance('Paid', token)),
    },
    {
      key: 'vip',
      label: 'VIP',
      style: swatchStyle(seatAppearance('Available', token)),
      vip: true,
    },
  ];

  return (
    <Space size={12} wrap>
      {items.map((item) => (
        <Space key={item.key} size={6}>
          <span style={item.style}>
            {item.vip && (
              <span
                style={{
                  position: 'absolute',
                  top: 2,
                  right: 2,
                  width: 5,
                  height: 5,
                  borderRadius: '50%',
                  background: token.colorWarning,
                }}
              />
            )}
          </span>
          <Text type="secondary" style={{ fontSize: 12 }}>
            {item.label}
          </Text>
        </Space>
      ))}
    </Space>
  );
}
