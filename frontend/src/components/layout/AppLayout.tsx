import { useState } from 'react';
import type { ReactNode } from 'react';
import { Layout } from 'antd';
import { useLocation } from 'react-router-dom';
import { useAuth } from '../../contexts';
import { bottomNavTabs } from '../ui/navItems';
import ErrorBoundary from '../ErrorBoundary';
import AppFooter from './AppFooter';
import AppHeader from './AppHeader';
import BottomNav from './BottomNav';
import PageContainer from './PageContainer';
import RouteEffects from './RouteEffects';
import StaffDrawer from './StaffDrawer';
import StaffSider from './StaffSider';
import TopNav from './TopNav';
import { useViewport } from './useViewport';

const { Content } = Layout;

/**
 * Khung duy nhất của cả app (trừ màn đăng nhập).
 *
 * QUY TẮC SỐNG CÒN: đây là MỘT cây JSX — phần tử có/không theo điều kiện, KHÔNG đổi loại
 * phần tử gốc khi màn hình đổi kích thước và KHÔNG key theo đường dẫn. Đổi gốc = remount
 * toàn bộ trang con = mất ghế đang chọn, đồng hồ giữ chỗ và kết nối SignalR giữa luồng
 * đặt vé. Lưới an toàn duy nhất được key theo đường dẫn là ErrorBoundary bọc <Routes> —
 * đổi màn vốn đã unmount trang cũ, key chỉ giúp thoát trạng thái lỗi khi đi tiếp.
 */
export default function AppLayout({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const { isMobile } = useViewport();
  const { pathname } = useLocation();
  const [drawerOpen, setDrawerOpen] = useState(false);

  const role = user?.role ?? null;
  const isStaff = role === 'Admin' || role === 'Manager';
  const showSider = isStaff && !isMobile;
  const showDrawer = isStaff && isMobile;
  const showBottomNav = isMobile && bottomNavTabs(role).length > 0;
  // Khách trên desktop/tablet và tài xế mọi cỡ màn dùng menu ngang; quản trị dùng Sider/Drawer.
  const showTopNav = !isStaff && !showBottomNav;

  return (
    <Layout style={{ minHeight: '100vh' }}>
      {showSider && <StaffSider role={role} />}

      <Layout>
        <AppHeader showMenuButton={showDrawer} onOpenMenu={() => setDrawerOpen(true)}>
          {showTopNav && <TopNav role={role} />}
        </AppHeader>

        <Content>
          <PageContainer hasBottomNav={showBottomNav}>
            <ErrorBoundary key={pathname}>{children}</ErrorBoundary>
          </PageContainer>
        </Content>

        <AppFooter hidden={showBottomNav} />
      </Layout>

      {showBottomNav && <BottomNav role={role} />}
      {showDrawer && (
        <StaffDrawer role={role} open={drawerOpen} onClose={() => setDrawerOpen(false)} />
      )}
      <RouteEffects />
    </Layout>
  );
}
