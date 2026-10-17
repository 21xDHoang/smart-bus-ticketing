import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Badge, Button, Empty, List, Popover, Spin, Typography, theme } from 'antd';
import { BellOutlined, CheckCircleFilled, CloseCircleFilled } from '@ant-design/icons';
import dayjs from 'dayjs';
import notificationApi from '../api/notificationApi';
import type { AppNotification, NotificationType } from '../api/notificationApi';
import { useAuth } from '../contexts';
import { formatDateTime } from './ui/format';

const { Text } = Typography;

// -----------------------------------------------------------------------------
// Chuông thông báo + badge — task "Hiển thị danh sách thông báo trong ứng dụng (chuông +
// badge)" (US 10, Nguyễn Đình Băng). Đặt trên thanh trên cùng, chỉ hiện khi đã đăng nhập.
//
// Trạng thái "đã đọc" CHƯA có ở backend (docs/33 §2 — bảng Notifications chỉ-ghi-thêm, câu
// hỏi "ReadAt hay bảng nối" đang để mở cho chính badge này). Nên badge tạm dùng quy ước
// PHÍA CLIENT: đếm số thông báo sinh sau mốc "đã xem lần cuối" lưu trong localStorage, mỗi
// người một mốc riêng. Mở chuông = đã xem → badge về 0. Khi backend có cột đã đọc, thay
// khối localStorage này bằng giá trị API trả về, không đổi hợp đồng hiển thị.
// -----------------------------------------------------------------------------

/** Khoá localStorage mốc "đã xem lần cuối" — khóa theo id người dùng. */
const LAST_SEEN_KEY_PREFIX = 'smartbus.notifications.lastSeen.';

/** Đọc mốc "đã xem" (epoch ms) của một người. Không có → null = coi mọi thông báo là chưa đọc. */
function readLastSeen(userId: string): number | null {
  try {
    const raw = localStorage.getItem(`${LAST_SEEN_KEY_PREFIX}${userId}`);
    return raw ? Number(raw) : null;
  } catch {
    return null;
  }
}

/** Ghi mốc "đã xem" (epoch ms). Lỗi (private mode, đầy bộ nhớ) thì nuốt — chỉ mất trạng thái, không chặn app. */
function writeLastSeen(userId: string, at: number): void {
  try {
    localStorage.setItem(`${LAST_SEEN_KEY_PREFIX}${userId}`, String(at));
  } catch {
    // Không làm gì — badge chỉ không nhớ được trạng thái đã đọc.
  }
}

/** Một thông báo có còn "chưa đọc" không — chưa có mốc nào thì mọi thông báo đều chưa đọc. */
function isUnread(createdAt: string, lastSeen: number | null): boolean {
  return lastSeen === null || dayjs(createdAt).valueOf() > lastSeen;
}

/** Số thông báo chưa đọc trong một danh sách. */
function countUnread(items: AppNotification[], lastSeen: number | null): number {
  return items.filter((item) => isUnread(item.createdAt, lastSeen)).length;
}

