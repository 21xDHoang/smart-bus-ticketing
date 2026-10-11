import type { CSSProperties } from 'react';
import { theme } from 'antd';

// Ô vuông mã tuyến — như miếng màu gắn trên biển bến: cùng một mã tuyến thì cùng một
// màu ở mọi màn (kết quả tìm chuyến, chọn ghế, vé). Màu suy từ chính mã tuyến bằng một
// phép băm nhỏ, cố định — không cần bảng tra, thêm tuyến mới là tự có màu.
//
// Thứ tự bảng màu đặt sao cho các tuyến đang chạy (01, 02, 03, HG01) rơi vào bốn màu
// khác nhau; vẫn là băm nên đừng dựa vào thứ tự này khi thêm tuyến khác.
//
// Bốn sắc đậm, mỗi sắc đạt ≥4.5:1 với chữ trắng (ô luôn mang chữ trắng, nguyên ở cả hai
// chế độ); tuyến 02 rơi đúng teal #007A6F của bảng màu.
const LINE_COLORS = ['#0B5FFF', '#007A6F', '#6C4FA1', '#A33B4A'] as const;

function colorOfCode(code: string): string {
  let sum = 0;
  for (let i = 0; i < code.length; i += 1) {
    sum += code.charCodeAt(i);
  }
  return LINE_COLORS[(sum - 1) % LINE_COLORS.length];
}

/**
 * Ô vuông màu theo mã tuyến.
 *
 * @param code mã tuyến, ví dụ "01" hoặc "HG01"
 * @param size `sm` cho ngữ cảnh chật (dòng phụ, bảng), mặc định `md`
 * @param style ghi đè kiểu dáng tại chỗ (ví dụ khoảng cách với chữ bên cạnh)
 */
export default function RouteBadge({
  code,
  size = 'md',
  style,
}: {
  code: string;
  size?: 'sm' | 'md';
  style?: CSSProperties;
}) {
  const { token } = theme.useToken();
  const height = size === 'sm' ? 22 : 26;
  const fontSize = size === 'sm' ? 12 : 13;

  return (
    <span
      title={`Tuyến ${code}`}
      aria-label={`Tuyến ${code}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        // Dùng được cả trong dòng chữ (dòng phụ của đầu trang) lẫn trong Space.
        verticalAlign: 'middle',
        minWidth: height,
        height,
        padding: '0 6px',
        borderRadius: token.borderRadiusSM,
        background: colorOfCode(code),
        color: token.colorTextLightSolid,
        fontWeight: 700,
        fontSize,
        lineHeight: 1,
        fontVariantNumeric: 'tabular-nums',
        letterSpacing: 0.3,
        ...style,
      }}
    >
      {code}
    </span>
  );
}
