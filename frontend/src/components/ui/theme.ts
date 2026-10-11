import { theme } from 'antd';
import type { ThemeConfig } from 'antd';

// Nền tảng thiết kế dùng chung — NGUỒN DUY NHẤT cho màu sắc và font của cả app,
// cho cả hai chế độ sáng/tối.
//
// Bảng màu mới lấy từ ngôn ngữ của biển bến và sơ đồ tuyến: xanh tuyến (#0B5FFF) cho mọi
// hành động, teal (#00B8A9) cho "của bạn" (ghế đang chọn, tuyến 02), xanh lá (#16A34A) là
// trạng thái tốt, cam (#F59E0B) là cảnh báo/sắp hết, đỏ (#DC2626) là lỗi/huỷ. Nền sáng
// #F6F8FB, nền tối #0F172A. Số liệu (giờ, tiền, mã ghế) dùng chữ số đều — xem index.css.
//
// QUY TẮC cho mọi màn: KHÔNG viết mã màu hex trực tiếp trong trang. Màu đã có token thì lấy
// qua `theme.useToken()`; khi cần cả hai chế độ (CSS thô, hàm thuần) thì lấy từ `PALETTE`,
// hoặc biến CSS `--app-*` trong index.css. Hai chỗ cố ý giữ nền trắng tuyệt đối: mã QR vé
// và màn quét vé — đầu đọc cần nền trắng, cả ở chế độ tối.
//
// TƯƠNG PHẢN (WCAG, đã tính bằng công thức): chữ trắng KHÔNG đủ tương phản trên teal
// (2.49:1), success (3.30:1), warning (2.15:1) — ba màu này khi mang chữ phải dùng chữ tối
// `onBase` = #0F172A (7.17 / 5.42 / 8.31 ✓). Primary và error mang chữ trắng (5.13 / 4.83 ✓).
// Chữ mờ (muted) chỉ đặt trên thẻ trắng: #64748B trên nền #F6F8FB chỉ 4.47:1, cần thì dùng
// `textMutedOnLayout` (#475569). Chế độ tối: màu chức năng làm CHỮ phải dùng sắc sáng
// (`asText`) — ví dụ #DC2626 làm chữ trên nền #0F172A chỉ 3.70:1, phải là #F87171.
//
// Đổi màu ở đây là mọi màn đổi theo. Vite HMR có thể giữ token cũ sau khi sửa file này —
// thấy sai màu thì refresh cứng (Ctrl+Shift+R) hoặc khởi động lại dev server.

/** Hai chế độ hiển thị. Người dùng chọn ở ThemeModeToggle; mặc định theo hệ điều hành. */
export type ColorMode = 'light' | 'dark';

/**
 * Một màu chức năng với đủ sắc độ dùng được ở mọi trạng thái.
 * `onBase`/`onSoft` là màu CHỮ đặt trên nền tương ứng — đã kiểm tương phản, không tự đoán.
 */
export interface RoleScale {
  /** Nền nút/badge/ghế — màu gốc của vai trò. */
  base: string;
  /** Nền khi rê chuột — luôn đậm hơn `base` để chữ giữ nguyên màu vẫn đạt tương phản. */
  hover: string;
  /** Nền khi đang bấm. */
  active: string;
  /** Nền nhạt cho badge/alert/hàng đang chọn. */
  soft: string;
  /** Viền trên nền `soft`. */
  softBorder: string;
  /** Màu chữ đặt trên `base`. */
  onBase: string;
  /** Màu chữ đặt trên `soft`. */
  onSoft: string;
  /** Vai trò này dùng làm CHỮ trên nền trang/thẻ (chế độ tối dùng sắc sáng hơn). */
  asText: string;
  /** Sắc của `asText` khi rê chuột — chế độ tối dùng sắc NHẠT hơn vì nền tối. */
  asTextHover: string;
}

