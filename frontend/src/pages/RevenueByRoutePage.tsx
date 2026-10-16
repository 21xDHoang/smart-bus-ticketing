import { useEffect, useState } from 'react';
import { Card, Table, Typography, theme } from 'antd';
import type { TableProps } from 'antd';
import revenueByRouteApi from '../api/revenueByRouteApi';
import type {
  RouteRevenueRow,
  RouteRevenueSortField,
  RouteRevenueSortOrder,
} from '../api/revenueByRouteApi';
import type { AppError } from '../api/axiosClient';
import { ErrorState, LoadingState, PageCard, PageHeader, RouteBadge } from '../components/ui';
import { formatVnd } from '../components/ui/format';
import { tablePagination } from '../components/ui/table';

const { Text } = Typography;

/** Kết quả một lượt tải bảng — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | {
      status: 'done';
      items: RouteRevenueRow[];
      total: number;
      totalRevenue: number;
      totalPayments: number;
    };

/** Thẻ số liệu — nhãn + giá trị lớn + dòng ghi chú (nếu có). */
function StatTile({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <Card variant="borderless" styles={{ body: { padding: 20 } }}>
      <Text type="secondary">{label}</Text>
      <div style={{ fontSize: 28, fontWeight: 700, lineHeight: 1.2, marginTop: 8 }}>{value}</div>
      {note && (
        <div style={{ marginTop: 8, minHeight: 20 }}>
          <Text type="secondary" style={{ fontSize: 13 }}>
            {note}
          </Text>
        </div>
      )}
    </Card>
  );
}

/** Thanh tỉ trọng doanh thu của tuyến so với tổng toàn kỳ. */
function ShareBar({ percent }: { percent: number }) {
  const { token } = theme.useToken();

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 160 }}>
      <div
        style={{
          flex: 1,
          height: 8,
          borderRadius: 4,
          background: token.colorFillTertiary,
          overflow: 'hidden',
        }}
      >
        <div
          style={{
            width: `${Math.min(100, percent)}%`,
            height: '100%',
            background: token.colorPrimary,
            borderRadius: 4,
          }}
        />
      </div>
      <Text strong style={{ fontVariantNumeric: 'tabular-nums', width: 52, textAlign: 'right' }}>
        {percent}%
      </Text>
    </div>
  );
}

