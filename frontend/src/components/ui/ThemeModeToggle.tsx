import { Button, Tooltip } from 'antd';
import { MoonOutlined, SunOutlined } from '@ant-design/icons';
import { useColorMode } from './colorMode';

/**
 * Nút đổi chế độ sáng/tối — đặt trên header cạnh avatar.
 *
 * Biểu tượng đổi theo chế độ đang bật (mặt trời khi đang tối, mặt trăng khi đang sáng) nên
 * trạng thái đọc được cả khi không phân biệt màu; nhãn `aria-label` và tooltip cùng mô tả
 * hành động sắp xảy ra. Vùng bấm 44×44 px theo chuẩn chạm.
 */
export default function ThemeModeToggle() {
  const { mode, toggle } = useColorMode();
  const dark = mode === 'dark';
  const label = dark ? 'Chuyển sang chế độ sáng' : 'Chuyển sang chế độ tối';

  return (
    <Tooltip title={label}>
      <Button
        type="text"
        aria-label={label}
        onClick={toggle}
        icon={dark ? <SunOutlined /> : <MoonOutlined />}
        style={{ width: 44, height: 44 }}
      />
    </Tooltip>
  );
}
