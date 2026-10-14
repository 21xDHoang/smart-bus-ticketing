import { useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Descriptions,
  Modal,
  Space,
  Table,
  Typography,
  message,
} from 'antd';
import type { TableProps } from 'antd';
import { ExportOutlined, EyeOutlined, RedoOutlined } from '@ant-design/icons';
import refundAdminApi from '../api/refundAdminApi';
import type {
  Refund,
  RefundFilterValue,
  RefundListParams,
} from '../api/refundAdminApi';
import { getRefundSourceMeta } from '../api/refundAdminApi';
import type { AppError } from '../api/axiosClient';
import RefundStatusTag from '../components/RefundStatusTag';
import RefundFilter from '../components/RefundFilter';
import { downloadRefundsCsv } from '../components/refundExport';
import { PageCard, PageHeader, RouteBadge, StatusTag } from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';
import { tablePagination } from '../components/ui/table';

const { Text } = Typography;

/**
 * Dòng "Vé / chuyến" trong danh sách hoàn tiền.
 *
 * Hợp đồng dự kiến của `/admin/refunds` trả sẵn `routeCode`/`routeName`/`departureTime` (khác
 * `/admin/feedbacks` chỉ trả `tripId`), nên hiển thị ngay tại đây, không phải tra thêm.
 */
function TripCell({ item }: { item: Refund }) {
  return (
    <div>
      <div style={{ fontWeight: 600 }}>
        <RouteBadge code={item.routeCode} size="sm" style={{ marginRight: 6 }} />
        {item.routeName}
      </div>
      <Text type="secondary" style={{ fontSize: 12 }}>
        {formatDateTime(item.departureTime)} · {item.origin} → {item.destination}
      </Text>
    </div>
  );
}

