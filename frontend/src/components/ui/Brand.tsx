import { useId } from 'react';
import { useColorMode } from './colorMode';

// Màu của wordmark theo hai chế độ — giá trị lấy đúng bản nhận diện đã duyệt.
// Dấu "Dải vé S" giữ nguyên ở cả hai chế độ vì nền gradient tự đủ tương phản.
const WORDMARK_COLORS = {
  light: { smart: '#1677FF', ticket: '#174A91' },
  dark: { smart: '#8CC0FF', ticket: '#F1F5F9' },
} as const;

interface BrandMarkProps {
  /** Cạnh của ô vuông, nhận mọi đơn vị CSS — mặc định co theo bề rộng màn hình. */
  size?: number | string;
}

/**
 * Dấu nhận diện "Dải vé S" — tự vẽ trên lưới 64 × 64, không dùng emoji hay icon thư viện.
 *
 * Một nét S liền mạch vừa là chữ S của "Smart", vừa là dải vé gấp khúc; hai hàng lỗ bấm vé
 * được khoét xuyên qua nét bằng mask. Dấu luôn đứng cạnh wordmark nên ẩn với trình đọc màn hình.
 */
export function BrandMark({ size = 'clamp(30px, 4.5vw, 34px)' }: BrandMarkProps) {
  const uid = useId().replace(/:/g, '');
  const gradientId = `brand-tile-${uid}`;
  const maskId = `brand-perforation-${uid}`;

  return (
    <svg
      viewBox="0 0 64 64"
      aria-hidden="true"
      focusable="false"
      style={{ display: 'block', flex: '0 0 auto', width: size, height: size }}
    >
      <defs>
        <linearGradient id={gradientId} gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="64" y2="64">
          <stop offset="0" stopColor="#2E8BFF" />
          <stop offset="1" stopColor="#0B4FC4" />
        </linearGradient>

        <mask id={maskId} maskUnits="userSpaceOnUse" x="0" y="0" width="64" height="64">
          <rect width="64" height="64" fill="#fff" />
          <g stroke="#000" strokeWidth="1.7" strokeLinecap="round" strokeDasharray="0 2.8">
            <line x1="37" y1="13" x2="37" y2="25" />
            <line x1="27" y1="49" x2="27" y2="61" />
          </g>
        </mask>
      </defs>

      <rect width="64" height="64" rx="16" fill={`url(#${gradientId})`} />
      <circle cx="52" cy="13" r="10" fill="#FFFFFF" opacity="0.06" />
      <path
        d="M45 19 H29 A9 9 0 0 0 29 37 H35 A9 9 0 0 1 35 55 H19"
        fill="none"
        stroke="#FFFFFF"
        strokeWidth="6.4"
        strokeLinecap="round"
        strokeLinejoin="round"
        mask={`url(#${maskId})`}
      />
    </svg>
  );
}

interface BrandWordmarkProps {
  /** Cỡ chữ, nhận mọi đơn vị CSS — trang đăng nhập dùng bản lớn hơn header. */
  fontSize?: number | string;
}

/** Wordmark hai tông "Smart" + "Bus Ticket"; chữ đổi sắc theo chế độ sáng/tối. */
export function BrandWordmark({ fontSize = 'clamp(16px, 2.4vw, 18px)' }: BrandWordmarkProps) {
  const { mode } = useColorMode();
  const colors = WORDMARK_COLORS[mode];

  return (
    <span
      style={{
        fontSize,
        fontWeight: 600,
        letterSpacing: '-0.025em',
        lineHeight: 1,
        whiteSpace: 'nowrap',
      }}
    >
      <span style={{ color: colors.smart }}>Smart</span>{' '}
      <span style={{ fontWeight: 800, color: colors.ticket }}>Bus Ticket</span>
    </span>
  );
}