/** Giờ tương đối gọn cho một mục thông báo — "Vừa xong" / "2 giờ trước" / "3 ngày trước". */
function formatRelativeTime(iso: string): string {
  const diffMinutes = dayjs().diff(dayjs(iso), 'minute');
  if (diffMinutes < 1) return 'Vừa xong';
  if (diffMinutes < 60) return `${diffMinutes} phút trước`;

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours} giờ trước`;

  const diffDays = Math.floor(diffHours / 24);
  if (diffDays < 30) return `${diffDays} ngày trước`;

  return formatDateTime(iso);
}

/**
 * Loại thông báo mang tin TỐT (duyệt yêu cầu / hoàn tiền thành công) — quyết định biểu
 * tượng. Ba loại còn lại đều là tin xấu: từ chối yêu cầu hoặc hoàn tiền thất bại.
 */
function isPositiveOutcome(type: NotificationType): boolean {
  return (
    type === 'CancellationApproved' ||
    type === 'TicketChangeApproved' ||
    type === 'RefundSucceeded'
  );
}

/** Một mục trong danh sách thông báo — biểu tượng theo kết quả, nội dung xuống dòng được. */
function NotificationItem({ notification }: { notification: AppNotification }) {
  const { token } = theme.useToken();
  const positive = isPositiveOutcome(notification.type);

  return (
    <div style={{ display: 'flex', gap: 10, padding: '10px 4px' }}>
      <span
        aria-hidden
        style={{
          flexShrink: 0,
          fontSize: 18,
          lineHeight: '22px',
          color: positive ? token.colorSuccess : token.colorError,
        }}
      >
        {positive ? <CheckCircleFilled /> : <CloseCircleFilled />}
      </span>

      <div style={{ minWidth: 0 }}>
        <Text strong style={{ fontSize: 13 }}>
          {notification.title}
        </Text>
        <div
          style={{
            margin: '2px 0 4px',
            fontSize: 12,
            color: token.colorTextSecondary,
            whiteSpace: 'pre-wrap',
            overflowWrap: 'break-word',
          }}
        >
          {notification.body}
        </div>
        <Text type="secondary" style={{ fontSize: 12 }}>
          {formatRelativeTime(notification.createdAt)}
        </Text>
      </div>
    </div>
  );
}

/** Ba trạng thái tải danh sách — không gộp trạng thái nào vào nhau. */
type LoadState = 'loading' | 'error' | 'done';

export default function NotificationBell() {
  const { user, isAuthenticated } = useAuth();
  const { token } = theme.useToken();
  const navigate = useNavigate();

  const userId = user?.id ?? null;

  const [items, setItems] = useState<AppNotification[]>([]);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [reloadKey, setReloadKey] = useState(0);
  const [lastSeen, setLastSeen] = useState<number | null>(null);
  const [open, setOpen] = useState(false);

  // Khi đổi người đăng nhập (hoặc đăng xuất), đọc lại mốc "đã xem" của người đó.
  useEffect(() => {
    // Đây là đồng bộ state với localStorage (nguồn ngoài React), không phải state dẫn xuất.
    // oxlint-disable-next-line react/set-state-in-effect
    setLastSeen(userId ? readLastSeen(userId) : null);
  }, [userId]);

  // Tải danh sách thông báo của người đang đăng nhập.
  useEffect(() => {
    if (!isAuthenticated) return;

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
  }, [isAuthenticated, reloadKey]);

  const handleOpenChange = (next: boolean) => {
    setOpen(next);

    // Mở chuông = đã xem: đẩy mốc lên "bây giờ" để badge về 0 (quy ước client, xem đầu file).
    if (next && userId) {
      const now = Date.now();
      writeLastSeen(userId, now);
      setLastSeen(now);
    }
  };

  // Chưa đăng nhập thì không có thông báo của ai để hiển thị — giấu chuông.
  if (!isAuthenticated) return null;

  const unread = countUnread(items, lastSeen);

  const content = (
    <div style={{ width: 'min(340px, calc(100vw - 32px))' }}>
      <Text strong style={{ display: 'block', marginBottom: 4 }}>
        Thông báo
      </Text>

      {loadState === 'loading' && (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '24px 0' }}>
          <Spin />
        </div>
      )}

      {loadState === 'error' && (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={
            <span>
              Không tải được thông báo.{' '}
              <Button type="link" size="small" onClick={() => setReloadKey((n) => n + 1)}>
                Thử lại
              </Button>
            </span>
          }
        />
      )}

      {loadState === 'done' && items.length === 0 && (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có thông báo nào" />
      )}

      {loadState === 'done' && items.length > 0 && (
        <>
          <List
            dataSource={items}
            renderItem={(item) => <NotificationItem notification={item} />}
            style={{ borderTop: `1px solid ${token.colorBorderSecondary}` }}
          />
          <div
            style={{
              textAlign: 'center',
              borderTop: `1px solid ${token.colorBorderSecondary}`,
              paddingTop: 4,
            }}
          >
            <Button
              type="link"
              size="small"
              onClick={() => {
                setOpen(false);
                navigate('/notifications');
              }}
            >
              Xem tất cả
            </Button>
          </div>
        </>
      )}
    </div>
  );

  return (
    <Popover
      content={content}
      trigger="click"
      placement="bottomRight"
      open={open}
      onOpenChange={handleOpenChange}
    >
      <Badge count={unread} size="small" offset={[-4, 4]}>
        <Button
          type="text"
          aria-label="Thông báo"
          icon={<BellOutlined style={{ fontSize: 18 }} />}
          style={{ width: 44, height: 44 }}
        />
      </Badge>
    </Popover>
  );
}
