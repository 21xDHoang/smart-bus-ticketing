import { useEffect, useState } from 'react';
import { Card, Space, Table, Typography, theme } from 'antd';
import type { TableProps } from 'antd';
import overloadAlertApi, { FREQUENT_THRESHOLD_DAYS } from '../api/overloadAlertApi';
import type { OverloadAlertReport, OverloadedTrip } from '../api/overloadAlertApi';
import type { AppError } from '../api/axiosClient';
import { ErrorState, LoadingState, PageCard, PageHeader, RouteBadge, StatusTag } from '../components/ui';
import { PAGE_SIZE_OPTIONS } from '../components/ui/format';

const { Text } = Typography;

/** Kết quả một lượt tải báo cáo — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; report: OverloadAlertReport };

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

/** Tỷ lệ lấp đầy trung bình — đỏ khi quá 100%, vàng khi cận ngưỡng 90–100%. */
function LoadPct({ percent }: { percent: number }) {
  const { token } = theme.useToken();
  const color = percent >= 100 ? token.colorError : percent >= 90 ? token.colorWarning : token.colorText;
  return (
    <Text strong style={{ color, fontVariantNumeric: 'tabular-nums' }}>
      {percent}%
    </Text>
  );
}

/** Số khách cao nhất — kèm "+n" đỏ khi vượt sức chứa ghế. */
function PeakPassengers({ passengers, capacity }: { passengers: number; capacity: number }) {
  const overflow = passengers - capacity;
  return (
    <span style={{ fontVariantNumeric: 'tabular-nums' }}>
      {passengers}
      {overflow > 0 && (
        <Text type="danger" style={{ marginLeft: 6, fontSize: 12 }}>
          +{overflow}
        </Text>
      )}
    </span>
  );
}

/** Mức độ cảnh báo — nặng gấp đôi ngưỡng "thường xuyên" thì gọi là nghiêm trọng. */
function severityOf(overloadedDays: number): { label: string; color: string } {
  return overloadedDays >= FREQUENT_THRESHOLD_DAYS * 2
    ? { label: 'Nghiêm trọng', color: 'error' }
    : { label: 'Thường xuyên', color: 'warning' };
}

// Màn hình "Cảnh báo chuyến quá tải" (US 20 "Tỷ lệ lấp đầy", Sprint 4, task của Nguyễn Đình
// Băng) — màn quản trị/vận hành, chỉ Admin và Manager vào được (route guard ở App.tsx). Gộp
// NHIỀU ngày để bắt những chuyến định kỳ bị quá tải LẶP LẠI, khác với màn "Tỷ lệ lấp đầy theo
// chuyến" (fillRateReportApi.ts) chỉ xem một ngày.
//
// Backend "tỷ lệ lấp đầy / cảnh báo quá tải" chưa có nên màn hình đang chạy dữ liệu giả
// (overloadAlertApi.ts). Khi backend + contract có, chỉ cần lật USE_MOCK_DATA là chạy API thật.
export default function OverloadAlertPage() {
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    overloadAlertApi
      .getReport()
      .then((report) => {
        if (cancelled) return;
        setState({ status: 'done', report });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({
          status: 'error',
          message: appError.customMessage || 'Không thể tải báo cáo cảnh báo chuyến quá tải.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  if (state.status === 'loading') {
    return (
      <div>
        <PageHeader
          title="Cảnh báo chuyến quá tải"
          subtitle="Các chuyến định kỳ bị quá tải lặp lại — để cân nhắc điều chỉnh quy mô xe."
        />
        <LoadingState description="Đang tải danh sách cảnh báo chuyến quá tải…" />
      </div>
    );
  }

  if (state.status === 'error') {
    return (
      <div>
        <PageHeader
          title="Cảnh báo chuyến quá tải"
          subtitle="Các chuyến định kỳ bị quá tải lặp lại — để cân nhắc điều chỉnh quy mô xe."
        />
        <ErrorState
          title="Không tải được báo cáo cảnh báo chuyến quá tải"
          description={state.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      </div>
    );
  }

  const { from, to, periodDays, overloadThresholdPct, items, total } = state.report;
  const maxDays = items[0]?.overloadedDays ?? 0;

  const columns: TableProps<OverloadedTrip>['columns'] = [
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
      title: 'Giờ khởi hành',
      dataIndex: 'departureTime',
      key: 'departureTime',
      width: 120,
      render: (departureTime: string) => (
        <span style={{ fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{departureTime}</span>
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
      title: 'Lấp đầy trung bình',
      dataIndex: 'averageFillRatePct',
      key: 'averageFillRatePct',
      align: 'right',
      width: 150,
      render: (averageFillRatePct: number) => <LoadPct percent={averageFillRatePct} />,
    },
    {
      title: 'Khách cao nhất',
      dataIndex: 'maxPassengers',
      key: 'maxPassengers',
      align: 'right',
      width: 130,
      render: (maxPassengers: number, record) => (
        <PeakPassengers passengers={maxPassengers} capacity={record.capacity} />
      ),
    },
    {
      title: 'Ngày quá tải',
      dataIndex: 'overloadedDays',
      key: 'overloadedDays',
      width: 150,
      render: (overloadedDays: number) => {
        const severity = severityOf(overloadedDays);
        return (
          <Space direction="vertical" size={2}>
            <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
              {overloadedDays}/{periodDays} ngày
            </Text>
            <StatusTag color={severity.color}>{severity.label}</StatusTag>
          </Space>
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title="Cảnh báo chuyến quá tải"
        subtitle="Các chuyến định kỳ bị quá tải lặp lại — để cân nhắc điều chỉnh quy mô xe."
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
        <StatTile label="Chuyến cảnh báo" value={total.toLocaleString('vi-VN')} />
        <StatTile
          label="Ngày theo dõi"
          value={`${periodDays}`}
          note={`Từ ${from} đến ${to}`}
        />
        <StatTile label="Ngưỡng quá tải" value={`≥${overloadThresholdPct}%`} note="Sức chứa ghế" />
        <StatTile label="Quá tải nặng nhất" value={`${maxDays} ngày`} />
      </div>

      <PageCard
        title="Chuyến thường xuyên quá tải"
        extra={
          <Text type="secondary" style={{ fontSize: 13 }}>
            Quá tải = lấp đầy ≥ {overloadThresholdPct}% · thường xuyên = ≥{' '}
            {FREQUENT_THRESHOLD_DAYS} ngày/{periodDays} ngày
          </Text>
        }
      >
        <Table<OverloadedTrip>
          rowKey="tripKey"
          columns={columns}
          dataSource={items}
          scroll={{ x: 860 }}
          locale={{ emptyText: 'Không có chuyến nào thường xuyên quá tải' }}
          // Dữ liệu đã tải hết về client (mock), nên phân trang để AntD tự quản — không dùng
          // tablePagination (dành cho phân trang phía server).
          pagination={{
            pageSize: 10,
            showSizeChanger: true,
            pageSizeOptions: PAGE_SIZE_OPTIONS,
            showTotal: (count, range) => `Hiển thị ${range[0]}–${range[1]} trên ${count} chuyến`,
          }}
        />
      </PageCard>
    </div>
  );
}
