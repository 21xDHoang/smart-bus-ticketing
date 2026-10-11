import type { CSSProperties, ReactNode } from 'react';
import { Tag, theme } from 'antd';

// Thẻ trạng thái / thẻ mã dùng chung — thay cho `<Tag color="...">` của AntD.
//
// Vì sao không dùng thẳng màu của AntD: với preset trạng thái (success/processing/…), AntD tô
// CHỮ bằng màu GỐC (colorSuccess #16A34A trên nền #DCFCE7 ≈ 3.0:1; colorWarning #F59E0B trên
// #FEF3C7 chỉ 1.93:1) — dưới ngưỡng AA 4.5:1. Ở đây chữ lấy bản `…Text` (đúng sắc đã chọn để
// đạt tương phản trên nền nhạt), nền lấy `…Bg`, viền lấy `…Border` — cả ba đều là token nên
// sáng/tối tự đổi theo, không hardcode màu nào.
//
// Nhận đúng từ vựng màu đang có trong các bảng meta (ticketStatus.ts, monthlyPassStatus.ts,
// api/feedbackApi.ts…) nên chỗ gọi chỉ cần đổi `<Tag` → `<StatusTag`, giữ nguyên meta.

type Tone = 'success' | 'warning' | 'error' | 'info' | 'primary';

/**
 * Từ vựng màu của AntD → vai trò trong bảng màu của app. Màu không có trong bảng (hoặc không
 * truyền) thì rơi về thẻ xám mặc định — 'default' giữ đúng nghĩa "trung tính" cũ.
 */
const TONE_BY_COLOR: Record<string, Tone> = {
  success: 'success',
  green: 'success',
  warning: 'warning',
  gold: 'warning',
  orange: 'warning',
  error: 'error',
  red: 'error',
  volcano: 'error',
  processing: 'info',
  cyan: 'info',
  blue: 'primary',
  geekblue: 'primary',
  purple: 'primary',
};

interface StatusTagProps {
  /** Màu theo từ vựng AntD — lấy thẳng từ `meta.color` của các bảng meta. */
  color?: string;
  icon?: ReactNode;
  /** Câu giải thích thêm khi rê chuột (tuỳ chọn). */
  title?: string;
  style?: CSSProperties;
  children: ReactNode;
}

/**
 * Thẻ trạng thái hai chế độ sáng/tối, chữ đạt AA trên nền nhạt.
 *
 * @example
 * <StatusTag color="success" icon={<CheckCircleFilled />}>Còn hiệu lực</StatusTag>
 */
export default function StatusTag({ color, icon, title, style, children }: StatusTagProps) {
  const { token } = theme.useToken();
  const tone = color ? TONE_BY_COLOR[color] : undefined;

  // Không nhận ra màu → thẻ xám mặc định của AntD (nền fill mờ + chữ theo màu chữ thường).
  if (!tone) {
    return (
      <Tag icon={icon} title={title} style={style}>
        {children}
      </Tag>
    );
  }

  const face: Record<Tone, { bg: string; border: string; text: string }> = {
    success: {
      bg: token.colorSuccessBg,
      border: token.colorSuccessBorder,
      text: token.colorSuccessText,
    },
    warning: {
      bg: token.colorWarningBg,
      border: token.colorWarningBorder,
      text: token.colorWarningText,
    },
    error: {
      bg: token.colorErrorBg,
      border: token.colorErrorBorder,
      text: token.colorErrorText,
    },
    info: {
      bg: token.colorInfoBg,
      border: token.colorInfoBorder,
      text: token.colorInfoText,
    },
    primary: {
      bg: token.colorPrimaryBg,
      border: token.colorPrimaryBorder,
      text: token.colorPrimaryText,
    },
  };
  const f = face[tone];

  return (
    <Tag
      icon={icon}
      title={title}
      style={{ background: f.bg, borderColor: f.border, color: f.text, ...style }}
    >
      {children}
    </Tag>
  );
}
