import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Empty, Popconfirm, Space, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import { BellOutlined, DeleteOutlined, SendOutlined } from '@ant-design/icons';
import { PageCard, PageHeader } from '../components/ui';
import { formatDateTimeSeconds } from '../components/ui/format';
import webPushApi from '../api/webPushApi';
import type { WebPushDevice } from '../api/webPushApi';
import {
  describeBrowser,
  enableWebPush,
  ensurePushServiceWorker,
  getCurrentPushEndpoint,
  isWebPushSupported,
  unsubscribeCurrentPushSubscription,
} from '../components/webPush';
import {
  getNotificationPermission,
  watchNotificationPermission,
} from '../components/notificationPermission';
import type { NotificationPermissionState } from '../components/notificationPermission';

const { Paragraph, Text } = Typography;

/** Host của push endpoint — chuỗi FCM dài ~150 ký tự, bảng chỉ cần biết dịch vụ nào. */
function endpointHost(endpoint: string): string {
  try {
    return new URL(endpoint).host;
  } catch {
    return 'push service';
  }
}

/** Câu lỗi hiển thị được: lỗi do webPush.ts tự đặt message tiếng Việt, còn lại câu chung. */
function friendlyError(error: unknown): string {
  if (error instanceof Error && error.message) return error.message;
  return 'Đã có lỗi xảy ra. Vui lòng thử lại.';
}

/**
 * Màn Cài đặt thông báo — task *"Tích hợp Web Push (PWA) để nhận thông báo trên trình
 * duyệt"* (US 10 "Thông báo trạm", Sprint 4 dòng 42 — Trần Trung Hiếu).
 *
 * Ba khối theo đúng thứ tự người dùng cần:
 *   1. trạng thái của TRÌNH DUYỆT NÀY — chưa hỗ trợ / bị chặn / đang nhận / bật lên;
 *   2. bảng các thiết bị đã đăng ký (mỗi trình duyệt một dòng) + gửi thử + gỡ;
 *   3. mẹo: thêm vào Màn hình chính, xoá dữ liệu trang web là mất đăng ký.
 *
 * Quyền thông báo chỉ được xin từ thao tác bấm "Bật thông báo" (enableWebPush gọi
 * requestNotificationPermission) — không xin tự động lúc mở trang. Việc đăng ký service
 * worker thì được làm sớm, vì nó không hỏi quyền.
 */