// Màn hình "Doanh thu theo tuyến" (US 19 "Doanh thu", Sprint 4, task của Nguyễn Đình Băng) —
// màn quản trị/vận hành, chỉ Admin và Manager vào được (route guard ở App.tsx). Bảng chi tiết
// doanh thu bán vé gom theo từng tuyến, sắp xếp và phân trang phía server (đúng hợp đồng dự
// kiến GET /api/reports/revenue/by-route).
//
// Backend "theo tuyến" chưa có nên màn hình đang chạy dữ liệu giả (revenueByRouteApi.ts) —
// cùng lối dashboard doanh thu (revenueDashboardApi.ts). Khi backend + contract có, chỉ cần
// lật USE_MOCK_DATA trong revenueByRouteApi.ts là bảng chạy API thật.
export default function RevenueByRoutePage() {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [sortBy, setSortBy] = useState<RouteRevenueSortField>('revenue');
  const [sortOrder, setSortOrder] = useState<RouteRevenueSortOrder>('descend');
  const [reloadKey, setReloadKey] = useState(0);
  const [state, setState] = useState<LoadState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    revenueByRouteApi
      .getByRoute({ page, pageSize, sortBy, sortOrder })
      .then((result) => {
        if (cancelled) return;
        setState({ status: 'done', ...result });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({
          status: 'error',
          message: appError.customMessage || 'Không thể tải bảng doanh thu theo tuyến.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [page, pageSize, sortBy, sortOrder, reloadKey]);

  // Sắp xếp phía server: cột báo `sorter: true` để AntD không tự sắp, ta giữ `sortOrder` có
  // điều khiển rồi xử lý trong `onChange`. Mặc định doanh thu giảm dần — tuyến thu nhiều nhất
  // nằm trên cùng.
  const handleTableChange: TableProps<RouteRevenueRow>['onChange'] = (
    _pagination,
    _filters,
    sorter,
    extra,
  ) => {
    if (extra.action !== 'sort') return; // phân trang do tablePagination lo

    const single = Array.isArray(sorter) ? sorter[0] : sorter;
    const key = single?.columnKey;
    const order = single?.order;
    const field = typeof key === 'string' ? (key as RouteRevenueSortField) : undefined;

    if (field && (order === 'ascend' || order === 'descend')) {
      setSortBy(field);
      setSortOrder(order);
    } else {
      // Bấm lần ba để bỏ sắp xếp → về mặc định thay vì để bảng không thứ tự.
      setSortBy('revenue');
      setSortOrder('descend');
    }
    setPage(1);
  };

  const columns: TableProps<RouteRevenueRow>['columns'] = [
    {
      title: 'Tuyến',
      dataIndex: 'routeName',
      key: 'routeName',
      sorter: true,
      sortOrder: sortBy === 'routeName' ? sortOrder : null,
      render: (_, record) => (
        <>
          <RouteBadge code={record.routeCode} size="sm" style={{ marginRight: 6 }} />
          {record.routeName}
        </>
      ),
    },
    {
      title: 'Số giao dịch',
      dataIndex: 'payments',
      key: 'payments',
      width: 140,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'payments' ? sortOrder : null,
      render: (payments: number) => payments.toLocaleString('vi-VN'),
    },
    {
      title: 'Doanh thu',
      dataIndex: 'revenue',
      key: 'revenue',
      width: 180,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'revenue' ? sortOrder : null,
      render: (revenue: number) => (
        <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
          {formatVnd(revenue)}
        </Text>
      ),
    },
    {
      title: 'Tỉ trọng',
      dataIndex: 'sharePct',
      key: 'sharePct',
      width: 200,
      render: (sharePct: number) => <ShareBar percent={sharePct} />,
    },
  ];

  if (state.status === 'loading') {
    return (
      <div>
        <PageHeader
          title="Doanh thu theo tuyến"
          subtitle="Bảng chi tiết doanh thu bán vé gom theo từng tuyến xe."
        />
        <LoadingState description="Đang tải bảng doanh thu theo tuyến…" />
      </div>
    );
  }

  if (state.status === 'error') {
    return (
      <div>
        <PageHeader
          title="Doanh thu theo tuyến"
          subtitle="Bảng chi tiết doanh thu bán vé gom theo từng tuyến xe."
        />
        <ErrorState
          title="Không tải được bảng doanh thu theo tuyến"
          description={state.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      </div>
    );
  }

  const { items, total, totalRevenue, totalPayments } = state;

  return (
    <div>
      <PageHeader
        title="Doanh thu theo tuyến"
        subtitle="Bảng chi tiết doanh thu bán vé gom theo từng tuyến xe."
      />

      {/* Dải thẻ số liệu — tổng toàn kỳ, không phụ thuộc trang đang xem. */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
          gap: 16,
          marginBottom: 16,
        }}
      >
        <StatTile label="Tổng doanh thu" value={formatVnd(totalRevenue)} />
        <StatTile
          label="Tổng giao dịch"
          value={totalPayments.toLocaleString('vi-VN')}
          note="Chỉ giao dịch thanh toán thành công"
        />
        <StatTile label="Số tuyến" value={total.toLocaleString('vi-VN')} />
      </div>

      <PageCard
        title="Chi tiết theo tuyến"
        extra={
          <Text type="secondary" style={{ fontSize: 13 }}>
            Bấm vào tiêu đề cột để sắp xếp · doanh thu mặc định giảm dần
          </Text>
        }
      >
        <Table<RouteRevenueRow>
          rowKey="routeId"
          columns={columns}
          dataSource={items}
          onChange={handleTableChange}
          scroll={{ x: 760 }}
          locale={{ emptyText: 'Không có dữ liệu doanh thu theo tuyến nào' }}
          pagination={tablePagination({
            page,
            pageSize,
            total,
            unitLabel: 'tuyến',
            onChange: (nextPage, nextPageSize) => {
              // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
              setPage(nextPageSize !== pageSize ? 1 : nextPage);
              setPageSize(nextPageSize);
            },
          })}
        />
      </PageCard>
    </div>
  );
}