// Màn hình "Admin quản lý yêu cầu hoàn tiền" (story 8 "Hoàn tiền tự động", Sprint 4, task
// của Nguyễn Đình Băng) — màn hình quản trị, chỉ Admin và Manager vào được (route guard ở
// App.tsx, khớp nhóm `/admin/refunds` dự kiến).
//
// Việc của màn hình: xem mọi khoản hoàn tiền của hệ thống, lọc theo trạng thái/nguồn gốc,
// thử lại khoản đang thất bại và xuất danh sách ra CSV. Khác màn "theo dõi trạng thái hoàn
// tiền" của hành khách (task của Hạnh): ở đây nhà xe thấy khoản hoàn của MỌI người, và là
// chỗ duy nhất được phép can thiệp (thử lại) khi lượt hoàn lỗi.
export default function AdminRefundPage() {
  const [filter, setFilter] = useState<RefundFilterValue>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [items, setItems] = useState<Refund[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);

  // Đổi số này là chạy lại truy vấn đang xem — dùng sau khi "Thử lại" để bảng lấy đúng
  // trạng thái mới từ kho dữ liệu, không còn là con số cũ.
  const [reloadToken, setReloadToken] = useState(0);

  // Khoản hoàn đang xem chi tiết; null = modal đóng.
  const [selected, setSelected] = useState<Refund | null>(null);

  // id đang được thử lại — để nút "Thử lại" quay vòng đúng chỗ chứ không quay cả bảng.
  const [retryingId, setRetryingId] = useState<string | null>(null);

  const [exporting, setExporting] = useState(false);

  const hasFilter = filter.status !== undefined || filter.source !== undefined;

  useEffect(() => {
    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API nên tắt cảnh báo
    // react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);

    const params: RefundListParams = { page, pageSize };
    if (filter.status) params.status = filter.status;
    if (filter.source) params.source = filter.source;

    refundAdminApi
      .list(params)
      .then((result) => {
        if (cancelled) return;
        setItems(result.items);
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        message.error((err as AppError).customMessage || 'Không thể tải danh sách hoàn tiền.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [filter, page, pageSize, reloadToken]);

  const openDetail = (item: Refund) => {
    setSelected(item);
  };

  const closeDetail = () => {
    setSelected(null);
  };

  const handleRetry = async (id: string) => {
    setRetryingId(id);
    try {
      const updated = await refundAdminApi.retry(id);

      // Cập nhật modal đang mở (nếu đang xem đúng khoản này) và báo bảng tải lại từ kho.
      setSelected((current) => (current?.id === updated.id ? updated : current));
      setReloadToken((token) => token + 1);
      message.success(`Đã gửi lại yêu cầu hoàn ${updated.refundCode} về trạng thái chờ xử lý.`);
    } catch (err: unknown) {
      message.error((err as AppError).customMessage || 'Không thử lại được khoản hoàn tiền.');
    } finally {
      setRetryingId(null);
    }
  };

  const handleExport = async () => {
    setExporting(true);
    try {
      const list = await refundAdminApi.exportList({ status: filter.status, source: filter.source });
      downloadRefundsCsv(list);
      message.success(`Đã xuất ${list.length} khoản hoàn tiền.`);
    } catch (err: unknown) {
      message.error((err as AppError).customMessage || 'Không xuất được danh sách hoàn tiền.');
    } finally {
      setExporting(false);
    }
  };

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
      title: 'Hành khách',
      dataIndex: 'passengerName',
      key: 'passengerName',
      width: 170,
      render: (passengerName: string) => <span style={{ fontWeight: 600 }}>{passengerName}</span>,
    },
    {
      title: 'Vé / chuyến',
      key: 'trip',
      width: 300,
      render: (_, record) => <TripCell item={record} />,
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
      render: (status: Refund['status']) => <RefundStatusTag status={status} />,
    },
    {
      title: 'Tạo lúc',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 150,
      render: (createdAt: string) => formatDateTime(createdAt),
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 170,
      render: (_, record) => (
        <Space size={4}>
          <Button
            type="link"
            size="small"
            icon={<EyeOutlined />}
            onClick={() => openDetail(record)}
          >
            Chi tiết
          </Button>
          {record.status === 'Failed' && (
            <Button
              type="link"
              size="small"
              icon={<RedoOutlined />}
              loading={retryingId === record.id}
              onClick={() => void handleRetry(record.id)}
            >
              Thử lại
            </Button>
          )}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="Quản lý hoàn tiền"
        subtitle="Theo dõi các khoản hoàn tiền của hệ thống, thử lại lượt thất bại và xuất danh sách để đối soát."
      />

      <PageCard
        toolbar={
          <RefundFilter
            value={filter}
            disabled={loading}
            onChange={(next) => {
              // Đổi bộ lọc luôn quay về trang 1 để không đứng ở trang không còn tồn tại.
              setFilter(next);
              setPage(1);
            }}
            extra={
              <Button
                icon={<ExportOutlined />}
                loading={exporting}
                onClick={() => void handleExport()}
              >
                Xuất danh sách
              </Button>
            }
          />
        }
      >
        <Table<Refund>
          rowKey="id"
          columns={columns}
          dataSource={items}
          loading={loading}
          scroll={{ x: 1400 }}
          locale={{
            emptyText: hasFilter ? 'Không có khoản hoàn nào khớp bộ lọc' : 'Chưa có khoản hoàn nào',
          }}
          pagination={tablePagination({
            page,
            pageSize,
            total,
            unitLabel: 'khoản hoàn',
            onChange: (nextPage, nextPageSize) => {
              // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
              setPage(nextPageSize !== pageSize ? 1 : nextPage);
              setPageSize(nextPageSize);
            },
          })}
        />
      </PageCard>

      <Modal
        title={`Chi tiết hoàn tiền ${selected?.refundCode ?? ''}`}
        open={selected !== null}
        onCancel={closeDetail}
        footer={null}
        width={680}
      >
        {selected && (
          <>
            <Descriptions
              column={1}
              bordered
              size="small"
              items={[
                {
                  key: 'source',
                  label: 'Nguồn gốc',
                  children: (
                    <StatusTag color={getRefundSourceMeta(selected.source).color}>
                      {getRefundSourceMeta(selected.source).label}
                    </StatusTag>
                  ),
                },
                {
                  key: 'passenger',
                  label: 'Hành khách',
                  children: selected.passengerName,
                },
                {
                  key: 'ticket',
                  label: 'Vé / chuyến',
                  children: <TripCell item={selected} />,
                },
                {
                  key: 'seat',
                  label: 'Số ghế',
                  children: selected.seatNumber,
                },
                {
                  key: 'amount',
                  label: 'Số tiền hoàn',
                  children: (
                    <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
                      {formatVnd(selected.amount)}
                    </Text>
                  ),
                },
                {
                  key: 'method',
                  label: 'Cổng hoàn',
                  children: selected.methodName,
                },
                {
                  key: 'payment',
                  label: 'Mã giao dịch gốc',
                  children: <Text code>{selected.paymentCode}</Text>,
                },
                {
                  key: 'gateway',
                  label: 'Mã giao dịch cổng',
                  children: selected.gatewayTransactionId ? (
                    <Text code>{selected.gatewayTransactionId}</Text>
                  ) : (
                    <Text type="secondary">Chưa có</Text>
                  ),
                },
                {
                  key: 'status',
                  label: 'Trạng thái',
                  children: <RefundStatusTag status={selected.status} />,
                },
                {
                  key: 'retryCount',
                  label: 'Số lần thử lại',
                  children: selected.retryCount,
                },
                {
                  key: 'createdAt',
                  label: 'Tạo lúc',
                  children: formatDateTime(selected.createdAt),
                },
                {
                  key: 'processedAt',
                  label: 'Xử lý xong lúc',
                  children: selected.processedAt ? (
                    formatDateTime(selected.processedAt)
                  ) : (
                    <Text type="secondary">Chưa xử lý xong</Text>
                  ),
                },
                ...(selected.failReason
                  ? [
                      {
                        key: 'failReason',
                        label: 'Lý do thất bại',
                        children: <Text type="danger">{selected.failReason}</Text>,
                      },
                    ]
                  : []),
              ]}
            />

            {selected.status === 'Failed' && (
              <div style={{ marginTop: 16 }}>
                <Alert
                  type="warning"
                  showIcon
                  message="Lượt hoàn này đang thất bại."
                  description="Bạn có thể thử lại để hệ thống gọi cổng thanh toán một lần nữa."
                  action={
                    <Button
                      size="small"
                      icon={<RedoOutlined />}
                      loading={retryingId === selected.id}
                      onClick={() => void handleRetry(selected.id)}
                    >
                      Thử lại
                    </Button>
                  }
                />
              </div>
            )}
          </>
        )}
      </Modal>
    </div>
  );
}