/** Toàn bộ token màu của một chế độ. */
export interface ModeTokens {
  primary: RoleScale;
  secondary: RoleScale;
  success: RoleScale;
  warning: RoleScale;
  error: RoleScale;
  neutral: {
    /** Nền trang. */
    bg: string;
    /** Nền thẻ — ở chế độ tối nhạt hơn nền trang một bậc. */
    surface: string;
    /** Nền nổi (dropdown, header bảng, ghế trống) — nhạt hơn `surface` một bậc nữa. */
    elevated: string;
    border: string;
    borderSecondary: string;
    text: string;
    textMuted: string;
    /** Chữ mờ khi nằm TRỰC TIẾP trên nền trang (không phải trên thẻ). */
    textMutedOnLayout: string;
    /** Mực đậm nhất — viền đôi ghế VIP. */
    ink: string;
    /** Nền ghế "đã bán" — xám đặc, chữ trắng đạt 4.76:1. */
    seatSold: string;
  };
}

/**
 * Bảng màu hai chế độ. Màu nền (`base`) giữ NGUYÊN ở cả hai chế độ — cùng một màu chức năng
 * mang cùng một màu chữ ở mọi nơi, ô ghế và badge không phải rẽ nhánh theo chế độ. Chỉ nền
 * nhạt (`soft`), màu chữ trên nền nhạt (`onSoft`) và màu chức năng khi làm chữ (`asText`,
 * `asTextHover`) đổi theo chế độ.
 */
export const PALETTE: Record<ColorMode, ModeTokens> = {
  light: {
    primary: {
      base: '#0B5FFF',
      hover: '#0A50D9',
      active: '#063FB0',
      soft: '#E8F0FF',
      softBorder: '#C2D8FF',
      onBase: '#FFFFFF',
      onSoft: '#063FB0',
      asText: '#0B5FFF',
      asTextHover: '#0A50D9',
    },
    secondary: {
      base: '#00B8A9',
      hover: '#00A697',
      active: '#009488',
      soft: '#E0F7F5',
      softBorder: '#A8E6E1',
      onBase: '#0F172A',
      onSoft: '#007A6F',
      asText: '#007A6F',
      asTextHover: '#00695F',
    },
    success: {
      base: '#16A34A',
      hover: '#1DB04F',
      active: '#17994A',
      soft: '#DCFCE7',
      softBorder: '#BBF7D0',
      onBase: '#0F172A',
      onSoft: '#15803D',
      asText: '#15803D',
      asTextHover: '#166534',
    },
    warning: {
      base: '#F59E0B',
      hover: '#EA8C00',
      active: '#D98200',
      soft: '#FEF3C7',
      softBorder: '#FDE68A',
      onBase: '#0F172A',
      onSoft: '#92400E',
      asText: '#B45309',
      asTextHover: '#92400E',
    },
    error: {
      base: '#DC2626',
      hover: '#B91C1C',
      active: '#991B1B',
      soft: '#FEE2E2',
      softBorder: '#FECACA',
      onBase: '#FFFFFF',
      onSoft: '#B91C1C',
      asText: '#DC2626',
      asTextHover: '#991B1B',
    },
    neutral: {
      bg: '#F6F8FB',
      surface: '#FFFFFF',
      elevated: '#FFFFFF',
      border: '#E2E8F0',
      borderSecondary: '#EEF2F7',
      text: '#0F172A',
      textMuted: '#64748B',
      textMutedOnLayout: '#475569',
      ink: '#0F172A',
      seatSold: '#64748B',
    },
  },
  dark: {
    primary: {
      base: '#0B5FFF',
      hover: '#0A50D9',
      active: '#063FB0',
      soft: '#1E3A66',
      softBorder: '#2C4E8E',
      onBase: '#FFFFFF',
      onSoft: '#7FB0FF',
      asText: '#7FB0FF',
      asTextHover: '#A8C7FF',
    },
    secondary: {
      base: '#00B8A9',
      hover: '#00A697',
      active: '#009488',
      soft: '#19434F',
      softBorder: '#1F5A63',
      onBase: '#0F172A',
      onSoft: '#2DD4BF',
      asText: '#2DD4BF',
      asTextHover: '#5EEAD4',
    },
    success: {
      base: '#16A34A',
      hover: '#1DB04F',
      active: '#17994A',
      soft: '#1D3F3E',
      softBorder: '#22554A',
      onBase: '#0F172A',
      onSoft: '#4ADE80',
      asText: '#4ADE80',
      asTextHover: '#86EFAC',
    },
    warning: {
      base: '#F59E0B',
      hover: '#EA8C00',
      active: '#D98200',
      soft: '#453E32',
      softBorder: '#5C4A26',
      onBase: '#0F172A',
      onSoft: '#FBBF24',
      asText: '#FBBF24',
      asTextHover: '#FCD34D',
    },
    error: {
      base: '#DC2626',
      hover: '#B91C1C',
      active: '#991B1B',
      soft: '#402837',
      softBorder: '#5A2B33',
      onBase: '#FFFFFF',
      onSoft: '#F87171',
      asText: '#F87171',
      asTextHover: '#FCA5A5',
    },
    neutral: {
      bg: '#0F172A',
      surface: '#1E293B',
      elevated: '#243244',
      border: '#334155',
      borderSecondary: '#26334A',
      text: '#E2E8F0',
      textMuted: '#94A3B8',
      textMutedOnLayout: '#94A3B8',
      ink: '#E2E8F0',
      seatSold: '#64748B',
    },
  },
};

