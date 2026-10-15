import { useEffect, useState } from 'react';
import { Button, Card, DatePicker, Flex, Table, Typography, theme } from 'antd';
import type { TableProps } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import fillRateReportApi from '../api/fillRateReportApi';
import type { FillRateReport, TripFillRate } from '../api/fillRateReportApi';
import type { AppError } from '../api/axiosClient';
import { ErrorState, LoadingState, PageCard, PageHeader, RouteBadge } from '../components/ui';
import { PAGE_SIZE_OPTIONS, formatTime } from '../components/ui/format';

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
    title: 'Giờ khởi hành',
    dataIndex: 'departureTime',
    key: 'departureTime',
    width: 120,
    render: (departureTime: string) => (
      <span style={{ fontWeight: 600 }}>{formatTime(departureTime)}</span>
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
// Hạnh) — màn quản trị/vận hành, chỉ Admin và Manager vào được (route guard ở App.tsx). Chọn
// một ngày rồi xem từng chuyến đầy/trống tới đâu: dải thẻ tóm tắt + bảng chuyến có thanh màu.
//
// Backend thống kê tỷ lệ lấp đầy chưa có nên màn hình đang chạy dữ liệu giả
// (fillRateReportApi.ts) — cùng lối dashboard doanh thu (revenueDashboardApi.ts).
export default function FillRateReportPage() {
  const [date, setDate] = useState<Dayjs>(() => dayjs());
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    fillRateReportApi
      .getReport(date.format('YYYY-MM-DD'))
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
  }, [date, reloadKey]);

  if (state.status === 'loading') {
    return (
      <div>
        <PageHeader
          title="Tỷ lệ lấp đầy theo chuyến"
          subtitle="Xem từng chuyến trong ngày đầy/trống tới đâu để điều chỉnh tần suất chạy xe."
        />
        <LoadingState description="Đang tải số liệu tỷ lệ lấp đầy…" />
      </div>
    );
  }

  if (state.status === 'error') {
    return (
      <div>
        <PageHeader
          title="Tỷ lệ lấp đầy theo chuyến"
          subtitle="Xem từng chuyến trong ngày đầy/trống tới đâu để điều chỉnh tần suất chạy xe."
        />
        <ErrorState
          title="Không tải được báo cáo tỷ lệ lấp đầy"
          description={state.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      </div>
    );
  }

  const { summary, trips } = state.report;

  return (
    <div>
      <PageHeader
        title="Tỷ lệ lấp đầy theo chuyến"
        subtitle="Xem từng chuyến trong ngày đầy/trống tới đâu để điều chỉnh tần suất chạy xe."
      />

      {/* Bộ lọc ngày — một hàng, nằm trên mọi thẻ và bảng để tất cả cùng đổi theo. */}
      <Flex align="center" gap={12} wrap style={{ marginBottom: 16 }}>
        <Text type="secondary">Ngày:</Text>
        <DatePicker
          value={date}
          format="DD/MM/YYYY"
          allowClear={false}
          onChange={(value) => setDate(value ?? dayjs())}
        />
        <Button icon={<ReloadOutlined />} onClick={() => setReloadKey((key) => key + 1)}>
          Làm mới
        </Button>
      </Flex>

      {/* Dải thẻ số liệu — tự co về 2 cột/1 cột khi màn hình hẹp. */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
          gap: 16,
          marginBottom: 16,
        }}
      >
        <StatTile label="Tổng chuyến" value={summary.totalTrips.toLocaleString('vi-VN')} />
        <StatTile
          label="Lấp đầy trung bình"
          value={`${summary.averageFillRatePct}%`}
          note="Trung bình khách trên tổng sức chứa ghế"
        />
        <StatTile
          label="Chuyến đầy"
          value={summary.fullTrips.toLocaleString('vi-VN')}
          note="Lấp đầy ≥ 80% sức chứa"
        />
        <StatTile
          label="Chuyến trống"
          value={summary.emptyTrips.toLocaleString('vi-VN')}
          note="Lấp đầy dưới 50% sức chứa"
        />
      </div>

      <PageCard
        title={`Các chuyến ngày ${date.format('DD/MM/YYYY')}`}
        extra={
          <Text type="secondary" style={{ fontSize: 13 }}>
            Xanh: ≥80% · Vàng: 50–79% · Đỏ: &lt;50%
          </Text>
        }
      >
        <Table<TripFillRate>
          rowKey="tripId"
          columns={COLUMNS}
          dataSource={trips}
          scroll={{ x: 920 }}
          locale={{ emptyText: 'Không có chuyến nào trong ngày này' }}
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
    </div>
  );
}
