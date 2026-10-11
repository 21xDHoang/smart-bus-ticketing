import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Alert, Button, Card, Flex, Tag, Typography, theme } from 'antd';
import {
  CreditCardOutlined,
  EditOutlined,
  GiftOutlined,
  QrcodeOutlined,
  RightOutlined,
  SearchOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router-dom';
import { EmptyState, LoadingState, PageHeader, RouteBadge } from '../components/ui';
import type { NavGroup, NavLeaf } from '../components/ui/navItems';
import { visibleNav } from '../components/ui/navItems';
import { formatDate, formatDateTime, formatVnd } from '../components/ui/format';
import { readRecentRoutes } from '../components/ui/recentRoutes';
import type { RecentRoute } from '../components/ui/recentRoutes';
import TicketStatusTag from '../components/TicketStatusTag';
import { getTicketStatus } from '../components/ticketStatus';
import { getRoleMeta } from '../api/adminUserApi';
import ticketApi from '../api/ticketApi';
import type { Ticket } from '../api/ticketApi';
import voucherApi from '../api/voucherApi';
import type { Voucher } from '../api/voucherApi';
import { useAuth } from '../contexts';

const { Text, Title } = Typography;

/** Mở đường dẫn nội bộ, kèm dữ liệu điều hướng khi cần (màn tra cứu đọc lại để điền sẵn). */
type OpenPage = (to: string, state?: Record<string, string>) => void;

/**
 * Trang chủ — hai biến thể theo vai trò:
 *
 * • Hành khách: ô tìm chuyến (việc chính của trang — CẢ thẻ là một nút), "Vé đang dùng"
 *   (vé còn hiệu lực gần khởi hành nhất, kèm lối vào mã QR), 4 lối vào nhanh, banner
 *   khuyến mãi. KHÔNG gọi `GET /routes` ở đây — đó là endpoint quản trị, khách bị 403;
 *   tuyến gợi ý đọc từ localStorage (recentRoutes.ts).
 * • Nhân viên (Admin/Manager/Driver): giữ nguyên lưới lối vào theo menu vai trò.
 *
 * Mỗi màn chỉ có MỘT hành động nổi bật (với khách là ô tìm chuyến) — các nút còn lại để
 * kiểu mặc định, không dùng `type="primary"`, tránh hai nút xanh tranh nhau.
 */
export default function HomePage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const role = user?.role ?? null;
  const roleMeta = getRoleMeta(user?.role ?? '');

  const openPage: OpenPage = (to, state) => {
    navigate(to, state ? { state } : undefined);
  };

  const entries = visibleNav(role);
  // "Trang chủ" không lên thẻ lối vào — đang đứng ngay đây rồi.
  const leaves = entries.filter((entry): entry is NavLeaf => !('children' in entry) && entry.to !== '/');
  const groups = entries.filter((entry): entry is NavGroup => 'children' in entry);

  return (
    <div>
      <PageHeader
        title={`Xin chào, ${user?.fullName || 'Bạn'}! 👋`}
        subtitle={
          <Flex align="center" gap={8} wrap>
            <span>Bạn đang đăng nhập với vai trò</span>
            {/* Nền đậm sẵn có của vai trò + chữ trắng (variant="solid") — màu preset của antd
                (green/cyan/geekblue/purple) chỉ đạt ~3.4–4.2:1 ở một trong hai chế độ, không
                đủ chuẩn AA; cách này đạt >=4.7:1 ở cả hai chế độ. */}
            <Tag variant="solid" color={roleMeta.bg} style={{ marginInlineEnd: 0 }}>
              {roleMeta.label}
            </Tag>
          </Flex>
        }
      />

      {role === 'Passenger' ? (
        <PassengerHome onOpen={openPage} />
      ) : (
        <>
          {leaves.length > 0 && (
            <section>
              <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
                Lối vào nhanh
              </Title>
              <TileGrid>
                {leaves.map((leaf) => (
                  <ShortcutTile key={leaf.to} to={leaf.to} label={leaf.label} icon={leaf.icon} onOpen={openPage} />
                ))}
              </TileGrid>
            </section>
          )}

          {groups.map((group) => (
            <section key={group.key} style={{ marginTop: 24 }}>
              <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
                {group.label}
              </Title>
              <TileGrid>
                {group.children.map((leaf) => (
                  <ShortcutTile key={leaf.to} to={leaf.to} label={leaf.label} icon={leaf.icon} onOpen={openPage} />
                ))}
              </TileGrid>
            </section>
          ))}
        </>
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Thẻ lối vào (dùng chung hai biến thể)                               */
/* ------------------------------------------------------------------ */

/** Thẻ lối vào lớn — CẢ thẻ là một nút, đủ cao để bấm trên điện thoại. */
function ShortcutTile({
  to,
  label,
  icon,
  onOpen,
}: {
  to: string;
  label: string;
  icon: ReactNode;
  onOpen: OpenPage;
}) {
  return (
    <button
      type="button"
      onClick={() => onOpen(to)}
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'flex-start',
        gap: 8,
        minHeight: 96,
        padding: 16,
        textAlign: 'left',
        cursor: 'pointer',
        background: 'var(--app-surface)',
        border: '1px solid var(--app-border)',
        borderRadius: 12,
        color: 'inherit',
        font: 'inherit',
      }}
    >
      <span aria-hidden="true" style={{ fontSize: 22, lineHeight: 1, color: 'var(--app-primary)' }}>
        {icon}
      </span>
      <span style={{ fontWeight: 600 }}>{label}</span>
    </button>
  );
}

function TileGrid({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
        gap: 12,
      }}
    >
      {children}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Trang chủ hành khách                                                */
/* ------------------------------------------------------------------ */

/** Bốn mục con mà thanh tab đáy không với tới — cố ý KHÔNG có "Tra cứu tuyến" vì ô
    tìm chuyến phía trên đã là lối vào đó (một đích chỉ xuất hiện một lần). */
const PASSENGER_TILES: { to: string; label: string; icon: ReactNode }[] = [
  { to: '/my-tickets', label: 'Vé của tôi', icon: <QrcodeOutlined /> },
  { to: '/my-monthly-passes', label: 'Vé tháng của tôi', icon: <CreditCardOutlined /> },
  { to: '/vouchers/available', label: 'Khuyến mãi', icon: <GiftOutlined /> },
  { to: '/feedback-submit', label: 'Gửi phản ánh', icon: <EditOutlined /> },
];

function PassengerHome({ onOpen }: { onOpen: OpenPage }) {
  const [recent] = useState<RecentRoute[]>(readRecentRoutes);

  const [tickets, setTickets] = useState<Ticket[] | null>(null);
  const [ticketsFailed, setTicketsFailed] = useState(false);
  const [retryTick, setRetryTick] = useState(0);
  const [vouchers, setVouchers] = useState<Voucher[]>([]);

  useEffect(() => {
    let alive = true;
    setTickets(null);
    setTicketsFailed(false);
    ticketApi
      .list()
      .then((items) => {
        if (alive) setTickets(items);
      })
      .catch(() => {
        if (alive) setTicketsFailed(true);
      });
    return () => {
      alive = false;
    };
  }, [retryTick]);

  useEffect(() => {
    let alive = true;
    voucherApi
      .listAvailable()
      .then((items) => {
        if (alive) setVouchers(items);
      })
      .catch(() => {
        // Khuyến mãi là phần phụ — tải hỏng thì ẩn hẳn, không chặn trang.
        if (alive) setVouchers([]);
      });
    return () => {
      alive = false;
    };
  }, []);

  const activeTickets = (tickets ?? [])
    .filter((ticket) => getTicketStatus(ticket) === 'Valid')
    .sort((a, b) => dayjs(a.departureTime).valueOf() - dayjs(b.departureTime).valueOf());
  const nextTicket = activeTickets[0] ?? null;

  return (
    <Flex vertical gap={24}>
      <SearchHero recent={recent} onOpen={onOpen} />

      <section>
        <Flex justify="space-between" align="baseline" gap={8} wrap style={{ marginBottom: 12 }}>
          <Title level={5} style={{ margin: 0 }}>
            Vé đang dùng
          </Title>
          {activeTickets.length > 1 && (
            <Text type="secondary" style={{ fontSize: 13 }}>
              Còn {activeTickets.length - 1} vé khác trong “Vé của tôi”
            </Text>
          )}
        </Flex>

        {tickets === null && !ticketsFailed ? (
          <Card styles={{ body: { padding: 16 } }}>
            <LoadingState description="Đang tải vé của bạn…" />
          </Card>
        ) : ticketsFailed ? (
          <Alert
            type="error"
            showIcon
            message="Không tải được vé đang dùng."
            action={
              <Button size="small" onClick={() => setRetryTick((n) => n + 1)}>
                Thử lại
              </Button>
            }
          />
        ) : nextTicket ? (
          <ActiveTicketCard
            ticket={nextTicket}
            onShowQr={() => onOpen(`/my-tickets/${nextTicket.id}/qr`)}
            onShowAll={() => onOpen('/my-tickets')}
          />
        ) : (
          <Card styles={{ body: { padding: 16 } }}>
            <EmptyState
              description="Bạn chưa có vé nào sắp khởi hành."
              action={<Button onClick={() => onOpen('/route-lookup')}>Tìm chuyến xe</Button>}
            />
          </Card>
        )}
      </section>

      <section>
        <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
          Lối vào nhanh
        </Title>
        <TileGrid>
          {PASSENGER_TILES.map((tile) => (
            <ShortcutTile key={tile.to} to={tile.to} label={tile.label} icon={tile.icon} onOpen={onOpen} />
          ))}
        </TileGrid>
      </section>

      {vouchers.length > 0 && (
        <section>
          <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
            Khuyến mãi
          </Title>
          <PromoBanner voucher={vouchers[0]} onOpenAll={() => onOpen('/vouchers/available')} />
        </section>
      )}
    </Flex>
  );
}

/** Ô tìm chuyến — hành động chính của trang chủ hành khách. Nền xanh nhạt + viền xanh,
    chữ lấy từ token `colorPrimaryText` (đạt AA ở cả hai chế độ); thẻ không dùng nút
    primary xanh đặc vì cả thẻ đã là nút. */
function SearchHero({ recent, onOpen }: { recent: RecentRoute[]; onOpen: OpenPage }) {
  const { token } = theme.useToken();

  return (
    <div
      style={{
        background: token.colorPrimaryBg,
        border: `1px solid ${token.colorPrimaryBorder}`,
        borderRadius: token.borderRadiusLG,
        padding: 16,
      }}
    >
      <button
        type="button"
        onClick={() => onOpen('/route-lookup')}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          width: '100%',
          minHeight: 56,
          padding: 0,
          border: 0,
          background: 'transparent',
          cursor: 'pointer',
          textAlign: 'left',
          color: 'inherit',
          font: 'inherit',
        }}
      >
        <span
          aria-hidden="true"
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flex: '0 0 auto',
            width: 44,
            height: 44,
            borderRadius: '50%',
            background: token.colorPrimary,
            color: token.colorTextLightSolid,
            fontSize: 20,
          }}
        >
          <SearchOutlined />
        </span>
        <span style={{ display: 'flex', flexDirection: 'column', gap: 2, flex: 1, minWidth: 0 }}>
          <span style={{ fontWeight: 700, fontSize: 16, color: token.colorText }}>Tìm chuyến xe</span>
          <span style={{ color: token.colorPrimaryText, fontSize: 13 }}>
            Chọn điểm đi, điểm đến và ngày khởi hành
          </span>
        </span>
        <RightOutlined aria-hidden="true" style={{ color: token.colorPrimaryText }} />
      </button>

      {recent.length > 0 && (
        <Flex gap={8} wrap style={{ marginTop: 12 }}>
          {recent.map((route, index) => (
            <RecentRouteChip key={`${index}-${route.origin}-${route.destination}`} route={route} onOpen={onOpen} />
          ))}
        </Flex>
      )}
    </div>
  );
}

