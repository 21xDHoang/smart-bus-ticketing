import { useEffect, useState } from 'react';
import { Button, Card, Table, Typography, theme } from 'antd';
import type { TableProps } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import fillRateReportApi, { defaultFillRateFilter } from '../api/fillRateReportApi';
import type { FillRateFilterValue, FillRateReport, TripFillRate } from '../api/fillRateReportApi';
import type { AppError } from '../api/axiosClient';
import FillRateFilter from '../components/FillRateFilter';
import { ErrorState, FilterBar, LoadingState, PageCard, PageHeader, RouteBadge } from '../components/ui';
import { PAGE_SIZE_OPTIONS, formatDate, formatDateTime } from '../components/ui/format';

const { Text } = Typography;

/** Kết quả một lượt tải báo cáo — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; report: FillRateReport };

/** Thẻ số liệu — nhãn + giá trị lớn + dòng ghi chú ngưỡng (nếu có). */
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

/** Thanh tỷ lệ lấp đầy — màu theo ngưỡng: đầy (≥80%) xanh, vừa vàng, trống (<50%) đỏ. */
function FillRateBar({ percent }: { percent: number }) {
  const { token } = theme.useToken();
  const color =
    percent >= 80 ? token.colorSuccess : percent >= 50 ? token.colorWarning : token.colorError;

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
            background: color,
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

const COLUMNS: TableProps<TripFillRate>['columns'] = [
  {
    title: 'Khởi hành',
    dataIndex: 'departureTime',
    key: 'departureTime',
    width: 150,
    render: (departureTime: string) => (
      <span style={{ fontWeight: 600 }}>{formatDateTime(departureTime)}</span>
    ),
  },
  {
    title: 'Mã chuyến',
    dataIndex: 'tripCode',
    key: 'tripCode',
    width: 120,
    render: (tripCode: string) => <Text code>{tripCode}</Text>,
  },
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
    title: 'Biển số',
    dataIndex: 'busPlate',
    key: 'busPlate',
    width: 130,
    render: (busPlate: string) => (
      <Text style={{ fontVariantNumeric: 'tabular-nums' }}>{busPlate}</Text>
    ),
  },
  {
    title: 'Sức chứa',
    dataIndex: 'capacity',
    key: 'capacity',
    align: 'right',
    width: 100,
    render: (capacity: number) => `${capacity} ghế`,
  },
  {
    title: 'Vé đã bán',
    dataIndex: 'passengers',
    key: 'passengers',
    align: 'right',
    width: 110,
    render: (passengers: number) => passengers.toLocaleString('vi-VN'),
  },
  {
    title: 'Tỷ lệ lấp đầy',
    dataIndex: 'fillRatePct',
    key: 'fillRatePct',
    width: 200,
    render: (fillRatePct: number) => <FillRateBar percent={fillRatePct} />,
  },
];

// Màn hình "Thống kê tỷ lệ lấp đầy theo chuyến" (US 20 "Tỷ lệ lấp đầy", task của Dương Thị
// Hạnh) — màn quản trị/vận hành, chỉ Admin và Manager vào được (route guard ở App.tsx). Lọc
// theo tuyến + khung giờ + khoảng ngày rồi xem từng chuyến đầy/trống tới đâu: dải thẻ tóm tắt
// + bảng chuyến có thanh màu.
//
// Backend thống kê tỷ lệ lấp đầy chưa có nên màn hình đang chạy dữ liệu giả
// (fillRateReportApi.ts) — cùng lối dashboard doanh thu (revenueDashboardApi.ts).
export default function FillRateReportPage() {
  const [filter, setFilter] = useState<FillRateFilterValue>(() => defaultFillRateFilter());
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    fillRateReportApi
      .getReport(filter)
      .then((report) => {
        if (cancelled) return;
        setState({ status: 'done', report });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({
          status: 'error',
          message: appError.customMessage || 'Không thể tải báo cáo tỷ lệ lấp đầy.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [filter, reloadKey]);

  // Bộ lọc + nút làm mới luôn hiển thị (kể cả lúc đang tải hay lỗi) để người dùng đổi lọc ngay.
  return (
    <div>
      <PageHeader
        title="Tỷ lệ lấp đầy theo chuyến"
        subtitle="Xem từng chuyến đầy/trống tới đâu để điều chỉnh tần suất chạy xe."
      />

      <FilterBar
        actions={
          <Button icon={<ReloadOutlined />} onClick={() => setReloadKey((key) => key + 1)}>
            Làm mới
          </Button>
        }
      >
        <FillRateFilter value={filter} onChange={setFilter} disabled={state.status === 'loading'} />
      </FilterBar>

      {state.status === 'loading' ? (
        <LoadingState description="Đang tải số liệu tỷ lệ lấp đầy…" />
      ) : state.status === 'error' ? (
        <ErrorState
          title="Không tải được báo cáo tỷ lệ lấp đầy"
          description={state.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      ) : (
        <>
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
              label="Tổng chuyến"
              value={state.report.summary.totalTrips.toLocaleString('vi-VN')}
            />
            <StatTile
              label="Lấp đầy trung bình"
              value={`${state.report.summary.averageFillRatePct}%`}
              note="Trung bình khách trên tổng sức chứa ghế"
            />
            <StatTile
              label="Chuyến đầy"
              value={state.report.summary.fullTrips.toLocaleString('vi-VN')}
              note="Lấp đầy ≥ 80% sức chứa"
            />
            <StatTile
              label="Chuyến trống"
              value={state.report.summary.emptyTrips.toLocaleString('vi-VN')}
              note="Lấp đầy dưới 50% sức chứa"
            />
          </div>

          <PageCard
            title={
              state.report.from === state.report.to
                ? `Các chuyến ngày ${formatDate(state.report.from)}`
                : `Các chuyến từ ${formatDate(state.report.from)} đến ${formatDate(state.report.to)}`
            }
            extra={
              <Text type="secondary" style={{ fontSize: 13 }}>
                Xanh: ≥80% · Vàng: 50–79% · Đỏ: &lt;50%
              </Text>
            }
          >
            <Table<TripFillRate>
              rowKey="tripId"
              columns={COLUMNS}
              dataSource={state.report.trips}
              scroll={{ x: 960 }}
              locale={{ emptyText: 'Không có chuyến nào khớp bộ lọc' }}
              // Dữ liệu đã tải hết về client (mock), nên phân trang để AntD tự quản — không dùng
              // tablePagination (dành cho phân trang phía server, cần state `page` riêng).
              pagination={{
                pageSize: 10,
                showSizeChanger: true,
                pageSizeOptions: PAGE_SIZE_OPTIONS,
                showTotal: (total, range) => `Hiển thị ${range[0]}–${range[1]} trên ${total} chuyến`,
              }}
            />
          </PageCard>
        </>
      )}
    </div>
  );
}
