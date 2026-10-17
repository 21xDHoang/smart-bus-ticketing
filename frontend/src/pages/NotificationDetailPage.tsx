import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Button, Result, Typography, theme } from 'antd';
import { CheckCircleFilled, CloseCircleFilled } from '@ant-design/icons';
import notificationApi from '../api/notificationApi';
import type { AppNotification } from '../api/notificationApi';
import { useAuth } from '../contexts';
import { ErrorState, LoadingState, PageCard, PageHeader } from '../components/ui';
import { formatDateTime } from '../components/ui/format';
import { isPositiveNotification } from '../components/notificationMeta';
import { useNotificationReadState } from '../components/notificationReadState';

const { Text } = Typography;

/** Kết quả một lượt tải thông báo theo id — bốn trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'not-found' }
  | { status: 'done'; notification: AppNotification };

// Màn hình chi tiết thông báo (US 10 "Thông báo trạm", Sprint 5 — Dương Thị Hạnh): hiển thị
// toàn bộ nội dung một thông báo (tiêu đề, nội dung xuống dòng được, thời điểm nhận) và tự đánh
// dấu đã đọc khi mở. Backend không có GET /notifications/{id} nên màn này gọi lại danh sách
// rồi lọc theo id trên URL — đúng hợp đồng api-contract.md (màn chi tiết đọc thẳng từ dòng đã
// tải về, không dựng endpoint riêng).
export default function NotificationDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth();
  const { token } = theme.useToken();

  const userId = user?.id ?? null;

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);
  const { markRead } = useNotificationReadState(userId);

  // Tải danh sách rồi lọc theo id trên URL. `cancelled` chặn setState sau khi đã rời màn hình.
  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    notificationApi
      .listMine()
      .then((list) => {
        if (cancelled) return;
        const found = list.find((item) => item.id === id) ?? null;
        if (found) {
          markRead(found.id);
          setState({ status: 'done', notification: found });
        } else {
          setState({ status: 'not-found' });
        }
      })
      .catch(() => {
        if (cancelled) return;
        setState({ status: 'error' });
      });

    return () => {
      cancelled = true;
    };
    // `markRead` cố ý không nằm trong deps: nó được tạo lại mỗi render, đưa vào sẽ lặp vô hạn.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, reloadKey]);

  if (state.status === 'loading') {
    return (
      <div>
        <PageHeader title="Chi tiết thông báo" onBack={() => navigate('/notifications')} />
        <PageCard>
          <LoadingState description="Đang tải thông báo…" />
        </PageCard>
      </div>
    );
  }

  if (state.status === 'error') {
    return (
      <div>
        <PageHeader title="Chi tiết thông báo" onBack={() => navigate('/notifications')} />
        <PageCard>
          <ErrorState
            title="Không tải được thông báo"
            onRetry={() => setReloadKey((n) => n + 1)}
          />
        </PageCard>
      </div>
    );
  }

  if (state.status === 'not-found') {
    return (
      <div>
        <PageHeader title="Chi tiết thông báo" onBack={() => navigate('/notifications')} />
        <PageCard>
          <Result
            status="404"
            title="Không tìm thấy thông báo"
            subTitle="Thông báo này không tồn tại hoặc không thuộc tài khoản của bạn."
            extra={
              <Button type="primary" onClick={() => navigate('/notifications')}>
                Về danh sách thông báo
              </Button>
            }
          />
        </PageCard>
      </div>
    );
  }

  const notification = state.notification;
  const positive = isPositiveNotification(notification.type);

  return (
    <div>
      <PageHeader title="Chi tiết thông báo" onBack={() => navigate('/notifications')} />

      <PageCard>
        <div style={{ display: 'flex', gap: 12 }}>
          <span
            aria-hidden
            style={{
              flexShrink: 0,
              fontSize: 24,
              lineHeight: '28px',
              color: positive ? token.colorSuccess : token.colorError,
            }}
          >
            {positive ? <CheckCircleFilled /> : <CloseCircleFilled />}
          </span>

          <div style={{ minWidth: 0, flex: 1 }}>
            <Text strong style={{ fontSize: 16 }}>
              {notification.title}
            </Text>

            <div
              style={{
                marginTop: 8,
                whiteSpace: 'pre-wrap',
                overflowWrap: 'break-word',
              }}
            >
              {notification.body}
            </div>

            <div style={{ marginTop: 16, color: token.colorTextSecondary, fontSize: 12 }}>
              {formatDateTime(notification.createdAt)}
            </div>
          </div>
        </div>
      </PageCard>
    </div>
  );
}
