import { useState } from 'react';
import { Layout, Menu } from 'antd';
import { useLocation, useNavigate } from 'react-router-dom';
import { buildNavItems, navKeyOf, visibleNav } from '../ui/navItems';

const { Sider } = Layout;

/** Nhóm menu chứa đường dẫn đang mở — để mở sẵn nhóm đó ngay khi vào. */
function groupOf(role: string | null, key: string): string | undefined {
  for (const entry of visibleNav(role)) {
    if ('children' in entry && entry.children.some((child) => child.to === key)) {
      return entry.key;
    }
  }
  return undefined;
}

/** Cột menu cho Admin/Manager trên màn rộng: thu gọn được, tự thu về rail 80px dưới 992px. */
export default function StaffSider({ role }: { role: string | null }) {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(false);
  const selected = navKeyOf(pathname);
  const initialGroup = groupOf(role, selected);

  return (
    <Sider
      collapsible
      collapsed={collapsed}
      onCollapse={setCollapsed}
      breakpoint="lg"
      collapsedWidth={80}
      width={248}
      theme="light"
      style={{
        position: 'sticky',
        // Header phủ hết chiều ngang và nằm trên hàng chứa Sider, nên cột bám ngay dưới Header.
        top: 'var(--app-header-h)',
        height: 'calc(100vh - var(--app-header-h))',
        overflowY: 'auto',
        overflowX: 'hidden',
        borderRight: '1px solid var(--app-border)',
        zIndex: 90,
      }}
    >
      <Menu
        mode="inline"
        selectedKeys={[selected]}
        defaultOpenKeys={initialGroup ? [initialGroup] : []}
        items={buildNavItems(role)}
        onClick={({ key }) => navigate(key)}
        style={{ borderInlineEnd: 'none', paddingBlock: 8 }}
      />
    </Sider>
  );
}
