import type { ReactNode } from 'react';
import type { MenuProps } from 'antd';
import {
  ApartmentOutlined,
  BarChartOutlined,
  BellOutlined,
  CalendarOutlined,
  CarOutlined,
  ClockCircleOutlined,
  CommentOutlined,
  CreditCardOutlined,
  CustomerServiceOutlined,
  DatabaseOutlined,
  DollarOutlined,
  EditOutlined,
  EnvironmentOutlined,
  FieldTimeOutlined,
  FileDoneOutlined,
  FileTextOutlined,
  FundOutlined,
  GiftOutlined,
  HistoryOutlined,
  HomeOutlined,
  LayoutOutlined,
  MessageOutlined,
  PartitionOutlined,
  PercentageOutlined,
  PlusCircleOutlined,
  QrcodeOutlined,
  ScanOutlined,
  ScheduleOutlined,
  SearchOutlined,
  SettingOutlined,
  SwapOutlined,
  TableOutlined,
  TagsOutlined,
  TeamOutlined,
  UserOutlined,
  WarningOutlined,
} from '@ant-design/icons';

// Bảng điều hướng của cả app — NGUỒN DUY NHẤT cho: menu ngang (khách), Sider/Drawer (quản
// trị), thanh tab đáy (khách trên điện thoại), tiêu đề trang (document.title) và breadcrumb.
// Mọi nhãn viết ở đây thì các nơi khác lấy lại, không chép tay để khỏi lệch.
//
// Quy ước nhóm: mỗi vai trò nhiều nhất 5 mục cấp cao nhất, mục nào có nhiều màn con thì gom
// thành nhóm thả xuống. Khách: Trang chủ · Tra cứu tuyến · Vé · Khuyến mãi · Hỗ trợ.
// Quản trị/Quản lý: Quản lý dữ liệu · Điều hành chuyến · Hệ thống. Tài xế: Trang chủ · Tra
// cứu tuyến · Soát vé · Hành khách đã lên xe.

/** Một mục mở thẳng một màn hình. `icon` bắt buộc — rail thu gọn (80px) và thẻ lối vào cần icon. */
export interface NavLeaf {
  to: string;
  label: string;
  roles: string[];
  icon: ReactNode;
}

/** Nhóm thả xuống gồm nhiều mục con. */
export interface NavGroup {
  key: string;
  label: string;
  icon: ReactNode;
  children: NavLeaf[];
}

export type NavEntry = NavLeaf | NavGroup;

/** Một tab của thanh điều hướng đáy trên điện thoại. */
export interface NavTab {
  key: string;
  label: string;
  to: string;
  icon: ReactNode;
  /** Các khóa điều hướng (navKeyOf) làm sáng tab này — thường chỉ [to], thêm khi tab gom nhiều màn. */
  activeFor: string[];
}

/** Một mắt của breadcrumb. Không có `to` = mắt hiện tại (không bấm được). */
export interface NavCrumb {
  label: string;
  to?: string;
}

// Vai trò nào thấy mục nào.
const ADMIN_MANAGER = ['Admin', 'Manager'];
const ADMIN = ['Admin'];
const PASSENGER = ['Passenger'];
const DRIVER = ['Driver'];
/** Mọi vai trò — Trang chủ và Tra cứu tuyến là màn chung, tài xế cũng cần. */
const ALL: string[] = [];

