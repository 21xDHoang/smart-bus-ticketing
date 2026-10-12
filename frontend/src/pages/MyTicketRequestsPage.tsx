import { useEffect, useState } from 'react';
import { Segmented, Table, Tag, Typography } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import ticketRequestApi from '../api/ticketRequestApi';
import type { TicketRequest, TicketRequestStatus, TicketRequestType } from '../api/ticketRequestApi';
import {
  getTicketRequestTypeMeta,
  TICKET_REQUEST_STATUS_OPTIONS,
} from '../api/ticketRequestApi';
import type { AppError } from '../api/axiosClient';
import TicketRequestStatusTag from '../components/TicketRequestStatusTag';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader } from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Kết quả một lượt tải danh sách yêu cầu — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; requests: TicketRequest[] };

/** Bộ lọc theo trạng thái xử lý — 'all' là xem tất cả. */
type StatusFilter = 'all' | TicketRequestStatus;

/** Các lựa chọn bộ lọc, sinh từ TICKET_REQUEST_STATUS_META để nhãn không lệch với tag trạng thái. */
const STATUS_FILTER_OPTIONS: { value: StatusFilter; label: string }[] = [
  { value: 'all', label: 'Tất cả' },
  ...TICKET_REQUEST_STATUS_OPTIONS,
];

/** Tiền tóm tắt một dòng — trả dòng chính + dòng phụ để cột "Số tiền" vẽ gọn, không lặp lại. */
function moneySummary(request: TicketRequest): { primary: string; secondary: string | null } {
  if (request.type === 'Cancel') {
    const refund = request.refundAmount ?? 0;
    return { primary: `Hoàn lại ${formatVnd(refund)}`, secondary: `Phí hủy ${formatVnd(request.fee)}` };
  }

  if (request.fee > 0) return { primary: `Trả thêm ${formatVnd(request.fee)}`, secondary: null };
  if (request.fee < 0) return { primary: `Hoàn lại ${formatVnd(-request.fee)}`, secondary: null };
  return { primary: 'Không chênh lệch', secondary: null };
}

/** Chuyến mới của một yêu cầu đổi chuyến — null với yêu cầu hủy vé. */
function NewTripCell({ request }: { request: TicketRequest }) {
  if (request.type !== 'Change' || !request.newRouteName) {
    return <Text type="secondary">—</Text>;
  }

  return (
    <div>
      <div style={{ fontWeight: 600 }}>
        {request.newRouteCode && <Tag style={{ marginRight: 6 }}>{request.newRouteCode}</Tag>}
        {request.newRouteName}
      </div>
      {request.newDepartureTime && (
        <Text type="secondary" style={{ fontSize: 12 }}>
          {formatDateTime(request.newDepartureTime)}
          {request.newSeatNumber ? ` · Ghế ${request.newSeatNumber}` : ''}
        </Text>
      )}
    </div>
  );
}

// Màn hình "Hủy/đổi vé của tôi" (story 5, Sprint 4 — task "Màn hình theo dõi trạng thái yêu
// cầu hủy - đổi" của Nguyễn Đình Băng): danh sách yêu cầu hủy vé / đổi chuyến của chính người
// đang đăng nhập, kèm trạng thái xử lý của nhà xe. Đây là màn hình cho HÀNH KHÁCH — không giới
// hạn vai trò như các màn hình quản trị. Backend TicketChanges / CancellationRequests chưa có
// nên màn hình đang chạy dữ liệu giả (ticketRequestApi.ts).
export default function MyTicketRequestsPage() {
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    ticketRequestApi
      .listMine(statusFilter === 'all' ? undefined : statusFilter)
      .then((list) => {
        if (cancelled) return;
        setState({ status: 'done', requests: list });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({ status: 'error', message: appError.customMessage || 'Không thể tải danh sách yêu cầu.' });
      });

    return () => {
      cancelled = true;
    };
  }, [statusFilter, reloadKey]);

  const columns: TableProps<TicketRequest>['columns'] = [
    {
      title: 'Ngày gửi',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 150,
      render: (createdAt: string) => formatDateTime(createdAt),
    },
    {
      title: 'Loại',
      dataIndex: 'type',
      key: 'type',
      width: 120,
      render: (type: TicketRequestType) => {
        const meta = getTicketRequestTypeMeta(type);
        return <Tag color={meta.color}>{meta.label}</Tag>;
      },
    },
    {
      title: 'Vé gốc',
      key: 'original',
      render: (_, request) => (
        <div>
          <div style={{ fontWeight: 600 }}>
            {request.routeCode && <Tag style={{ marginRight: 6 }}>{request.routeCode}</Tag>}
            {request.routeName}
          </div>
          <Text type="secondary" style={{ fontSize: 12 }}>
            {dayjs(request.departureTime).format('HH:mm DD/MM/YYYY')} · Ghế {request.seatNumber}
          </Text>
          <div>
            <Text type="secondary" style={{ fontSize: 12 }}>
              Mã vé {request.ticketCode}
            </Text>
          </div>
        </div>
      ),
    },
    {
      title: 'Chuyến mới',
      key: 'newTrip',
      width: 260,
      render: (_, request) => <NewTripCell request={request} />,
    },
    {
      title: 'Số tiền',
      key: 'money',
      width: 180,
      render: (_, request) => {
        const { primary, secondary } = moneySummary(request);
        return (
          <div>
            <div style={{ fontWeight: 600 }}>{primary}</div>
            {secondary && (
              <Text type="secondary" style={{ fontSize: 12 }}>
                {secondary}
              </Text>
            )}
          </div>
        );
      },
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 130,
      render: (status: TicketRequestStatus) => <TicketRequestStatusTag status={status} />,
    },
  ];

  const content = () => {
    if (state.status === 'loading') return <LoadingState description="Đang tải yêu cầu hủy/đổi…" />;

    if (state.status === 'error') {
      return (
        <ErrorState
          title="Không tải được danh sách yêu cầu"
          description={state.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      );
    }

    if (state.requests.length === 0) {
      return (
        <EmptyState description="Chưa có yêu cầu hủy/đổi vé nào" />
      );
    }

    return (
      <Table<TicketRequest>
        rowKey="id"
        columns={columns}
        dataSource={state.requests}
        pagination={false}
        scroll={{ x: 1050 }}
        expandable={{
          expandedRowRender: (request) => (
            <div style={{ padding: '4px 8px' }}>
              {request.reason && (
                <div style={{ marginBottom: 8 }}>
                  <Text strong>Lý do gửi: </Text>
                  <Text>{request.reason}</Text>
                </div>
              )}

              {request.reviewNote && (
                <div style={{ marginBottom: 8 }}>
                  <Text strong>Phản hồi của nhà xe: </Text>
                  <Text>{request.reviewNote}</Text>
                </div>
              )}

              <Text type="secondary" style={{ display: 'block', fontSize: 12 }}>
                Gửi lúc {formatDateTime(request.createdAt)}
                {request.updatedAt ? ` · Cập nhật ${formatDateTime(request.updatedAt)}` : ''}
              </Text>
            </div>
          ),
        }}
      />
    );
  };

  return (
    <div>
      <PageHeader
        title="Hủy/đổi vé của tôi"
        subtitle="Theo dõi các yêu cầu hủy vé hoặc đổi chuyến bạn đã gửi và trạng thái xử lý của nhà xe."
      />

      <PageCard
        toolbar={
          <Segmented
            options={STATUS_FILTER_OPTIONS}
            value={statusFilter}
            onChange={(value) => setStatusFilter(value as StatusFilter)}
          />
        }
      >
        {content()}
      </PageCard>
    </div>
  );
}
