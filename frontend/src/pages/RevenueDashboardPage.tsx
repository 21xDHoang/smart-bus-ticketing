import { useEffect, useState } from 'react';
import { Card, Segmented, Table, Typography, theme } from 'antd';
import type { TableProps } from 'antd';
import { ArrowDownOutlined, ArrowUpOutlined } from '@ant-design/icons';
import revenueDashboardApi from '../api/revenueDashboardApi';
import type { DailyRevenue, RevenueDashboard, RevenueRange, RouteRevenue } from '../api/revenueDashboardApi';
import type { AppError } from '../api/axiosClient';
import RevenueTrendChart from '../components/RevenueTrendChart';
import RevenueByRouteChart from '../components/RevenueByRouteChart';
import { ErrorState, LoadingState, PageCard, PageHeader, RouteBadge } from '../components/ui';
import { formatDate, formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Khoảng thời gian chọn nhanh — khớp tham số `range` dự kiến của endpoint. */
const RANGE_OPTIONS: { value: RevenueRange; label: string }[] = [
  { value: '7d', label: '7 ngày' },
  { value: '30d', label: '30 ngày' },
  { value: '90d', label: '90 ngày' },
];

/** Kết quả một lượt tải dashboard — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; dashboard: RevenueDashboard };

/** Thẻ số liệu — nhãn + giá trị lớn + chênh lệch so với kỳ trước (mũi tên + màu theo chiều tốt/xấu). */
function StatTile({
  label,
  value,
  delta,
  deltaIsUpGood,
  note,
}: {
  label: string;
  value: string;
  delta?: number;
  deltaIsUpGood?: boolean;
  note?: string;
}) {
  const { token } = theme.useToken();

  let deltaColor = token.colorTextSecondary;
  if (delta !== undefined && delta !== 0) {
    const up = delta > 0;
    deltaColor = up === deltaIsUpGood ? token.colorSuccess : token.colorError;
  }

  return (
    <Card variant="borderless" styles={{ body: { padding: 20 } }}>
      <Text type="secondary">{label}</Text>
      <div style={{ fontSize: 28, fontWeight: 700, lineHeight: 1.2, marginTop: 8 }}>{value}</div>
      <div style={{ marginTop: 8, minHeight: 20 }}>
        {delta !== undefined && delta !== 0 ? (
          <span style={{ color: deltaColor, fontWeight: 600, fontSize: 13 }}>
            {delta > 0 ? <ArrowUpOutlined /> : <ArrowDownOutlined />} {Math.abs(delta)}%{' '}
            <Text type="secondary" style={{ fontSize: 13 }}>
              so với kỳ trước
            </Text>
          </span>
        ) : (
          <Text type="secondary" style={{ fontSize: 13 }}>
            {note ?? 'Không đổi so với kỳ trước'}
          </Text>
        )}
      </div>
    </Card>
  );
}

const trendColumns: TableProps<DailyRevenue>['columns'] = [
  { title: 'Ngày', dataIndex: 'date', key: 'date', render: (date: string) => formatDate(date) },
  {
    title: 'Doanh thu',
    dataIndex: 'revenue',
    key: 'revenue',
    align: 'right',
    render: (revenue: number) => (
      <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
        {formatVnd(revenue)}
      </Text>
    ),
  },
  {
    title: 'Vé bán',
    dataIndex: 'tickets',
    key: 'tickets',
    align: 'right',
    render: (tickets: number) => tickets.toLocaleString('vi-VN'),
  },
];

const routeColumns: TableProps<RouteRevenue>['columns'] = [
  {
    title: 'Tuyến',
    dataIndex: 'routeName',
    key: 'routeName',
    render: (_, record) => (
      <>
        <RouteBadge code={record.routeCode} size="sm" style={{ marginRight: 6 }} />
        {record.routeName}
      </>
    ),
  },
  {
    title: 'Doanh thu',
    dataIndex: 'revenue',
    key: 'revenue',
    align: 'right',
    render: (revenue: number) => (
      <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
        {formatVnd(revenue)}
      </Text>
    ),
  },
];

// Màn hình "Dashboard doanh thu (thẻ số liệu + biểu đồ)" (US 19 "Doanh thu", Sprint 5, task
// của Dương Thị Hạnh) — màn quản trị/vận hành, chỉ Admin và Manager vào được (route guard ở
// App.tsx). Tổng quan doanh thu theo khoảng thời gian: dải 4 thẻ số liệu + biểu đồ theo ngày
// và theo tuyến, mỗi biểu đồ có nút chuyển sang dạng bảng cho ai không đọc được màu.
//
// Backend thống kê doanh thu chưa có nên màn hình đang chạy dữ liệu giả (revenueDashboardApi.ts).
export default function RevenueDashboardPage() {
  const [range, setRange] = useState<RevenueRange>('30d');
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  const [trendView, setTrendView] = useState<'chart' | 'table'>('chart');
  const [routeView, setRouteView] = useState<'chart' | 'table'>('chart');

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    revenueDashboardApi
      .getDashboard(range)
      .then((dashboard) => {
        if (cancelled) return;
        setState({ status: 'done', dashboard });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({ status: 'error', message: appError.customMessage || 'Không thể tải dashboard doanh thu.' });
      });

    return () => {
      cancelled = true;
    };
  }, [range, reloadKey]);

  if (state.status === 'loading') {
    return (
      <div>
        <PageHeader title="Dashboard doanh thu" subtitle="Tổng quan doanh thu theo khoảng thời gian." />
        <LoadingState description="Đang tải số liệu doanh thu…" />
      </div>
    );
  }

  if (state.status === 'error') {
    return (
      <div>
        <PageHeader title="Dashboard doanh thu" subtitle="Tổng quan doanh thu theo khoảng thời gian." />
        <ErrorState
          title="Không tải được dashboard doanh thu"
          description={state.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      </div>
    );
  }

  const { summary, byDay, byRoute } = state.dashboard;

  return (
    <div>
      <PageHeader
        title="Dashboard doanh thu"
        subtitle="Tổng quan doanh thu theo khoảng thời gian — mọi số liệu dưới đây cùng khớp một lát dữ liệu."
      />

      {/* Bộ lọc khoảng thời gian — một hàng, nằm trên mọi thẻ và biểu đồ để tất cả cùng đổi theo. */}
      <div style={{ marginBottom: 16 }}>
        <Segmented
          options={RANGE_OPTIONS}
          value={range}
          onChange={(value) => setRange(value as RevenueRange)}
        />
      </div>

      {/* Dải thẻ số liệu — tự co về 2 cột/1 cột khi màn hình hẹp. */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
          gap: 16,
          marginBottom: 16,
        }}
      >
        <StatTile
          label="Tổng doanh thu"
          value={formatVnd(summary.totalRevenue)}
          delta={summary.revenueDeltaPct}
          deltaIsUpGood
        />
        <StatTile
          label="Vé đã bán"
          value={summary.ticketsSold.toLocaleString('vi-VN')}
          delta={summary.ticketsDeltaPct}
          deltaIsUpGood
        />
        <StatTile
          label="Chuyến hoạt động"
          value={summary.tripsCount.toLocaleString('vi-VN')}
          note="Ước tính ~24 khách/chuyến"
        />
        <StatTile
          label="Tiền đã hoàn"
          value={formatVnd(summary.refundAmount)}
          delta={summary.refundDeltaPct}
          deltaIsUpGood={false}
        />
      </div>

      <PageCard
        title="Doanh thu theo ngày"
        extra={
          <Segmented
            size="small"
            options={[
              { value: 'chart', label: 'Biểu đồ' },
              { value: 'table', label: 'Bảng' },
            ]}
            value={trendView}
            onChange={(value) => setTrendView(value as 'chart' | 'table')}
          />
        }
      >
        {trendView === 'chart' ? (
          <RevenueTrendChart data={byDay} />
        ) : (
          <Table<DailyRevenue>
            rowKey="date"
            columns={trendColumns}
            dataSource={byDay}
            pagination={false}
            size="small"
          />
        )}
      </PageCard>

      <div style={{ marginTop: 16 }}>
        <PageCard
          title="Doanh thu theo tuyến"
          extra={
            <Segmented
              size="small"
              options={[
                { value: 'chart', label: 'Biểu đồ' },
                { value: 'table', label: 'Bảng' },
              ]}
              value={routeView}
              onChange={(value) => setRouteView(value as 'chart' | 'table')}
            />
          }
        >
          {routeView === 'chart' ? (
            <RevenueByRouteChart data={byRoute} />
          ) : (
            <Table<RouteRevenue>
              rowKey="routeCode"
              columns={routeColumns}
              dataSource={byRoute}
              pagination={false}
              size="small"
            />
          )}
        </PageCard>
      </div>
    </div>
  );
}