const NAV: NavEntry[] = [
  {
    to: '/',
    label: 'Trang chủ',
    roles: ALL,
    icon: <HomeOutlined />,
  },
  {
    to: '/route-lookup',
    label: 'Tra cứu tuyến',
    roles: ALL,
    icon: <SearchOutlined />,
  },
  {
    to: '/ticket-scan',
    label: 'Soát vé',
    roles: DRIVER,
    icon: <ScanOutlined />,
  },
  {
    to: '/trip-passengers',
    label: 'Hành khách đã lên xe',
    roles: DRIVER,
    icon: <TeamOutlined />,
  },

  // ── Khách: gom 3 màn liên quan tới vé vào một nhóm để cấp cao nhất chỉ còn 5 mục.
  {
    key: 'group-tickets',
    label: 'Vé',
    icon: <QrcodeOutlined />,
    children: [
      {
        to: '/my-tickets',
        label: 'Vé của tôi',
        roles: PASSENGER,
        icon: <QrcodeOutlined />,
      },
      {
        to: '/my-ticket-requests',
        label: 'Hủy/đổi vé của tôi',
        roles: PASSENGER,
        icon: <SwapOutlined />,
      },
      {
        to: '/my-refunds',
        label: 'Hoàn tiền của tôi',
        roles: PASSENGER,
        icon: <DollarOutlined />,
      },
      {
        to: '/my-monthly-passes',
        label: 'Vé tháng của tôi',
        roles: PASSENGER,
        icon: <CreditCardOutlined />,
      },
      {
        to: '/monthly-passes',
        label: 'Đăng ký vé tháng',
        roles: PASSENGER,
        icon: <PlusCircleOutlined />,
      },
    ],
  },
  {
    to: '/vouchers/available',
    label: 'Khuyến mãi',
    roles: PASSENGER,
    icon: <GiftOutlined />,
  },
  {
    key: 'group-support',
    label: 'Hỗ trợ',
    icon: <CustomerServiceOutlined />,
    children: [
      {
        to: '/feedback-submit',
        label: 'Gửi phản ánh',
        roles: PASSENGER,
        icon: <EditOutlined />,
      },
      {
        to: '/my-feedback',
        label: 'Phản ánh của tôi',
        roles: PASSENGER,
        icon: <MessageOutlined />,
      },
    ],
  },

  // ── Quản trị / Quản lý: giữ 3 nhóm như trước, thêm icon cho từng mục con để rail 80px dùng được.
  {
    key: 'group-data',
    label: 'Quản lý dữ liệu',
    icon: <DatabaseOutlined />,
    children: [
      {
        to: '/routes',
        label: 'Tuyến đường',
        roles: ADMIN_MANAGER,
        icon: <PartitionOutlined />,
      },
      {
        to: '/stops',
        label: 'Trạm dừng',
        roles: ADMIN_MANAGER,
        icon: <EnvironmentOutlined />,
      },
      {
        to: '/buses',
        label: 'Đội xe',
        roles: ADMIN_MANAGER,
        icon: <CarOutlined />,
      },
      {
        to: '/route-stops',
        label: 'Gán trạm vào tuyến',
        roles: ADMIN_MANAGER,
        icon: <ApartmentOutlined />,
      },
      {
        to: '/fares',
        label: 'Cấu hình giá vé',
        roles: ADMIN_MANAGER,
        icon: <DollarOutlined />,
      },
      {
        to: '/seat-layouts',
        label: 'Sơ đồ ghế',
        roles: ADMIN_MANAGER,
        icon: <LayoutOutlined />,
      },
      {
        to: '/vouchers',
        label: 'Quản lý voucher',
        roles: ADMIN_MANAGER,
        icon: <TagsOutlined />,
      },
    ],
  },
  {
    key: 'group-trips',
    label: 'Điều hành chuyến',
    icon: <ScheduleOutlined />,
    children: [
      {
        to: '/trips-by-day',
        label: 'Chuyến theo ngày',
        roles: ADMIN_MANAGER,
        icon: <CalendarOutlined />,
      },
      {
        to: '/trip-schedule',
        label: 'Lịch trình',
        roles: ADMIN_MANAGER,
        icon: <ClockCircleOutlined />,
      },
      {
        to: '/trip-frequency',
        label: 'Tần suất chạy xe',
        roles: ADMIN_MANAGER,
        icon: <FieldTimeOutlined />,
      },
      {
        to: '/trip-assignment',
        label: 'Phân công điều xe',
        roles: ADMIN_MANAGER,
        icon: <TeamOutlined />,
      },
    ],
  },
  {
    key: 'group-system',
    label: 'Hệ thống',
    icon: <SettingOutlined />,
    children: [
      {
        to: '/admin-feedbacks',
        label: 'Xử lý phản ánh',
        roles: ADMIN_MANAGER,
        icon: <CommentOutlined />,
      },
      {
        to: '/admin-refunds',
        label: 'Quản lý hoàn tiền',
        roles: ADMIN_MANAGER,
        icon: <DollarOutlined />,
      },
      {
        to: '/admin-ticket-requests',
        label: 'Duyệt huỷ/đổi vé',
        roles: ADMIN_MANAGER,
        icon: <FileDoneOutlined />,
      },
      {
        to: '/notification-templates',
        label: 'Mẫu thông báo',
        roles: ADMIN_MANAGER,
        icon: <BellOutlined />,
      },
      {
        to: '/report-templates',
        label: 'Mẫu báo cáo',
        roles: ADMIN_MANAGER,
        icon: <FileTextOutlined />,
      },
      {
        to: '/admin-users',
        label: 'Người dùng',
        roles: ADMIN,
        icon: <UserOutlined />,
      },
      {
        to: '/audit-logs',
        label: 'Nhật ký hoạt động',
        roles: ADMIN,
        icon: <HistoryOutlined />,
      },
    ],
  },
  {
    key: 'group-reports',
    label: 'Báo cáo',
    icon: <BarChartOutlined />,
    children: [
      {
        to: '/fill-rate',
        label: 'Tỷ lệ lấp đầy theo chuyến',
        roles: ADMIN_MANAGER,
        icon: <PercentageOutlined />,
      },
      {
        to: '/overload-alerts',
        label: 'Cảnh báo chuyến quá tải',
        roles: ADMIN_MANAGER,
        icon: <WarningOutlined />,
      },
      {
        to: '/revenue-dashboard',
        label: 'Dashboard doanh thu',
        roles: ADMIN_MANAGER,
        icon: <FundOutlined />,
      },
      {
        to: '/ticket-validation-stats',
        label: 'Thống kê lượt soát',
        roles: ADMIN_MANAGER,
        icon: <ScanOutlined />,
      },
      {
        to: '/revenue-by-route',
        label: 'Doanh thu theo tuyến',
        roles: ADMIN_MANAGER,
        icon: <TableOutlined />,
      },
    ],
  },
];

