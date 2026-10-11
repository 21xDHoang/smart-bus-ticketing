import { Avatar, Dropdown, message } from 'antd';
import type { MenuProps } from 'antd';
import { LogoutOutlined, QrcodeOutlined, UserOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { BRAND } from '../ui/theme';
import { useAuth } from '../../contexts';

/** Cụm tài khoản trên header: avatar + tên + thực đơn. */
export default function UserMenu() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const laHanhKhach = user?.role === 'Passenger';

  const items: MenuProps['items'] = [
    {
      key: 'profile',
      label: 'Hồ sơ cá nhân',
      icon: <UserOutlined />,
      onClick: () => navigate('/profile'),
    },
    // "Vé của tôi" trỏ đúng màn danh sách vé (/my-tickets), không phải vé tháng.
    ...(laHanhKhach
      ? [
          {
            key: 'my-tickets',
            label: 'Vé của tôi',
            icon: <QrcodeOutlined />,
            onClick: () => navigate('/my-tickets'),
          },
        ]
      : []),
    { type: 'divider' as const },
    {
      key: 'logout',
      label: 'Đăng xuất',
      icon: <LogoutOutlined />,
      danger: true,
      onClick: async () => {
        await logout();
        message.info('Đã đăng xuất tài khoản.');
      },
    },
  ];

  return (
    <Dropdown menu={{ items }} placement="bottomRight" trigger={['click']}>
      <button
        type="button"
        aria-label="Tài khoản"
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 10,
          height: 44,
          padding: '0 6px',
          background: 'none',
          border: 0,
          borderRadius: 10,
          cursor: 'pointer',
          color: 'inherit',
          font: 'inherit',
        }}
      >
        <Avatar style={{ backgroundColor: BRAND.primary }} icon={<UserOutlined />} />
        <span className="app-user-name" style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
          {user?.fullName || 'Người dùng'}
        </span>
      </button>
    </Dropdown>
  );
}
