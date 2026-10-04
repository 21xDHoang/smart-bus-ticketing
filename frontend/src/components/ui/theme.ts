import type { ThemeConfig } from 'antd';

// Nền tảng thiết kế dùng chung — nguồn duy nhất cho màu sắc và font của cả app.
//
// Quy tắc cho mọi màn: KHÔNG viết mã màu hex trực tiếp trong trang nữa. Màu đã có token
// thì lấy qua `theme.useToken()` (token.colorPrimary, token.colorTextSecondary,
// token.borderRadiusLG, token.boxShadowTertiary…). Chỉ hai thứ antd không có token tương
// đương mới import hằng số từ đây: BRAND.gradient và BRAND.font.

/** Màu thương hiệu — giữ nguyên bộ màu đang dùng, chỉ gom về một chỗ. */
export const BRAND = {
  primary: '#4361ee',
  accent: '#f72585',
  gradient: 'linear-gradient(90deg, #4361ee, #f72585)',
  font: "'Plus Jakarta Sans', system-ui, 'Segoe UI', Roboto, sans-serif",
} as const;

/** Bảng màu phụ (slate) cho nền/viền/chữ khi token antd không diễn tả được đúng sắc độ. */
export const SLATE = {
  50: '#f8fafc',
  100: '#f1f5f9',
  500: '#64748b',
  800: '#1e293b',
} as const;

/**
 * Theme của app — khai báo có annotation `ThemeConfig` (không `as`) để gõ sai tên token
 * là `tsc -b` báo lỗi ngay, không âm thầm bỏ qua.
 */
export const appTheme: ThemeConfig = {
  token: {
    colorPrimary: BRAND.primary,
    colorInfo: BRAND.primary,
    borderRadius: 10,
    // Card/Modal… dùng bán kính lớn — đúng độ bo 16 các màn đang tự viết tay.
    borderRadiusLG: 16,
    fontFamily: BRAND.font,
    colorBgLayout: SLATE[50],
    colorTextHeading: SLATE[800],
    colorTextSecondary: SLATE[500],
  },
  components: {
    Layout: {
      headerBg: '#ffffff',
      headerHeight: 64,
      headerPadding: '0 24px',
      bodyBg: SLATE[50],
      footerBg: 'transparent',
      footerPadding: '12px 24px',
    },
    Menu: {
      itemBorderRadius: 8,
      itemColor: SLATE[500],
      itemSelectedColor: BRAND.primary,
      itemSelectedBg: '#eef1ff',
      itemHoverBg: SLATE[50],
      // Menu ngang: mục đang chọn thành "viên thuốc" nền xanh nhạt như nav cũ, bỏ gạch chân.
      horizontalItemSelectedColor: BRAND.primary,
      horizontalItemSelectedBg: '#eef1ff',
      horizontalItemBorderRadius: 8,
      activeBarBorderWidth: 0,
    },
    Card: {
      headerBg: 'transparent',
      headerFontSize: 16,
      bodyPadding: 24,
      headerPadding: 24,
    },
    Table: {
      headerBg: SLATE[50],
      headerColor: SLATE[500],
      rowHoverBg: SLATE[50],
      borderColor: SLATE[100],
      headerSplitColor: 'transparent',
    },
    Button: {
      fontWeight: 600,
      primaryShadow: 'none',
    },
  },
};