/** Màn hình không nằm trong menu (bước giữa của luồng đặt vé, hồ sơ) — nhãn cho tiêu đề trang. */
const SCREEN_TITLES: Record<string, string> = {
  '/trip-results': 'Kết quả tìm chuyến',
  '/seat-map': 'Chọn ghế',
  '/payment': 'Thanh toán',
  '/payment-waiting': 'Chờ kết quả thanh toán',
  '/profile': 'Hồ sơ cá nhân',
};

/** nhãn + icon của một mục menu AntD. */
function toMenuItem(entry: NavEntry): NonNullable<MenuProps['items']>[number] {
  if ('children' in entry) {
    return {
      key: entry.key,
      label: entry.label,
      icon: entry.icon,
      children: entry.children.map((leaf) => ({
        key: leaf.to,
        label: leaf.label,
        icon: leaf.icon,
      })),
    };
  }
  return { key: entry.to, label: entry.label, icon: entry.icon };
}

/** Vai trò này có được thấy mục không. `roles` rỗng = mọi vai trò; chưa đăng nhập = không thấy mục riêng. */
function canSee(leaf: NavLeaf, role: string | null): boolean {
  return leaf.roles.length === 0 || (role !== null && leaf.roles.includes(role));
}

/**
 * Menu của một vai trò, đúng dạng `items` của AntD Menu. Nhóm không còn mục con nào được
 * thấy thì tự ẩn cả nhóm.
 */
export function buildNavItems(role: string | null): MenuProps['items'] {
  return NAV.flatMap((entry) => {
    if ('children' in entry) {
      const children = entry.children.filter((leaf) => canSee(leaf, role));
      return children.length > 0 ? [toMenuItem({ ...entry, children })] : [];
    }
    return canSee(entry, role) ? [toMenuItem(entry)] : [];
  });
}

/** Các mục điều hướng đã lọc theo vai trò — dùng cho thẻ "lối vào" ở trang chủ. */
export function visibleNav(role: string | null): NavEntry[] {
  // Ghi rõ kiểu cho flatMap: hai nhánh trả NavLeaf hoặc NavGroup, không có tham số kiểu
  // TypeScript sẽ suy nhầm thành NavLeaf rồi báo lỗi ở nhánh nhóm.
  return NAV.flatMap<NavEntry>((entry) => {
    if ('children' in entry) {
      const children = entry.children.filter((leaf) => canSee(leaf, role));
      return children.length > 0 ? [{ ...entry, children }] : [];
    }
    return canSee(entry, role) ? [entry] : [];
  });
}