/** Chip "tuyến gần đây" — bấm là sang màn tra cứu với sẵn điểm đi/điểm đến. */
function RecentRouteChip({ route, onOpen }: { route: RecentRoute; onOpen: OpenPage }) {
  const { token } = theme.useToken();

  return (
    <button
      type="button"
      onClick={() => onOpen('/route-lookup', { origin: route.origin, destination: route.destination })}
      style={{
        minHeight: 36,
        padding: '6px 12px',
        borderRadius: 999,
        border: `1px solid ${token.colorPrimaryBorder}`,
        background: token.colorBgContainer,
        color: token.colorText,
        font: 'inherit',
        fontSize: 13,
        cursor: 'pointer',
      }}
    >
      {route.origin} → {route.destination}
    </button>
  );
}

function ActiveTicketCard({
  ticket,
  onShowQr,
  onShowAll,
}: {
  ticket: Ticket;
  onShowQr: () => void;
  onShowAll: () => void;
}) {
  const { token } = theme.useToken();

  return (
    <Card styles={{ body: { padding: 16 } }}>
      <Flex justify="space-between" align="flex-start" gap={8} wrap>
        <Flex align="center" gap={8} wrap>
          <RouteBadge code={ticket.routeCode} />
          <Text strong>{ticket.routeName}</Text>
        </Flex>
        <TicketStatusTag ticket={ticket} />
      </Flex>

      <div style={{ marginTop: 8, color: token.colorTextSecondary, fontSize: 13 }}>
        Khởi hành <span className="tabular-nums">{formatDateTime(ticket.departureTime)}</span> · Ghế{' '}
        <span className="tabular-nums">{ticket.seatNumber}</span>
      </div>

      <Flex justify="space-between" align="center" gap={8} wrap style={{ marginTop: 12 }}>
        <Text type="secondary" style={{ fontSize: 13 }}>
          Mã vé: <span className="tabular-nums">{ticket.code}</span>
        </Text>
        <Flex gap={8}>
          <Button onClick={onShowAll}>Tất cả vé</Button>
          <Button icon={<QrcodeOutlined />} onClick={onShowQr}>
            Xem mã QR
          </Button>
        </Flex>
      </Flex>
    </Card>
  );
}

