import { Menu } from 'antd';
import { useLocation, useNavigate } from 'react-router-dom';
import { buildNavItems, navKeyOf } from '../ui/navItems';

/** Menu ngang trên header — khách (desktop/tablet) và tài xế (mọi cỡ màn). */
export default function TopNav({ role }: { role: string | null }) {
  const navigate = useNavigate();
  const { pathname } = useLocation();

  return (
    <Menu
      mode="horizontal"
      selectedKeys={[navKeyOf(pathname)]}
      items={buildNavItems(role)}
      onClick={({ key }) => navigate(key)}
      style={{ flex: 1, minWidth: 0, borderBottom: 'none', background: 'transparent' }}
    />
  );
}
