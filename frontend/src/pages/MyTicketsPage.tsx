import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, List, Segmented, Space, Tag, Typography, theme } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import ticketApi from '../api/ticketApi';
import type { Ticket } from '../api/ticketApi';
import type { AppError } from '../api/axiosClient';
import TicketQr from '../components/TicketQr';
import TicketStatusTag from '../components/TicketStatusTag';
import { getTicketStatus, isTicketUsable, TICKET_STATUS_META } from '../components/ticketStatus';
import type { TicketDisplayStatus } from '../components/ticketStatus';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader } from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Kết quả một lượt tải danh sách vé — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; tickets: Ticket[] };

/** Bộ lọc theo trạng thái hiển thị — 'all' là xem tất cả. */
type Filter = 'all' | TicketDisplayStatus;

/** Một dòng vé đã suy sẵn trạng thái hiển thị — tránh tính lại trong lúc render. */
interface TicketRow {
  ticket: Ticket;
  status: TicketDisplayStatus;
}

/** Các lựa chọn bộ lọc, sinh từ TICKET_STATUS_META để nhãn không lệch với tag trạng thái. */
const FILTER_OPTIONS: { value: Filter; label: string }[] = [
  { value: 'all', label: 'Tất cả' },
  ...(Object.keys(TICKET_STATUS_META) as TicketDisplayStatus[]).map((status) => ({
    value: status,
    label: TICKET_STATUS_META[status].label,
  })),
];

// Màn hình "Vé của tôi" — task "Xử lý vé hết hạn / vé đã sử dụng trên UI" (story 4, Sprint 3,
// Dương Thị Hạnh). Liệt kê vé điện tử của hành khách và vẽ đúng trạng thái hiển thị: vé còn
// hiệu lực giữ mã QR rõ, vé đã sử dụng / hết hạn / đã huỷ thì mã QR bôi xám + tag trạng thái.
// Trạng thái "hết hạn" suy từ giờ khởi hành (xem ticketStatus.ts) chứ không phải cột lưu sẵn.
export default function MyTicketsPage() {
  const navigate = useNavigate();
  const { token } = theme.useToken();

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);
  const [filter, setFilter] = useState<Filter>('all');

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    ticketApi
      .list()
      .then((tickets) => {
        if (cancelled) return;
        setState({ status: 'done', tickets });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({
          status: 'error',
          message: appError.customMessage || 'Không thể tải danh sách vé.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const rows: TicketRow[] =
    state.status === 'done'
      ? state.tickets.map((ticket) => ({ ticket, status: getTicketStatus(ticket) }))
      : [];

  const filtered = rows.filter(({ status }) => filter === 'all' || status === filter);

  return (
    <div>
      <PageHeader
        title="Vé của tôi"
        subtitle="Vé điện tử đã mua của bạn. Vé đã sử dụng, hết hạn hoặc đã huỷ được bôi xám và không thể quét lại."
      />

      <PageCard
        toolbar={
          <Space wrap style={{ width: '100%', justifyContent: 'space-between' }}>
            <Segmented<Filter>
              options={FILTER_OPTIONS}
              value={filter}
              onChange={(value) => setFilter(value as Filter)}
            />
            {state.status === 'done' && (
              <Text type="secondary">
                Hiển thị <Text strong>{filtered.length}</Text> vé
              </Text>
            )}
          </Space>
        }
      >
        {/* Đang tải — spinner riêng, không mượn danh sách của lượt trước. */}
        {state.status === 'loading' && <LoadingState description="Đang tải danh sách vé…" />}

        {/* Gọi API hỏng — trạng thái riêng, có lối thoát, KHÔNG mạo nhận là "chưa có vé nào". */}
        {state.status === 'error' && (
          <ErrorState
            title="Không tải được danh sách vé"
            description={state.message}
            onRetry={() => setReloadKey((n) => n + 1)}
          />
        )}

        {/* Tải xong nhưng không có vé khớp bộ lọc — mảng rỗng là câu trả lời hợp lệ. */}
        {state.status === 'done' && filtered.length === 0 && (
          <EmptyState
            description={
              filter === 'all'
                ? 'Bạn chưa có vé điện tử nào.'
                : 'Không có vé nào ở trạng thái này.'
            }
            action={
              filter === 'all' ? (
                <Button type="primary" icon={<SearchOutlined />} onClick={() => navigate('/route-lookup')}>
                  Tra cứu chuyến
                </Button>
              ) : (
                <Button onClick={() => setFilter('all')}>Xem tất cả vé</Button>
              )
            }
          />
        )}

        {state.status === 'done' && filtered.length > 0 && (
          <List<TicketRow>
            dataSource={filtered}
            renderItem={({ ticket, status }) => (
              <List.Item
                style={{ padding: '16px 0', borderBlockEnd: `1px solid ${token.colorSplit}` }}
              >
                <div
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 16,
                    width: '100%',
                    flexWrap: 'wrap',
                  }}
                >
                  {/* Tuyến + trạng thái + giờ khởi hành + ghế + mã vé */}
                  <div style={{ flex: 1, minWidth: 260 }}>
                    <Space size={8} wrap>
                      <Tag color="blue">{ticket.routeCode}</Tag>
                      <Text strong>{ticket.routeName}</Text>
                      <TicketStatusTag ticket={ticket} />
                    </Space>

                    <div style={{ marginTop: 6 }}>
                      <Text type="secondary" style={{ fontSize: 13 }}>
                        {ticket.origin} → {ticket.destination}
                      </Text>
                    </div>

                    <div>
                      <Text type="secondary" style={{ fontSize: 13 }}>
                        Khởi hành {formatDateTime(ticket.departureTime)} · Ghế {ticket.seatNumber}
                      </Text>
                    </div>

                    <div>
                      <Text type="secondary" style={{ fontSize: 13 }}>
                        Mã vé: <Text code>{ticket.code}</Text>
                      </Text>
                    </div>
                  </div>

                  {/* Mã QR + giá — vé không còn hiệu lực thì QR bôi xám. */}
                  <div
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      alignItems: 'center',
                      gap: 8,
                    }}
                  >
                    <TicketQr code={ticket.code} disabled={!isTicketUsable(status)} />
                    <Text strong style={{ fontSize: 15 }}>
                      {formatVnd(ticket.price)}
                    </Text>
                  </div>
                </div>
              </List.Item>
            )}
          />
        )}
      </PageCard>
    </div>
  );
}
