import { useEffect, useState } from 'react';
import { Segmented, Table, Tag, Typography } from 'antd';
import type { TableProps } from 'antd';
import myRefundApi from '../api/myRefundApi';
import type { Refund, RefundStatus } from '../api/refundAdminApi';
import { getRefundSourceMeta, REFUND_STATUS_OPTIONS } from '../api/refundAdminApi';
import type { AppError } from '../api/axiosClient';
import RefundStatusTag from '../components/RefundStatusTag';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader, StatusTag } from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Kết quả một lượt tải danh sách hoàn tiền — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; refunds: Refund[] };

/** Bộ lọc theo trạng thái xử lý — 'all' là xem tất cả. */
type StatusFilter = 'all' | RefundStatus;

/** Các lựa chọn bộ lọc, sinh từ REFUND_STATUS_META để nhãn không lệch với tag trạng thái. */
const STATUS_FILTER_OPTIONS: { value: StatusFilter; label: string }[] = [
  { value: 'all', label: 'Tất cả' },
  ...REFUND_STATUS_OPTIONS,
];

/** Vé / chuyến được hoàn — gộp tuyến + giờ đi + ghế vào một cột để bảng không quá rộng. */
function TripCell({ item }: { item: Refund }) {
  return (
    <div>
      <div style={{ fontWeight: 600 }}>
        {item.routeCode && <Tag style={{ marginRight: 6 }}>{item.routeCode}</Tag>}
        {item.routeName}
      </div>
      <Text type="secondary" style={{ fontSize: 12 }}>
        {formatDateTime(item.departureTime)} · Ghế {item.seatNumber}
      </Text>
      <div>
        <Text type="secondary" style={{ fontSize: 12 }}>
          Mã vé {item.ticketCode}
        </Text>
      </div>
    </div>
  );
}

// Màn hình "Theo dõi trạng thái hoàn tiền của hành khách" (story 8 "Hoàn tiền tự động", Sprint 4,
// task của Dương Thị Hạnh) — danh sách các khoản hoàn tiền của CHÍNH người đang đăng nhập, kèm
// trạng thái xử lý. Đây là màn hình cho HÀNH KHÁCH, chỉ XEM — không có nút thử lại (việc thử lại
// khoản lỗi thuộc màn quản trị "Quản lý hoàn tiền" của Băng). Backend Refunds chưa có nên màn
// hình đang chạy dữ liệu giả (myRefundApi.ts).
export default function MyRefundsPage() {
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    myRefundApi
      .listMine(statusFilter === 'all' ? undefined : statusFilter)
      .then((list) => {
        if (cancelled) return;
        setState({ status: 'done', refunds: list });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({ status: 'error', message: appError.customMessage || 'Không thể tải danh sách hoàn tiền.' });
      });

    return () => {
      cancelled = true;
    };
  }, [statusFilter, reloadKey]);

  const columns: TableProps<Refund>['columns'] = [
    {
      title: 'Mã hoàn tiền',
      dataIndex: 'refundCode',
      key: 'refundCode',
      width: 150,
      render: (refundCode: string) => <Text code>{refundCode}</Text>,
    },
    {
      title: 'Nguồn gốc',
      dataIndex: 'source',
      key: 'source',
      width: 140,
      render: (source: Refund['source']) => {
        const meta = getRefundSourceMeta(source);
        return <StatusTag color={meta.color}>{meta.label}</StatusTag>;
      },
    },
    {
      title: 'Vé / chuyến',
      key: 'trip',
      width: 260,
      render: (_, item) => <TripCell item={item} />,
    },
    {
      title: 'Số tiền hoàn',
      dataIndex: 'amount',
      key: 'amount',
      width: 150,
      align: 'right',
      render: (amount: number) => (
        <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
          {formatVnd(amount)}
        </Text>
      ),
    },
    {
      title: 'Cổng hoàn',
      dataIndex: 'methodName',
      key: 'methodName',
      width: 110,
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 140,
      render: (status: RefundStatus) => <RefundStatusTag status={status} />,
    },
    {
      title: 'Ngày yêu cầu',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 150,
      render: (createdAt: string) => formatDateTime(createdAt),
    },
  ];

  const content = () => {
    if (state.status === 'loading') return <LoadingState description="Đang tải lịch sử hoàn tiền…" />;

    if (state.status === 'error') {
      return (
        <ErrorState
          title="Không tải được lịch sử hoàn tiền"
          description={state.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      );
    }

    if (state.refunds.length === 0) {
      return <EmptyState description="Chưa có khoản hoàn tiền nào" />;
    }

    return (
      <Table<Refund>
        rowKey="id"
        columns={columns}
        dataSource={state.refunds}
        pagination={false}
        scroll={{ x: 1100 }}
        expandable={{
          expandedRowRender: (item) => (
            <div style={{ padding: '4px 8px' }}>
              {item.status === 'Failed' && (
                <div style={{ marginBottom: 8 }}>
                  <Text type="danger" strong>
                    Lý do chưa hoàn: {item.failReason ?? 'Không rõ lý do.'}
                  </Text>
                </div>
              )}

              {item.processedAt && (
                <div style={{ marginBottom: 8 }}>
                  <Text strong>Xử lý xong lúc: </Text>
                  <Text>{formatDateTime(item.processedAt)}</Text>
                </div>
              )}

              <Text type="secondary" style={{ display: 'block', fontSize: 12 }}>
                Gửi lúc {formatDateTime(item.createdAt)}
                {item.updatedAt ? ` · Cập nhật ${formatDateTime(item.updatedAt)}` : ''}
              </Text>

              {item.status === 'Failed' && (
                <Text type="secondary" style={{ display: 'block', fontSize: 12, marginTop: 8 }}>
                  Hệ thống sẽ tự động thử lại khoản hoàn này — bạn không cần làm gì thêm.
                </Text>
              )}
            </div>
          ),
        }}
      />
    );
  };

  return (
    <div>
      <PageHeader
        title="Hoàn tiền của tôi"
        subtitle="Theo dõi trạng thái các khoản hoàn tiền của bạn và kết quả xử lý từ hệ thống."
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