/** Lấy bảng token của một chế độ — dùng trong hàm thuần (seatAppearance…) và CSS nội tuyến. */
export function palette(mode: ColorMode): ModeTokens {
  return PALETTE[mode];
}

// ── Bí danh cũ — giữ TÊN export để App.tsx không phải sửa; giá trị chế độ sáng.
//    Màn nào cần theo chế độ thì dùng PALETTE/palette(mode) hoặc token của antd.

/** Màu thương hiệu còn nơi tiêu thụ (giá trị chế độ sáng — chỗ cần hai chế độ thì dùng PALETTE). */
export const BRAND = {
  /** Xanh tuyến — màu chính cho mọi hành động: nút, liên kết, mục menu đang chọn. */
  primary: '#0B5FFF',
  font: "'Be Vietnam Pro', 'Inter', system-ui, 'Segoe UI', Roboto, sans-serif",
} as const;

/**
 * Override component của antd theo từng chế độ — mọi giá trị lấy từ PALETTE, không hex tay.
 */
function buildComponents(mode: ColorMode, p: ModeTokens): ThemeConfig['components'] {
  const dark = mode === 'dark';

  return {
    Layout: {
      headerBg: p.neutral.surface,
      headerHeight: 64,
      headerPadding: '0 24px',
      bodyBg: p.neutral.bg,
      footerBg: 'transparent',
      footerPadding: '12px 24px',
    },
    Menu: {
      // Mục menu cao 44px — đủ lớn để bấm trên điện thoại, vẫn gọn trên desktop.
      itemHeight: 44,
      itemBorderRadius: 10,
      itemMarginInline: 8,
      itemColor: p.neutral.textMuted,
      itemSelectedColor: p.primary.onSoft,
      itemSelectedBg: p.primary.soft,
      itemHoverBg: dark ? p.neutral.elevated : p.neutral.borderSecondary,
      // Menu dọc (Sider/Drawer) lẫn ngang (header): mục đang chọn là "viên thuốc" nền xanh
      // nhạt, không gạch chân.
      horizontalItemSelectedColor: p.primary.onSoft,
      horizontalItemSelectedBg: p.primary.soft,
      horizontalItemBorderRadius: 10,
      activeBarBorderWidth: 0,
    },
    Card: {
      headerBg: 'transparent',
      headerFontSize: 16,
      bodyPadding: 24,
      headerPadding: 24,
    },
    Table: {
      headerBg: dark ? p.neutral.elevated : p.neutral.bg,
      headerColor: p.neutral.textMuted,
      rowHoverBg: dark ? p.neutral.elevated : p.neutral.borderSecondary,
      borderColor: p.neutral.borderSecondary,
      headerSplitColor: 'transparent',
    },
    Button: {
      fontWeight: 600,
      primaryShadow: 'none',
    },
    Segmented: {
      trackBg: dark ? p.neutral.bg : p.neutral.borderSecondary,
      itemSelectedBg: dark ? p.neutral.elevated : p.neutral.surface,
    },
  };
}

/** Cache theo chế độ — ThemeConfig giữ nguyên tham chiếu giữa các lần render (antd khỏi tính lại). */
const cache = new Map<ColorMode, ThemeConfig>();

/**
 * Theme của app theo chế độ sáng/tối. Khai báo có annotation để gõ sai tên token là
 * `tsc -b` báo lỗi ngay, không âm thầm bỏ qua.
 *
 * Ngoài màu gốc, ở đây còn ghi đè thẳng các token phái sinh (colorPrimaryHover,
 * colorPrimaryBg…) — antd tự sinh được, nhưng bản tự sinh có sắc hover SÁNG hơn nền gốc,
 * chữ trắng trên đó không đạt tương phản; giá trị trong PALETTE đã tính đủ.
 */
