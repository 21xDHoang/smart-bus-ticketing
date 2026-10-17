import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, List, Typography, theme } from 'antd';
import { CheckCircleFilled, CheckOutlined, CloseCircleFilled } from '@ant-design/icons';
import notificationApi from '../api/notificationApi';
import type { AppNotification } from '../api/notificationApi';
import { useAuth } from '../contexts';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader } from '../components/ui';
import { formatRelativeTime, isPositiveNotification } from '../components/notificationMeta';
import { useNotificationReadState } from '../components/notificationReadState';

const { Text, Paragraph } = Typography;

/** Ba trạng thái tải danh sách — không gộp trạng thái nào vào nhau. */
type LoadState = 'loading' | 'error' | 'done';

// Màn hình danh sách thông báo (US 10 "Thông báo trạm", Sprint 5 — Dương Thị Hạnh): hiển thị
// lịch sử thông báo của chính người dùng, mới nhất trước, kèm trạng thái chưa đọc (chấm + chữ
// đậm) và nút "Đánh dấu tất cả đã đọc". Bấm một dòng thì mở màn chi tiết. Vào từ link "Xem tất
// cả" ở chuông thông báo — không nằm trong menu điều hướng.
//
// Backend chỉ có GET /notifications/me; không có endpoint chi tiết (màn chi tiết đọc thẳng từ
// dòng đã tải về). Trạng thái "đã đọc" chưa có ở backend nên lưu PHÍA CLIENT
// (notificationReadState.ts), cùng lối badge chuông đang làm.
export default function NotificationsPage() {
  const navigate = useNavigate();
  const { user } = useAuth();
  const { token } = theme.useToken();

  const userId = user?.id ?? null;

  const [items, setItems] = useState<AppNotification[]>([]);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [reloadKey, setReloadKey] = useState(0);
  const { readIds, markAllRead } = useNotificationReadState(userId);

  // Tải danh sách thông báo của người đang đăng nhập.
  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoadState('loading');

    notificationApi
      .listMine()
      .then((list) => {
        if (cancelled) return;
        setItems(list);
        setLoadState('done');
      })
      .catch(() => {
        if (cancelled) return;
        setLoadState('error');
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const unreadCount = items.filter((item) => !readIds.includes(item.id)).length;

  return (
    <div>
      <PageHeader
        title="Thông báo"
        subtitle="Lịch sử thông báo bạn đã nhận, mới nhất trước."
        extra={
          unreadCount > 0 ? (
            <Button icon={<CheckOutlined />} onClick={() => markAllRead(items.map((i) => i.id))}>
              Đánh dấu tất cả đã đọc
            </Button>
          ) : undefined
        }
      />

      <PageCard>
        {loadState === 'loading' && <LoadingState description="Đang tải thông báo…" />}

        {loadState === 'error' && (
          <ErrorState
            title="Không tải được thông báo"
            onRetry={() => setReloadKey((n) => n + 1)}
          />
        )}

        {loadState === 'done' && items.length === 0 && (
          <EmptyState description="Chưa có thông báo nào" />
        )}

        {loadState === 'done' && items.length > 0 && (
          <List
            dataSource={items}
            renderItem={(item) => {
              const unread = !readIds.includes(item.id);
              const positive = isPositiveNotification(item.type);

              return (
                <List.Item
                  onClick={() => navigate(`/notifications/${item.id}`)}
                  style={{ cursor: 'pointer', paddingInline: 4 }}
                  extra={
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      {formatRelativeTime(item.createdAt)}
                    </Text>
                  }
                >
                  <List.Item.Meta
                    avatar={
                      <span
                        aria-hidden
                        style={{
                          fontSize: 18,
                          lineHeight: '22px',
                          color: positive ? token.colorSuccess : token.colorError,
                        }}
                      >
                        {positive ? <CheckCircleFilled /> : <CloseCircleFilled />}
                      </span>
                    }
                    title={
                      <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        {unread && (
                          <span
                            aria-hidden
                            style={{
                              flexShrink: 0,
                              width: 8,
                              height: 8,
                              borderRadius: '50%',
                              background: token.colorPrimary,
                            }}
                          />
                        )}
                        <Text strong={unread}>{item.title}</Text>
                      </span>
                    }
                    description={
                      <Paragraph
                        type="secondary"
                        ellipsis={{ rows: 2 }}
                        style={{ margin: 0 }}
                      >
                        {item.body}
                      </Paragraph>
                    }
                  />
                </List.Item>
              );
            }}
          />
        )}
      </PageCard>
    </div>
  );
}
