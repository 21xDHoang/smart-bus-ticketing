import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Collapse, Space, Typography } from 'antd';
import { BellOutlined } from '@ant-design/icons';
import {
  dismissPrompt,
  getNotificationPermission,
  isPromptDismissed,
  requestNotificationPermission,
  watchNotificationPermission,
} from './notificationPermission';
import type { NotificationPermissionState } from './notificationPermission';

const { Paragraph, Text } = Typography;

interface NotificationPermissionPromptProps {
  /**
   * Gọi khi trạng thái quyền đổi — do người dùng bấm "Bật thông báo" hoặc do họ sửa
   * cài đặt trang web rồi quay lại tab. Trang cha dùng mốc này để bật/tắt phần hiển thị
   * thông báo hệ điều hành; component không tự gửi thông báo nào.
   */
  onPermissionChange?: (state: NotificationPermissionState) => void;
}

/** Trạng thái `default`/`denied` mới có thể bị người dùng ẩn; hai trạng thái kia không hiện. */
function readDismissed(state: NotificationPermissionState): boolean {
  return state === 'default' || state === 'denied' ? isPromptDismissed(state) : false;
}

/**
 * Lời mời bật thông báo + hướng dẫn khi trình duyệt đã chặn — task *"Xin quyền thông báo
 * và hướng dẫn khi người dùng từ chối"* (US 10, Sprint 5 dòng 23 — Hoàng Văn Thịnh).
 *
 * Bốn trạng thái, không gộp trạng thái nào vào nhau:
 *
 * | Trạng thái    | Giao diện                                                        |
 * |---------------|------------------------------------------------------------------|
 * | `unsupported` | Không hiện gì — trình duyệt/ngữ cảnh không xin được quyền         |
 * | `default`     | Lời mời + nút **Bật thông báo** (gọi requestPermission) + **Để sau** |
 * | `granted`     | Không hiện gì — đã bật rồi                                       |
 * | `denied`      | Hướng dẫn mở lại quyền theo từng trình duyệt + **Kiểm tra lại**  |
 *
 * ⚠️ Trình duyệt chỉ cho hỏi quyền từ một thao tác bấm của người dùng, và sau khi người
 * dùng bấm "Chặn" thì **không hỏi lại được** — vì vậy nhánh `denied` chỉ hướng dẫn mở
 * cài đặt trang web, không gọi requestPermission lần nữa.
 *
 * Component KHÔNG tự đặt vào layout: `App.tsx`/`main.tsx` là file của Băng, phải nhờ chủ
 * file nhúng (docs/46 §4). Cũng KHÔNG gửi thông báo — việc đẩy tin vẫn là SignalR + bảng
 * Notifications (A8.5).
 */
