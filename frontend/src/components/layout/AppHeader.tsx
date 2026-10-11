import type { ReactNode } from 'react';
import { Button, Layout } from 'antd';
import { MenuOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { ThemeModeToggle } from '../ui';
import UserMenu from './UserMenu';

const { Header } = Layout;

interface AppHeaderProps {
  /** Menu ngang đặt giữa logo và cụm tài khoản — khách trên desktop/tablet, tài xế mọi cỡ màn. */
  children?: ReactNode;
  /** Nút mở ngăn kéo menu — chỉ quản trị/quản lý trên điện thoại. */
  showMenuButton?: boolean;
  onOpenMenu?: () => void;
}

/**
 * Thanh trên cùng, dính theo cuộn trang. zIndex 100 — luôn dưới lớp popup của antd (1000+)
 * và dưới màn quét vé toàn màn hình của TicketQrPage (2000).
 */
export default function AppHeader({
  children,
  showMenuButton = false,
  onOpenMenu,
}: AppHeaderProps) {
  return (
    <Header
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        height: 'var(--app-header-h)',
        lineHeight: 'normal',
        paddingInline: 16,
        position: 'sticky',
        top: 0,
        zIndex: 100,
        borderBottom: '1px solid var(--app-border)',
      }}
    >
      {showMenuButton && (
        <Button
          type="text"
          aria-label="Mở menu"
          icon={<MenuOutlined />}
          onClick={onOpenMenu}
          style={{ width: 44, height: 44 }}
        />
      )}

      <Link
        to="/"
        style={{ display: 'flex', alignItems: 'center', gap: 10, textDecoration: 'none' }}
      >
        <span aria-hidden="true" style={{ fontSize: 24 }}>
          🚌
        </span>
        <span
          style={{
            fontSize: 18,
            fontWeight: 700,
            background: 'var(--app-brand-gradient)',
            WebkitBackgroundClip: 'text',
            backgroundClip: 'text',
            WebkitTextFillColor: 'transparent',
            whiteSpace: 'nowrap',
          }}
        >
          Smart Bus Ticket
        </span>
      </Link>

      {children}

      <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 4 }}>
        <ThemeModeToggle />
        <UserMenu />
      </div>
    </Header>
  );
}
