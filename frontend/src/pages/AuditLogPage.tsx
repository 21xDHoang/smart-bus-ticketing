import { useEffect, useState } from 'react';
import { Button, Card, Space, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { EyeOutlined } from '@ant-design/icons';
import { fetchAuditLogs } from '../api/auditLogListApi';
import type { AuditLogListItem } from '../api/auditLogListApi';
import { getAuditActionMeta } from '../api/auditLogApi';
import type { AppError } from '../api/axiosClient';
import AuditLogFilter from '../components/AuditLogFilter';
import { defaultAuditLogFilter } from '../components/auditLogFilterValue';
import type { AuditLogFilterValue } from '../components/auditLogFilterValue';
import AuditLogDetailModal from '../components/AuditLogDetailModal';

const { Title, Text } = Typography;

// Tách chuỗi target dạng "<Tên bảng>:<Id>" để hiển thị tách bạch tên bảng và id.
// Login/Logout không có target → trả null.
function splitTarget(target: string | null): { table: string; id: string } | null {
  if (!target) return null;
  const separator = target.indexOf(':');
  if (separator === -1) return { table: target, id: '' };
  return { table: target.slice(0, separator), id: target.slice(separator + 1) };
}

// Màn hình xem nhật ký hoạt động (story 23) — chỉ Admin vào được (route guard nằm ở App.tsx).
// Giữ state lọc + phân trang, gọi GET /audit-logs rồi vẽ bảng; bộ lọc tái dùng component của
// Thịnh (`AuditLogFilter`), chi tiết tái dùng modal của Băng (`AuditLogDetailModal`).
const AuditLogPage = () => {
  // Bộ lọc khởi tạo là khoảng 30 ngày gần nhất — đúng mặc định backend trả khi bỏ trống.
  const [filter, setFilter] = useState<AuditLogFilterValue>(() => defaultAuditLogFilter());
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [data, setData] = useState<AuditLogListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);

  // Bản ghi đang xem chi tiết; null = modal đóng.
  const [selectedLog, setSelectedLog] = useState<AuditLogListItem | null>(null);

  useEffect(() => {
    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    fetchAuditLogs({ page, pageSize, ...filter })
      .then((result) => {
        if (cancelled) return;
        setData(result.items);
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không thể tải nhật ký hoạt động.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [filter, page, pageSize]);

  const columns: TableProps<AuditLogListItem>['columns'] = [
    {
      title: 'Thời gian',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 180,
      render: (createdAt: string) => dayjs(createdAt).format('DD/MM/YYYY HH:mm:ss'),
    },
    {
      title: 'Người thao tác',
      key: 'user',
      width: 220,
      render: (_, record) => {
        if (!record.userFullName) return <Text type="secondary">Không xác định</Text>;
        return (
          <div>
            <div style={{ fontWeight: 600 }}>{record.userFullName}</div>
            {record.userPhoneNumber && (
              <Text type="secondary" style={{ fontSize: 12 }}>
                {record.userPhoneNumber}
              </Text>
            )}
          </div>
        );
      },
    },
    {
      title: 'Hành động',
      dataIndex: 'action',
      key: 'action',
      width: 150,
      render: (action: string) => {
        const meta = getAuditActionMeta(action);
        return <Tag color={meta.color}>{meta.label}</Tag>;
      },
    },
    {
      title: 'Đối tượng',
      dataIndex: 'target',
      key: 'target',
      render: (target: string | null) => {
        const split = splitTarget(target);
        if (!split) return <Text type="secondary">—</Text>;
        return (
          <Space size={4}>
            <Tag color="blue">{split.table}</Tag>
            {split.id && <Text code>{split.id}</Text>}
          </Space>
        );
      },
    },
    {
      title: 'Địa chỉ IP',
      dataIndex: 'ipAddress',
      key: 'ipAddress',
      width: 150,
      render: (ipAddress: string | null) =>
        ipAddress ? <Text code>{ipAddress}</Text> : <Text type="secondary">Không xác định</Text>,
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 100,
      render: (_, record) => (
        <Button
          type="link"
          size="small"
          icon={<EyeOutlined />}
          onClick={() => setSelectedLog(record)}
        >
          Chi tiết
        </Button>
      ),
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Nhật ký hoạt động
        </Title>
        <Text type="secondary">
          Theo dõi truy cập và thao tác hệ thống để phục vụ công tác kiểm toán và an ninh.
        </Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <AuditLogFilter
          value={filter}
          disabled={loading}
          onChange={(next) => {
            // Đổi bộ lọc luôn quay về trang 1 để không đứng ở trang không còn tồn tại.
            setFilter(next);
            setPage(1);
          }}
        />

        <Table<AuditLogListItem>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          scroll={{ x: 980 }}
          locale={{ emptyText: 'Không tìm thấy bản ghi nhật ký nào' }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            showQuickJumper: true,
            pageSizeOptions: [10, 20, 50],
            showTotal: (t, range) => `Hiển thị ${range[0]}–${range[1]} trên ${t} bản ghi`,
            onChange: (nextPage, nextPageSize) => {
              // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
              setPage(nextPageSize !== pageSize ? 1 : nextPage);
              setPageSize(nextPageSize);
            },
          }}
        />
      </Card>

      <AuditLogDetailModal
        open={selectedLog !== null}
        log={selectedLog}
        onClose={() => setSelectedLog(null)}
      />
    </div>
  );
};

export default AuditLogPage;
