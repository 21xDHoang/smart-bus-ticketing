import { Drawer, Menu } from 'antd';
import { useLocation, useNavigate } from 'react-router-dom';
import { buildNavItems, navKeyOf } from '../ui/navItems';

interface StaffDrawerProps {
  role: string | null;
  open: boolean;
  onClose: () => void;
}

/** Ngăn kéo menu cho Admin/Manager trên điện thoại — cùng cây menu với Sider. */
export default function StaffDrawer({ role, open, onClose }: StaffDrawerProps) {
  const { pathname } = useLocation();
  const navigate = useNavigate();

  return (
    <Drawer
      title="Menu"
      placement="left"
      width={288}
      open={open}
      onClose={onClose}
      styles={{ body: { padding: 8 } }}
    >
      <Menu
        mode="inline"
        selectedKeys={[navKeyOf(pathname)]}
        items={buildNavItems(role)}
        onClick={({ key }) => {
          onClose();
          navigate(key);
        }}
        style={{ borderInlineEnd: 'none' }}
      />
    </Drawer>
  );
}