function discountText(voucher: Voucher): string {
  const base =
    voucher.discountType === 'Percent'
      ? `Giảm ${voucher.discountValue}%`
      : `Giảm ${formatVnd(voucher.discountValue)}`;
  const cap =
    voucher.discountType === 'Percent' && voucher.maxDiscount ? `, tối đa ${formatVnd(voucher.maxDiscount)}` : '';
  const min = voucher.minOrderValue > 0 ? ` · Đơn từ ${formatVnd(voucher.minOrderValue)}` : '';
  return `${base}${cap}${min}`;
}

/** Banner khuyến mãi — chỉ là phần phụ trợ: tải hỏng hoặc không có mã thì cả mục được ẩn
    (xử lý ở PassengerHome), người dùng vẫn còn lối vào "Khuyến mãi" trong thẻ nhanh. */
function PromoBanner({ voucher, onOpenAll }: { voucher: Voucher; onOpenAll: () => void }) {
  const { token } = theme.useToken();

  return (
    <Card styles={{ body: { padding: 16 } }}>
      <Flex align="center" gap={12} wrap>
        <span
          aria-hidden="true"
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flex: '0 0 auto',
            width: 44,
            height: 44,
            borderRadius: '50%',
            background: token.colorWarningBg,
            color: token.colorWarningText,
            fontSize: 20,
          }}
        >
          <GiftOutlined />
        </span>

        <div style={{ flex: 1, minWidth: 200 }}>
          <Text strong style={{ display: 'block' }}>
            {voucher.name}
          </Text>
          <Text type="secondary" style={{ fontSize: 13 }}>
            {discountText(voucher)}
          </Text>
          <div style={{ marginTop: 4 }}>
            <Text code className="tabular-nums">
              {voucher.code}
            </Text>
            <Text type="secondary" style={{ fontSize: 12, marginLeft: 8 }}>
              Hiệu lực đến {formatDate(voucher.validUntil)}
            </Text>
          </div>
        </div>

        <Button type="link" onClick={onOpenAll} style={{ padding: 0 }}>
          Tất cả khuyến mãi
        </Button>
      </Flex>
    </Card>
  );
}
