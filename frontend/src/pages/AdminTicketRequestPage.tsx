import { useEffect, useState } from 'react';
import { Button, Descriptions, Input, Modal, Segmented, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import { CheckOutlined, CloseOutlined, EyeOutlined, ToolOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import ticketRequestAdminApi from '../api/ticketRequestAdminApi';
import type { AdminTicketRequest } from '../api/ticketRequestAdminApi';
import type { TicketRequestStatus, TicketRequestType } from '../api/ticketRequestApi';
import {
  getTicketRequestTypeMeta,
  TICKET_REQUEST_STATUS_OPTIONS,
} from '../api/ticketRequestApi';
import type { AppError } from '../api/axiosClient';
import TicketRequestStatusTag from '../components/TicketRequestStatusTag';
import {
  EmptyState,
  ErrorState,
  FilterBar,
  LoadingState,
  PageCard,
  PageHeader,
  RouteBadge,
  StatusTag,
} from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Số ký tự tối đa của lý do từ chối — khớp cột `RejectReason` (text) không có trần nghiệp vụ, đặt trần UI vừa phải. */
const REJECT_REASON_MAX_LENGTH = 500;

/** Kết quả một lượt tải hàng đợi — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; requests: AdminTicketRequest[] };

/** Bộ lọc theo trạng thái xử lý — 'all' là xem tất cả; mặc định là 'Pending' vì đây là màn duyệt. */
type StatusFilter = 'all' | TicketRequestStatus;

/** Các lựa chọn bộ lọc, sinh từ TICKET_REQUEST_STATUS_META để nhãn không lệch với tag trạng thái. */
const STATUS_FILTER_OPTIONS: { value: StatusFilter; label: string }[] = [
  { value: 'all', label: 'Tất cả' },
  ...TICKET_REQUEST_STATUS_OPTIONS,
];

/** Tiền tóm tắt một dòng — trả dòng chính + dòng phụ để cột "Số tiền" vẽ gọn, không lặp lại. */
function moneySummary(request: AdminTicketRequest): { primary: string; secondary: string | null } {
  if (request.type === 'Cancel') {
    const refund = request.refundAmount ?? 0;
    return { primary: `Hoàn lại ${formatVnd(refund)}`, secondary: `Phí hủy ${formatVnd(request.fee)}` };
  }

  if (request.fee > 0) return { primary: `Trả thêm ${formatVnd(request.fee)}`, secondary: null };
  if (request.fee < 0) return { primary: `Hoàn lại ${formatVnd(-request.fee)}`, secondary: null };
  return { primary: 'Không chênh lệch', secondary: null };
}

/** Chuyến mới của một yêu cầu đổi chuyến — null với yêu cầu hủy vé. */
function NewTripCell({ request }: { request: AdminTicketRequest }) {
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

/** Vé gốc — dòng đầy đủ mã vé + chuyến + ghế, đặt chung một cột để bảng không quá rộng. */
function OriginalTicketCell({ request }: { request: AdminTicketRequest }) {
  return (
    <div>
      <div style={{ fontWeight: 600 }}>
        {request.routeCode && <RouteBadge code={request.routeCode} size="sm" style={{ marginRight: 6 }} />}
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
  );
}

// Màn hình "Duyệt yêu cầu huỷ / đổi vé" (story 5, Sprint 4 — task "Màn hình Admin duyệt yêu
// cầu huỷ - đổi vé" của Dương Thị Hạnh): hàng đợi các yêu cầu huỷ vé / đổi chuyến của hành
// khách cho Admin/Manager xem và duyệt hoặc từ chối. Backend PATCH/GET huỷ/đổi chưa có nên
// màn hình đang chạy dữ liệu giả (ticketRequestAdminApi.ts).
//
// Ba điểm hợp đồng định hình giao diện (docs/32-csdl-huy-doi-ve.md):
//   1. MỘT màn cho CẢ HAI loại — hai bảng `CancellationRequests` và `TicketChanges` cố ý dùng
//      chung tên cột + trùng tên giá trị trạng thái, nên bảng này gộp hai hàng đợi về một, cột
//      `type` phân biệt.
//   2. Từ chối BẮT BUỘC có lý do — `RejectReason` là thứ gửi về cho khách, để trống là thông
//      báo cụt (docs/33 §"…Rejected").
//   3. Không hứa số tiền — duyệt chỉ lật trạng thái; tiền hoàn/trả thêm đi qua Refunds/Payments
//      ở các story khác, nên màn hình chỉ hiển thị, không tính hay hứa gì thêm.
export default function AdminTicketRequestPage() {
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('Pending');
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  // Yêu cầu đang xem trong modal; null = đóng. Giữ cả bản ghi (không chỉ id) vì list() đã trả
  // đủ trường — không cần gọi thêm chi tiết như màn phản ánh (list chỉ có replyCount).
  const [selected, setSelected] = useState<AdminTicketRequest | null>(null);
  const [acting, setActing] = useState(false);

  // Modal từ chối riêng — tách khỏi modal chi tiết để ô nhập lý do không trộn vào luồng duyệt.
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [rejecting, setRejecting] = useState(false);

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    ticketRequestAdminApi
      .list(statusFilter === 'all' ? undefined : statusFilter)
      .then((list) => {
        if (cancelled) return;
        setState({ status: 'done', requests: list });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({ status: 'error', message: appError.customMessage || 'Không thể tải hàng đợi duyệt.' });
      });

    return () => {
      cancelled = true;
    };
  }, [statusFilter, reloadKey]);

  const openDetail = (request: AdminTicketRequest) => setSelected(request);

  const closeDetail = () => {
    if (acting) return;
    setSelected(null);
  };

  const handleApprove = async () => {
    if (!selected) return;

    setActing(true);
    try {
      await ticketRequestAdminApi.approve(selected.id, selected.type);
      setSelected(null);
      setReloadKey((key) => key + 1);
      message.success('Đã duyệt yêu cầu.');
    } catch (err: unknown) {
      message.error((err as AppError).customMessage || 'Không duyệt được yêu cầu.');
    } finally {
      setActing(false);
    }
  };

  const openReject = () => {
    setRejectReason('');
    setRejectOpen(true);
  };

  const closeReject = () => {
    if (rejecting) return;
    setRejectOpen(false);
    setRejectReason('');
  };

  const handleReject = async () => {
    if (!selected) return;

    const reason = rejectReason.trim();
    if (!reason) {
      message.warning('Vui lòng nhập lý do từ chối.');
      return;
    }

    setRejecting(true);
    try {
      await ticketRequestAdminApi.reject(selected.id, selected.type, reason);
      setRejectOpen(false);
      setRejectReason('');
      setSelected(null);
      setReloadKey((key) => key + 1);
      message.success('Đã từ chối yêu cầu.');
    } catch (err: unknown) {
      message.error((err as AppError).customMessage || 'Không từ chối được yêu cầu.');
    } finally {
      setRejecting(false);
    }
  };

  const columns: TableProps<AdminTicketRequest>['columns'] = [
    {
      title: 'Ngày gửi',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 140,
      render: (createdAt: string) => formatDateTime(createdAt),
    },
    {
      title: 'Hành khách',
      dataIndex: 'passengerName',
      key: 'passengerName',
      width: 150,
      render: (passengerName: string) => (
        <span style={{ fontWeight: 600 }}>{passengerName}</span>
      ),
    },
    {
      title: 'Loại',
      dataIndex: 'type',
      key: 'type',
      width: 110,
      render: (type: TicketRequestType) => {
        const meta = getTicketRequestTypeMeta(type);
        return <StatusTag color={meta.color}>{meta.label}</StatusTag>;
      },
    },
    {
      title: 'Vé gốc',
      key: 'original',
      width: 250,
      render: (_, request) => <OriginalTicketCell request={request} />,
    },
    {
      title: 'Chuyến mới',
      key: 'newTrip',
      width: 240,
      render: (_, request) => <NewTripCell request={request} />,
    },
    {
      title: 'Số tiền',
      key: 'money',
      width: 170,
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
    {
      title: 'Thao tác',
      key: 'actions',
      width: 110,
      render: (_, request) => (
        <Button
          type="link"
          size="small"
          icon={request.status === 'Pending' ? <ToolOutlined /> : <EyeOutlined />}
          onClick={() => openDetail(request)}
        >
          {request.status === 'Pending' ? 'Xử lý' : 'Xem'}
        </Button>
      ),
    },
  ];

  const content = () => {
    if (state.status === 'loading') return <LoadingState description="Đang tải hàng đợi duyệt…" />;

    if (state.status === 'error') {
      return (
        <ErrorState
          title="Không tải được hàng đợi duyệt"
          description={state.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      );
    }

    if (state.requests.length === 0) {
      const description =
        statusFilter === 'Pending'
          ? 'Không còn yêu cầu nào đang chờ duyệt.'
          : 'Không có yêu cầu nào khớp bộ lọc.';
      return <EmptyState description={description} />;
    }

    return (
      <Table<AdminTicketRequest>
        rowKey="id"
        columns={columns}
        dataSource={state.requests}
        pagination={false}
        scroll={{ x: 1300 }}
      />
    );
  };

  return (
    <div>
      <PageHeader
        title="Duyệt yêu cầu huỷ / đổi vé"
        subtitle="Xem và duyệt (hoặc từ chối) các yêu cầu huỷ vé, đổi chuyến của hành khách."
      />

      <PageCard
        toolbar={
          <FilterBar
            actions={
              statusFilter !== 'Pending' ? (
                <Button type="link" size="small" onClick={() => setStatusFilter('Pending')}>
                  Về hàng đợi chờ
                </Button>
              ) : undefined
            }
          >
            <Segmented
              options={STATUS_FILTER_OPTIONS}
              value={statusFilter}
              onChange={(value) => setStatusFilter(value as StatusFilter)}
            />
          </FilterBar>
        }
      >
        {content()}
      </PageCard>

      <Modal
        title="Xử lý yêu cầu"
        open={selected !== null}
        onCancel={closeDetail}
        width={680}
        footer={
          selected?.status === 'Pending'
            ? [
                <Button key="reject" danger icon={<CloseOutlined />} onClick={openReject} disabled={acting}>
                  Từ chối
                </Button>,
                <Button
                  key="approve"
                  type="primary"
                  icon={<CheckOutlined />}
                  loading={acting}
                  onClick={() => void handleApprove()}
                >
                  Duyệt
                </Button>,
              ]
            : null
        }
      >
        {selected && (
          <>
            <Descriptions
              column={2}
              bordered
              size="small"
              items={[
                {
                  key: 'passenger',
                  label: 'Hành khách',
                  children: selected.passengerName,
                },
                {
                  key: 'type',
                  label: 'Loại yêu cầu',
                  children: (
                    <StatusTag color={getTicketRequestTypeMeta(selected.type).color}>
                      {getTicketRequestTypeMeta(selected.type).label}
                    </StatusTag>
                  ),
                },
                {
                  key: 'status',
                  label: 'Trạng thái',
                  children: <TicketRequestStatusTag status={selected.status} />,
                },
                {
                  key: 'createdAt',
                  label: 'Gửi lúc',
                  children: formatDateTime(selected.createdAt),
                },
              ]}
            />

            <div style={{ marginTop: 16 }}>
              <Text strong>Vé gốc</Text>
              <div style={{ marginTop: 4 }}>
                <OriginalTicketCell request={selected} />
              </div>
              <div style={{ marginTop: 4 }}>
                <Text type="secondary">Giá vé đã thanh toán: </Text>
                <Text strong>{formatVnd(selected.price)}</Text>
              </div>
            </div>

            {selected.type === 'Change' && (
              <div style={{ marginTop: 16 }}>
                <Text strong>Chuyến muốn đổi sang</Text>
                <div style={{ marginTop: 4 }}>
                  <NewTripCell request={selected} />
                </div>
                <div style={{ marginTop: 4 }}>
                  <Text type="secondary">Chênh lệch giá: </Text>
                  <Text strong>{moneySummary(selected).primary}</Text>
                </div>
              </div>
            )}

            {selected.type === 'Cancel' && (
              <div style={{ marginTop: 16 }}>
                <Text type="secondary">Phí huỷ: </Text>
                <Text strong>{formatVnd(selected.fee)}</Text>
                <Text type="secondary" style={{ marginLeft: 12 }}>
                  Số tiền hoàn lại: {formatVnd(selected.refundAmount ?? 0)}
                </Text>
              </div>
            )}

            <div style={{ marginTop: 16 }}>
              <Text strong>Lý do khách gửi</Text>
              <div style={{ marginTop: 4 }}>
                {selected.reason ? (
                  <Text style={{ whiteSpace: 'pre-wrap' }}>{selected.reason}</Text>
                ) : (
                  <Text type="secondary">Khách không nêu lý do.</Text>
                )}
              </div>
            </div>

            {selected.status !== 'Pending' && (
              <div style={{ marginTop: 16 }}>
                <Text strong>Ghi chú nhà xe</Text>
                <div style={{ marginTop: 4 }}>
                  {selected.reviewNote ? (
                    <Text style={{ whiteSpace: 'pre-wrap' }}>{selected.reviewNote}</Text>
                  ) : (
                    <Text type="secondary">—</Text>
                  )}
                </div>
                {selected.updatedAt && (
                  <Text type="secondary" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
                    Cập nhật {formatDateTime(selected.updatedAt)}
                  </Text>
                )}
              </div>
            )}
          </>
        )}
      </Modal>

      <Modal
        title="Từ chối yêu cầu"
        open={rejectOpen}
        onCancel={closeReject}
        onOk={() => void handleReject()}
        okText="Xác nhận từ chối"
        okButtonProps={{ danger: true, loading: rejecting }}
        cancelButtonProps={{ disabled: rejecting }}
      >
        <Text type="secondary">
          Lý do từ chối sẽ gửi về cho hành khách. Vui lòng nêu rõ vì sao không duyệt.
        </Text>
        <Input.TextArea
          rows={4}
          style={{ marginTop: 12 }}
          maxLength={REJECT_REASON_MAX_LENGTH}
          showCount
          autoFocus
          value={rejectReason}
          disabled={rejecting}
          placeholder="Ví dụ: Chuyến khởi hành dưới 2 giờ nên không đủ điều kiện hủy."
          onChange={(event) => setRejectReason(event.target.value)}
        />
      </Modal>
    </div>
  );
}