export default function NotificationSettingsPage() {
  const [supported] = useState(isWebPushSupported);
  const [permission, setPermission] = useState<NotificationPermissionState>(getNotificationPermission);
  const [devices, setDevices] = useState<WebPushDevice[]>([]);
  const [currentEndpoint, setCurrentEndpoint] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    const [list, endpoint] = await Promise.all([
      webPushApi.listDevices(),
      getCurrentPushEndpoint(),
    ]);

    setDevices(list);
    setCurrentEndpoint(endpoint);
  }, []);

  // Tải danh sách thiết bị + trạng thái subscription hiện tại. Đăng ký service worker sớm
  // (không xin quyền) để lần bấm "Bật" sau đó chỉ còn subscribe + POST.
  useEffect(() => {
    let cancelled = false;

    (async () => {
      if (supported) {
        try {
          await ensurePushServiceWorker();
        } catch {
          // Chế độ riêng tư/chính sách chặn service worker — vẫn hiện danh sách thiết bị;
          // nút Bật sẽ báo lỗi cụ thể nếu người dùng thử.
        }
      }

      try {
        const [list, endpoint] = await Promise.all([
          webPushApi.listDevices(),
          getCurrentPushEndpoint(),
        ]);

        if (!cancelled) {
          setDevices(list);
          setCurrentEndpoint(endpoint);
        }
      } catch (error) {
        if (!cancelled) message.error(friendlyError(error));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [supported]);

  // Người dùng đổi quyền trong cài đặt trang web rồi quay lại tab — đồng bộ lại trạng thái.
  useEffect(
    () =>
      watchNotificationPermission(() => {
        setPermission(getNotificationPermission());
        void getCurrentPushEndpoint()
          .then(setCurrentEndpoint)
          .catch(() => {
            // Không đọc được subscription — giữ trạng thái cũ, lần tải sau sửa.
          });
      }),
    [],
  );

  const currentDevice =
    currentEndpoint === null
      ? null
      : (devices.find((device) => device.endpoint === currentEndpoint) ?? null);

  const handleEnable = async () => {
    if (busy) return;

    setBusy(true);

    try {
      const device = await enableWebPush();
      await refresh();
      setPermission(getNotificationPermission());
      message.success(`Đã bật thông báo trên ${describeBrowser(device.userAgent)}.`);
    } catch (error) {
      message.error(friendlyError(error));
      setPermission(getNotificationPermission());
    } finally {
      setBusy(false);
    }
  };

  const handleTest = async (device: WebPushDevice) => {
    if (busy) return;

    setBusy(true);

    try {
      await webPushApi.sendTest(device.id);
      message.success('Đã gửi thông báo thử — kiểm tra thanh thông báo của thiết bị.');
    } catch (error) {
      message.error(friendlyError(error));
    } finally {
      setBusy(false);
    }
  };

  const handleRemove = async (device: WebPushDevice) => {
    if (busy) return;

    setBusy(true);

    try {
      // Gỡ đúng thiết bị đang mở thì huỷ luôn subscription phía trình duyệt cho dứt khoát;
      // thiết bị khác thì chỉ xoá dòng của nó.
      if (currentEndpoint !== null && device.endpoint === currentEndpoint) {
        await unsubscribeCurrentPushSubscription();
      }

      await webPushApi.deleteDevice(device.id);
      await refresh();
      message.success('Đã gỡ thiết bị khỏi danh sách nhận thông báo.');
    } catch (error) {
      message.error(friendlyError(error));
    } finally {
      setBusy(false);
    }
  };

  const handleDisableHere = async () => {
    if (busy) return;

    setBusy(true);

    try {
      await unsubscribeCurrentPushSubscription();

      if (currentDevice) {
        await webPushApi.deleteDevice(currentDevice.id);
      }

      await refresh();
      message.info('Đã tắt thông báo trên thiết bị này.');
    } catch (error) {
      message.error(friendlyError(error));
    } finally {
      setBusy(false);
    }
  };

  const handleRecheck = () => {
    setPermission(getNotificationPermission());
    void getCurrentPushEndpoint()
      .then(setCurrentEndpoint)
      .catch(() => {
        // Giữ trạng thái cũ — lần bấm "Bật" sẽ báo lỗi nếu có.
      });
  };

  const columns: TableProps<WebPushDevice>['columns'] = [
    {
      title: 'Thiết bị',
      key: 'device',
      render: (_, device) => (
        <Space direction="vertical" size={0}>
          <Space size={6} wrap>
            <Text strong>{describeBrowser(device.userAgent)}</Text>
            {device.endpoint === currentEndpoint && <Tag color="blue">Thiết bị này</Tag>}
          </Space>
          <Text type="secondary" style={{ fontSize: 12 }}>
            {endpointHost(device.endpoint)}
          </Text>
        </Space>
      ),
    },
    {
      title: 'Đăng ký lúc',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 180,
      render: (createdAt: string) => formatDateTimeSeconds(createdAt),
    },
    {
      title: 'Hết hạn',
      dataIndex: 'expirationTime',
      key: 'expirationTime',
      width: 180,
      render: (expirationTime: string | null) =>
        expirationTime ? (
          formatDateTimeSeconds(expirationTime)
        ) : (
          <Text type="secondary">Không hẹn giờ</Text>
        ),
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 200,
      render: (_, device) => (
        <Space size={4}>
          <Button
            size="small"
            icon={<SendOutlined />}
            loading={busy}
            onClick={() => void handleTest(device)}
          >
            Gửi thử
          </Button>
          <Popconfirm
            title="Gỡ thiết bị này?"
            description="Thiết bị sẽ không nhận thông báo đẩy nữa cho tới khi bật lại."
            okText="Gỡ"
            cancelText="Huỷ"
            onConfirm={() => void handleRemove(device)}
          >
            <Button size="small" danger icon={<DeleteOutlined />} disabled={busy}>
              Gỡ
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="Cài đặt thông báo"
        subtitle="Nhận thông báo đẩy trên trình duyệt — kể cả khi tab đã đóng."
      />

      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <PageCard title="Thông báo trên trình duyệt này">
          {!supported ? (
            <Alert
              type="warning"
              showIcon
              message="Trình duyệt này chưa hỗ trợ Web Push"
              description="Cần Chrome, Edge, Firefox hoặc Safari bản mới, và trang phải chạy HTTPS (localhost cũng được). Trên iPhone/iPad, hãy thêm ứng dụng vào Màn hình chính trước khi bật."
            />
          ) : permission === 'denied' ? (
            <Alert
              type="error"
              showIcon
              message="Trình duyệt đang chặn thông báo"
              description={
                <>
                  <Paragraph style={{ marginBottom: 8 }}>
                    Bạn đã từ chối quyền thông báo nên trình duyệt <Text strong>không hỏi lại</Text>.
                    Mở biểu tượng ổ khoá cạnh địa chỉ web →{' '}
                    <Text strong>Cài đặt trang web</Text> → <Text strong>Thông báo</Text> →{' '}
                    <Text strong>Cho phép</Text>, rồi tải lại trang này.
                  </Paragraph>
                  <Button size="small" onClick={handleRecheck}>
                    Kiểm tra lại
                  </Button>
                </>
              }
            />
          ) : currentEndpoint !== null ? (
            <Alert
              type="success"
              showIcon
              message="Thiết bị này đang nhận thông báo"
              description={
                currentDevice
                  ? `Đăng ký lúc ${formatDateTimeSeconds(currentDevice.createdAt)}. Bấm "Gửi thử" để kiểm tra thanh thông báo của máy.`
                  : 'Trình duyệt đã có subscription nhưng máy chủ chưa có dòng đăng ký — bấm "Đăng ký lại" để đồng bộ.'
              }
              action={
                <Space wrap>
                  {currentDevice ? (
                    <Button
                      size="small"
                      icon={<SendOutlined />}
                      loading={busy}
                      onClick={() => void handleTest(currentDevice)}
                    >
                      Gửi thử
                    </Button>
                  ) : (
                    <Button
                      size="small"
                      type="primary"
                      icon={<BellOutlined />}
                      loading={busy}
                      onClick={() => void handleEnable()}
                    >
                      Đăng ký lại
                    </Button>
                  )}
                  <Popconfirm
                    title="Tắt thông báo trên thiết bị này?"
                    description="Bạn có thể bật lại bất cứ lúc nào."
                    okText="Tắt"
                    cancelText="Huỷ"
                    onConfirm={() => void handleDisableHere()}
                  >
                    <Button size="small" danger disabled={busy}>
                      Tắt
                    </Button>
                  </Popconfirm>
                </Space>
              }
            />
          ) : (
            <Space direction="vertical" size={12} style={{ width: '100%' }}>
              <Paragraph style={{ margin: 0 }}>
                Bật để nhận thông báo khi xe sắp đến trạm, khi yêu cầu huỷ/đổi vé được xử lý và khi
                hoàn tiền xong — kể cả khi tab đã đóng.
              </Paragraph>
              <Button
                type="primary"
                size="large"
                icon={<BellOutlined />}
                loading={busy}
                onClick={() => void handleEnable()}
              >
                {permission === 'granted' ? 'Đăng ký thiết bị này' : 'Bật thông báo trên thiết bị này'}
              </Button>
              {permission === 'granted' && (
                <Text type="secondary">
                  Trình duyệt đã cho phép thông báo — chỉ còn đăng ký thiết bị với máy chủ.
                </Text>
              )}
            </Space>
          )}
        </PageCard>

        <PageCard
          title="Thiết bị đã đăng ký"
          extra={<Text type="secondary">Mỗi trình duyệt là một thiết bị riêng</Text>}
        >
          <Table<WebPushDevice>
            rowKey="id"
            columns={columns}
            dataSource={devices}
            loading={loading}
            pagination={false}
            scroll={{ x: 760 }}
            locale={{ emptyText: <Empty description="Chưa có thiết bị nào đăng ký" /> }}
          />
        </PageCard>

        <PageCard title="Mẹo để nhận thông báo">
          <ul style={{ margin: 0, paddingLeft: 20 }}>
            <li>
              Trên điện thoại, thêm ứng dụng vào <Text strong>Màn hình chính</Text> để nhận thông
              báo cả khi không mở trình duyệt. iPhone/iPad chỉ cho Web Push với ứng dụng đã thêm vào
              Màn hình chính.
            </li>
            <li>
              Xoá dữ liệu của trang web trong trình duyệt sẽ gỡ đăng ký — quay lại đây bật lại.
            </li>
            <li>
              Nhận thông báo không thay thế danh sách thông báo trong ứng dụng: mọi tin vẫn lưu ở
              mục Thông báo để xem lại.
            </li>
          </ul>
        </PageCard>
      </Space>
    </div>
  );
}
