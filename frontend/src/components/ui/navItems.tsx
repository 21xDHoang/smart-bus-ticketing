import type { ReactNode } from 'react';
import {
  CreditCardOutlined,
  DatabaseOutlined,
  HomeOutlined,
  MessageOutlined,
  ScheduleOutlined,
  SearchOutlined,
  SettingOutlined,
} from '@ant-design/icons';
import type { MenuProps } from 'antd';

// Cấu hình điều hướng của khung app — trước đây là mảng phẳng 18 mục nằm trong App.tsx,
// Admin phải xuống 2 dòng mới đủ chỗ. Nay gom thành nhóm xổ xuống.
//
// `roles` rỗng = mọi vai trò đã đăng nhập đều thấy. Mục có vai trò thì ẩn với vai trò khác
// (ẩn thay vì để bấm vào rồi nhận trang 403 — giữ nguyên luật cũ). Nhóm tự ẩn khi vai trò
// hiện tại không thấy được mục con nào.

/** Vai trò được thấy mục quản trị. */
const ADMIN_MANAGER = ['Admin', 'Manager'];
const ADMIN = ['Admin'];
/** Mọi vai trò đã đăng nhập. */
const ALL: string[] = [];

/** Một mục lá — bấm là đi thẳng tới `to`. */
interface NavLeaf {
  to: string;
  label: string;
  roles: string[];
}

/** Một nhóm xổ xuống gồm các mục lá. */
interface NavGroup {
  key: string;
  label: string;
  icon: ReactNode;
  children: NavLeaf[];
}

type NavEntry = (NavLeaf & { icon: ReactNode }) | NavGroup;

const NAV: NavEntry[] = [
  { to: '/', label: 'Trang chủ', roles: ALL, icon: <HomeOutlined /> },
  { to: '/route-lookup', label: 'Tra cứu tuyến', roles: ALL, icon: <SearchOutlined /> },
  {
    key: 'group-passes',
    label: 'Vé tháng',
    icon: <CreditCardOutlined />,
    children: [
      { to: '/monthly-passes', label: 'Đăng ký vé tháng', roles: ALL },
      { to: '/my-monthly-passes', label: 'Vé tháng của tôi', roles: ALL },
    ],
  },
  {
    key: 'group-feedback',
    label: 'Phản ánh',
    icon: <MessageOutlined />,
    children: [
      { to: '/feedback-submit', label: 'Gửi phản ánh', roles: ALL },
      { to: '/my-feedback', label: 'Phản ánh của tôi', roles: ALL },
    ],
  },
  {
    key: 'group-data',
    label: 'Quản lý dữ liệu',
    icon: <DatabaseOutlined />,
    children: [
      { to: '/routes', label: 'Tuyến đường', roles: ADMIN_MANAGER },
      { to: '/stops', label: 'Trạm dừng', roles: ADMIN_MANAGER },
      { to: '/buses', label: 'Đội xe', roles: ADMIN_MANAGER },
      { to: '/route-stops', label: 'Gán trạm vào tuyến', roles: ADMIN_MANAGER },
      { to: '/fares', label: 'Cấu hình giá vé', roles: ADMIN_MANAGER },
    ],
  },
  {
    key: 'group-trips',
    label: 'Điều hành chuyến',
    icon: <ScheduleOutlined />,
    children: [
      { to: '/trips-by-day', label: 'Chuyến theo ngày', roles: ADMIN_MANAGER },
      { to: '/trip-schedule', label: 'Lịch trình', roles: ADMIN_MANAGER },
      { to: '/trip-frequency', label: 'Tần suất chạy xe', roles: ADMIN_MANAGER },
      { to: '/trip-assignment', label: 'Phân công điều xe', roles: ADMIN_MANAGER },
    ],
  },
  {
    key: 'group-system',
    label: 'Hệ thống',
    icon: <SettingOutlined />,
    children: [
      { to: '/admin-feedbacks', label: 'Xử lý phản ánh', roles: ADMIN_MANAGER },
      { to: '/admin-users', label: 'Người dùng', roles: ADMIN },
      { to: '/audit-logs', label: 'Nhật ký', roles: ADMIN },
    ],
  },
];

/** Vai trò hiện tại có thấy mục lá này không. */
function canSee(leaf: NavLeaf, role: string | null): boolean {
  return leaf.roles.length === 0 || leaf.roles.includes(role ?? '');
}

/** Dựng items cho Menu theo vai trò — lọc mục lá trước, nhóm rỗng thì ẩn cả nhóm. */
export function buildNavItems(role: string | null): MenuProps['items'] {
  return NAV.flatMap((entry) => {
    if (!('children' in entry)) {
      return canSee(entry, role) ? [{ key: entry.to, icon: entry.icon, label: entry.label }] : [];
    }

    const children = entry.children.filter((leaf) => canSee(leaf, role));
    if (children.length === 0) return [];

    return [
      {
        key: entry.key,
        icon: entry.icon,
        label: entry.label,
        children: children.map((leaf) => ({ key: leaf.to, label: leaf.label })),
      },
    ];
  });
}

/**
 * Key cần tô sáng trên menu theo đường dẫn hiện tại. Thường là chính đường dẫn — antd tự
 * tô cả nhóm cha khi mục con của nó được chọn. Riêng /trip-results là bước tiếp của luồng
 * "Tra cứu tuyến" (đi từ màn đó sang) nên sáng theo mục đó thay vì không sáng gì.
 */
export function navKeyOf(pathname: string): string {
  return pathname === '/trip-results' ? '/route-lookup' : pathname;
}