export default function NotificationPermissionPrompt({
  onPermissionChange,
}: NotificationPermissionPromptProps) {
  const [permission, setPermission] = useState<NotificationPermissionState>(
    getNotificationPermission,
  );
  const [dismissed, setDismissed] = useState(() => readDismissed(getNotificationPermission()));
  const [requesting, setRequesting] = useState(false);

  // Giữ trạng thái mới nhất ngoài render để callback của trình duyệt và effect không đọc
  // phải giá trị cũ đã đóng gói trong closure.
  const permissionRef = useRef(permission);

  const applyPermission = useCallback(
    (next: NotificationPermissionState) => {
      const changed = permissionRef.current !== next;
      permissionRef.current = next;
      setPermission(next);
      setDismissed(readDismissed(next));

      if (changed) onPermissionChange?.(next);
    },
    [onPermissionChange],
  );

  // Người dùng có thể mở cài đặt trang web, đổi quyền rồi quay lại tab — đồng bộ lại
  // thay vì bắt họ tải lại trang.
  useEffect(
    () => watchNotificationPermission(() => applyPermission(getNotificationPermission())),
    [applyPermission],
  );

  const handleEnable = async () => {
    if (requesting) return;

    setRequesting(true);

    try {
      applyPermission(await requestNotificationPermission());
    } finally {
      setRequesting(false);
    }
  };

  const handleSnooze = () => {
    if (permission === 'default' || permission === 'denied') dismissPrompt(permission);
    setDismissed(true);
  };

  // Đọc lại quyền sau khi người dùng sửa cài đặt trình duyệt. Không gọi
  // requestPermission: khi đã bị chặn, trình duyệt không hiện hộp thoại nữa.
  const handleRecheck = () => {
    const next = getNotificationPermission();
    applyPermission(next);
    if (next === 'default' || next === 'denied') setDismissed(false);
  };

  if (permission === 'unsupported' || permission === 'granted' || dismissed) return null;

  if (permission === 'default') {
    return (
      <Alert
        type="info"
        showIcon
        icon={<BellOutlined />}
        message="Bật thông báo để không bỏ lỡ chuyến xe"
        description="Ứng dụng sẽ báo khi xe sắp đến trạm, khi yêu cầu đổi/huỷ vé được xử lý và khi hoàn tiền xong."
        action={
          <Space wrap>
            <Button type="primary" size="small" loading={requesting} onClick={handleEnable}>
              Bật thông báo
            </Button>
            <Button type="text" size="small" onClick={handleSnooze}>
              Để sau
            </Button>
          </Space>
        }
        style={{ marginBottom: 16 }}
      />
    );
  }

  return (
    <Alert
      type="warning"
      showIcon
      message="Trình duyệt đang chặn thông báo"
      description={
        <>
          <Paragraph style={{ marginBottom: 8 }}>
            Bạn đã từ chối quyền thông báo nên trình duyệt <Text strong>không hỏi lại</Text>.
            Muốn nhận thông báo, hãy mở lại quyền trong cài đặt trang web:
          </Paragraph>

          <Collapse
            size="small"
            ghost
            items={[
              {
                key: 'chromium',
                label: 'Chrome, Edge, Cốc Cốc',
                children: (
                  <ol style={{ margin: 0, paddingLeft: 20 }}>
                    <li>Bấm biểu tượng ổ khoá (hoặc nút điều khiển trang) cạnh địa chỉ web.</li>
                    <li>
                      Chọn <Text strong>Cài đặt trang web</Text> → <Text strong>Thông báo</Text>.
                    </li>
                    <li>
                      Đổi sang <Text strong>Cho phép</Text>, rồi tải lại trang.
                    </li>
                  </ol>
                ),
              },
              {
                key: 'firefox',
                label: 'Firefox',
                children: (
                  <ol style={{ margin: 0, paddingLeft: 20 }}>
                    <li>Bấm biểu tượng ổ khoá cạnh địa chỉ web → Quyền.</li>
                    <li>
                      Ở mục <Text strong>Thông báo</Text>, bỏ chặn trang này.
                    </li>
                    <li>Tải lại trang rồi bấm "Bật thông báo" khi lời mời hiện lại.</li>
                  </ol>
                ),
              },
              {
                key: 'safari',
                label: 'Safari',
                children: (
                  <ol style={{ margin: 0, paddingLeft: 20 }}>
                    <li>
                      Trên máy Mac: <Text strong>Safari</Text> →{' '}
                      <Text strong>Cài đặt cho trang web này</Text> →{' '}
                      <Text strong>Thông báo</Text> → Cho phép.
                    </li>
                    <li>
                      Trên iPhone/iPad: <Text strong>Cài đặt</Text> →{' '}
                      <Text strong>Thông báo</Text> → chọn Safari hoặc ứng dụng web đã thêm vào
                      Màn hình chính → bật thông báo.
                    </li>
                  </ol>
                ),
              },
            ]}
          />

          <Paragraph type="secondary" style={{ margin: '8px 0 0' }}>
            Đang mở chế độ ẩn danh? Hãy đóng cửa sổ đó và mở ứng dụng bằng cửa sổ thường — một
            số trình duyệt chặn thông báo ở chế độ ẩn danh.
          </Paragraph>
        </>
      }
      action={
        <Space direction="vertical" size={4}>
          <Button size="small" onClick={handleRecheck}>
            Kiểm tra lại
          </Button>
          <Button type="text" size="small" onClick={handleSnooze}>
            Đã hiểu
          </Button>
        </Space>
      }
      style={{ marginBottom: 16 }}
    />
  );
}