export function buildTheme(mode: ColorMode): ThemeConfig {
  const cached = cache.get(mode);
  if (cached) return cached;

  const p = PALETTE[mode];
  const dark = mode === 'dark';

  const config: ThemeConfig = {
    algorithm: dark ? theme.darkAlgorithm : theme.defaultAlgorithm,
    token: {
      colorPrimary: p.primary.base,
      colorInfo: p.secondary.base,
      colorSuccess: p.success.base,
      colorWarning: p.warning.base,
      colorError: p.error.base,

      // Chế độ tối: mọi sắc xám phái sinh (nền nổi, viền, chữ mờ…) suy từ nền navy này
      // thay vì đen mặc định của antd.
      ...(dark ? { colorBgBase: p.neutral.bg } : {}),

      // Bo góc theo thang mới: thẻ/nút 12, khối lớn (modal, panel) 16, control nhỏ 8.
      borderRadius: 12,
      borderRadiusLG: 16,
      borderRadiusSM: 8,
      // Nhịp chuyển động 150–250ms — đủ thấy mạch thay đổi, không đủ để phải chờ.
      motionDurationFast: '0.15s',
      motionDurationMid: '0.2s',
      motionDurationSlow: '0.25s',
      fontFamily: BRAND.font,

      colorText: p.neutral.text,
      colorTextHeading: p.neutral.text,
      colorTextSecondary: p.neutral.textMuted,
      colorBgLayout: p.neutral.bg,
      colorBgContainer: p.neutral.surface,
      colorBgElevated: p.neutral.elevated,
      colorBorder: p.neutral.border,
      colorBorderSecondary: p.neutral.borderSecondary,

      // Liên kết: sắc sáng ở chế độ tối, sắc đậm khi hover ở chế độ sáng.
      colorLink: p.primary.asText,
      colorLinkHover: p.primary.asTextHover,
      // Đang bấm: chế độ tối giữ sắc sáng — sắc đậm chìm vào nền navy.
      colorLinkActive: dark ? p.primary.asTextHover : p.primary.active,

      // Sắc độ đầy đủ của 5 vai trò — nút, badge, alert, hàng đang chọn đều lấy từ đây.
      colorPrimaryHover: p.primary.hover,
      colorPrimaryActive: p.primary.active,
      colorPrimaryBg: p.primary.soft,
      colorPrimaryBorder: p.primary.softBorder,
      colorPrimaryText: p.primary.onSoft,
      colorPrimaryTextHover: p.primary.asTextHover,
      colorPrimaryTextActive: dark ? p.primary.asText : p.primary.active,

      colorSuccessHover: p.success.hover,
      colorSuccessActive: p.success.active,
      colorSuccessBg: p.success.soft,
      colorSuccessBorder: p.success.softBorder,
      colorSuccessText: p.success.onSoft,
      colorSuccessTextHover: p.success.asTextHover,

      colorWarningHover: p.warning.hover,
      colorWarningActive: p.warning.active,
      colorWarningBg: p.warning.soft,
      colorWarningBorder: p.warning.softBorder,
      colorWarningText: p.warning.onSoft,
      colorWarningTextHover: p.warning.asTextHover,

      colorErrorHover: p.error.hover,
      colorErrorActive: p.error.active,
      colorErrorBg: p.error.soft,
      colorErrorBorder: p.error.softBorder,
      colorErrorText: p.error.onSoft,
      colorErrorTextHover: p.error.asTextHover,

      colorInfoHover: p.secondary.hover,
      colorInfoActive: p.secondary.active,
      colorInfoBg: p.secondary.soft,
      colorInfoBorder: p.secondary.softBorder,
      colorInfoText: p.secondary.onSoft,
      colorInfoTextHover: p.secondary.asTextHover,

      // Đổ bóng nhẹ kiểu giấy — chế độ tối bỏ hẳn, thẻ phân tầng bằng độ sáng nền.
      boxShadowTertiary: dark ? 'none' : '0 4px 12px rgba(15, 23, 42, 0.04)',
    },
    components: buildComponents(mode, p),
  };

  cache.set(mode, config);
  return config;
}
