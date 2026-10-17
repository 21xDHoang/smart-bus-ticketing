import { useCallback, useEffect, useState } from 'react';
import { Button, Popconfirm, Space, Table, Tag, Typography, message } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import reportTemplateApi, { REPORT_TYPE_META } from '../api/reportTemplateApi';
import type { ReportTemplate, ReportTemplatePayload } from '../api/reportTemplateApi';
import ReportTemplateFormModal from '../components/ReportTemplateFormModal';
import { EmptyState, PageCard, PageHeader } from '../components/ui';
import { formatDate, formatDateTime } from '../components/ui/format';

const { Text } = Typography;

/** Màu thẻ theo loại báo cáo — chỉ để phân biệt bằng mắt, không mang nghĩa trạng thái. */
const REPORT_TYPE_COLOR: Record<string, string> = {
  Revenue: 'blue',
  FillRate: 'green',
  AuditLog: 'purple',
};

/** Nhãn các cột đã chọn — đổi khoá ổn định thành nhãn hiển thị. */
function columnLabels(template: ReportTemplate): string[] {
  const byKey = new Map(REPORT_TYPE_META[template.reportType].columns.map((column) => [column.key, column.label]));
  return template.columns.map((key) => byKey.get(key) ?? key);
}

function rangeText(template: ReportTemplate): string {
  return template.from === template.to
    ? formatDate(template.from)
    : `${formatDate(template.from)} → ${formatDate(template.to)}`;
}

// Màn hình "Cấu hình mẫu báo cáo" (US 21 "Xuất báo cáo Excel/PDF", task của Dương Thị Hạnh) —
// quản trị viên soạn sẵn các MẪU báo cáo: chọn loại báo cáo + cột hiển thị + khoảng thời gian,
// để lúc xuất file chỉ cần lấy đúng mẫu thay vì chọn lại mỗi lần. Cấu hình lưu trong trình duyệt
// (reportTemplateApi.ts) vì chưa có bảng CSDL — xem chú thích đầu file API.
export default function ReportTemplateConfigPage() {
  const [templates, setTemplates] = useState<ReportTemplate[]>([]);
  const [loading, setLoading] = useState(true);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<ReportTemplate | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setTemplates(await reportTemplateApi.list());
    } catch (error) {
      message.error((error as Error).message || 'Không tải được danh sách mẫu báo cáo.');
      setTemplates([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void load();
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (template: ReportTemplate) => {
    setEditing(template);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: ReportTemplatePayload) => {
    setSubmitting(true);
    try {
      if (editing) {
        await reportTemplateApi.update(editing.id, payload);
        message.success('Đã cập nhật mẫu báo cáo.');
      } else {
        await reportTemplateApi.create(payload);
        message.success('Đã thêm mẫu báo cáo.');
      }
      setModalOpen(false);
      await load();
    } catch (error) {
      message.error((error as Error).message || 'Thao tác thất bại.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await reportTemplateApi.remove(id);
      message.success('Đã xoá mẫu báo cáo.');
      await load();
    } catch (error) {
      message.error((error as Error).message || 'Xoá thất bại.');
    }
  };

  const columns: TableProps<ReportTemplate>['columns'] = [
    {
      title: 'Tên mẫu',
      dataIndex: 'name',
      key: 'name',
      render: (name: string) => <Text strong>{name}</Text>,
    },
    {
      title: 'Loại báo cáo',
      key: 'reportType',
      width: 160,
      render: (_, template) => (
        <Tag color={REPORT_TYPE_COLOR[template.reportType]}>
          {REPORT_TYPE_META[template.reportType].label}
        </Tag>
      ),
    },
    {
      title: 'Cột hiển thị',
      key: 'columns',
      render: (_, template) => {
        const labels = columnLabels(template);
        return (
          <Text type={labels.length > 0 ? undefined : 'secondary'}>
            {labels.length > 0 ? labels.join(' · ') : '—'}
          </Text>
        );
      },
    },
    {
      title: 'Khoảng thời gian',
      key: 'range',
      width: 180,
      render: (_, template) => <Text>{rangeText(template)}</Text>,
    },
    {
      title: 'Cập nhật lần cuối',
      key: 'updatedAt',
      width: 160,
      render: (_, template) => {
        const at = template.updatedAt ?? template.createdAt;
        return <Text type="secondary">{formatDateTime(at)}</Text>;
      },
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 120,
      render: (_, template) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(template)}>
            Sửa
          </Button>
          <Popconfirm
            title="Xoá mẫu báo cáo?"
            description={`Mẫu “${template.name}” sẽ bị xoá.`}
            okText="Xoá"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => handleDelete(template.id)}
          >
            <Button type="link" size="small" danger>
              Xoá
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="Mẫu báo cáo"
        subtitle="Soạn sẵn mẫu báo cáo: chọn loại, cột hiển thị và khoảng thời gian để dùng lại khi xuất file."
        extra={
          <Space wrap>
            <Button icon={<ReloadOutlined />} onClick={() => void load()}>
              Làm mới
            </Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
              Thêm mẫu
            </Button>
          </Space>
        }
      />

      <PageCard>
        <Table<ReportTemplate>
          rowKey="id"
          columns={columns}
          dataSource={templates}
          loading={loading}
          pagination={false}
          locale={{
            emptyText: (
              <EmptyState description="Chưa có mẫu báo cáo nào. Bấm “Thêm mẫu” để soạn mẫu đầu tiên." />
            ),
          }}
        />
      </PageCard>

      <ReportTemplateFormModal
        open={modalOpen}
        editing={editing}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
}