/**
 * Khóa điều hướng của một đường dẫn — màn con (kết quả tìm chuyến, chọn ghế, thanh toán)
 * vẫn làm sáng mục "Tra cứu tuyến" vì chúng là các bước của cùng một luồng.
 */
export function navKeyOf(pathname: string): string {
  if (
    pathname === '/trip-results' ||
    pathname === '/seat-map' ||
    pathname === '/payment' ||
    pathname === '/payment-waiting'
  ) {
    return '/route-lookup';
  }
  if (pathname.startsWith('/my-tickets/')) {
    return '/my-tickets';
  }
  return pathname;
}

/** Tiêu đề trang (dùng cho document.title) — lấy từ menu trước, màn ngoài menu tra bảng riêng. */
export function pageTitleOf(pathname: string): string {
  if (pathname.endsWith('/request')) {
    return 'Huỷ / Đổi vé';
  }
  if (pathname.startsWith('/my-tickets/')) {
    return 'Mã QR vé';
  }
  for (const entry of NAV) {
    if ('children' in entry) {
      const leaf = entry.children.find((child) => child.to === pathname);
      if (leaf) return leaf.label;
    } else if (entry.to === pathname) {
      return entry.label;
    }
  }
  return SCREEN_TITLES[pathname] ?? 'Smart Bus Ticket';
}

/**
 * Breadcrumb của một đường dẫn: màn nào có "cha" (nhóm menu hoặc màn trước trong luồng) thì
 * trả về đường dẫn cha → màn hiện tại; màn cấp cao nhất trả về rỗng (PageHeader sẽ ẩn).
 */
export function navTrail(pathname: string): NavCrumb[] {
  if (
    pathname === '/trip-results' ||
    pathname === '/seat-map' ||
    pathname === '/payment' ||
    pathname === '/payment-waiting'
  ) {
    return [{ label: 'Tra cứu tuyến', to: '/route-lookup' }, { label: pageTitleOf(pathname) }];
  }
  if (pathname.endsWith('/request')) {
    return [{ label: 'Vé của tôi', to: '/my-tickets' }, { label: 'Huỷ / Đổi vé' }];
  }
  if (pathname.startsWith('/my-tickets/')) {
    return [{ label: 'Vé của tôi', to: '/my-tickets' }, { label: 'Mã QR vé' }];
  }
  for (const entry of NAV) {
    if ('children' in entry) {
      const leaf = entry.children.find((child) => child.to === pathname);
      if (leaf) return [{ label: entry.label }, { label: leaf.label }];
    }
  }
  return [];
}

/**
 * Thanh tab đáy trên điện thoại — TUYỂN TAY, không suy diễn từ menu: chỉ những lối vào hay
 * dùng nhất. Nhãn ngắn để vừa 5 tab trên màn 360px. Trả rỗng cho vai trò không dùng tab đáy.
 */
export function bottomNavTabs(role: string | null): NavTab[] {
  if (role !== 'Passenger') return [];
  return [
    {
      key: 'home',
      label: 'Trang chủ',
      to: '/',
      icon: <HomeOutlined />,
      activeFor: ['/'],
    },
    {
      key: 'lookup',
      label: 'Tra cứu',
      to: '/route-lookup',
      icon: <SearchOutlined />,
      activeFor: ['/route-lookup'],
    },
    {
      key: 'tickets',
      label: 'Vé',
      to: '/my-tickets',
      icon: <QrcodeOutlined />,
      activeFor: ['/my-tickets', '/my-ticket-requests', '/my-monthly-passes', '/monthly-passes'],
    },
    {
      key: 'vouchers',
      label: 'Khuyến mãi',
      to: '/vouchers/available',
      icon: <GiftOutlined />,
      activeFor: ['/vouchers/available'],
    },
    {
      key: 'feedback',
      label: 'Phản ánh',
      to: '/my-feedback',
      icon: <MessageOutlined />,
      activeFor: ['/my-feedback', '/feedback-submit'],
    },
  ];
}
