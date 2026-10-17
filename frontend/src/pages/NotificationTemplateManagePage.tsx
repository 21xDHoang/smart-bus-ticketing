import { useCallback, useEffect, useState } from 'react';
import { Button, Space, Table, Typography, message } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import notificationTemplateApi from '../api/notificationTemplateApi';
import {
  NOTIFICATION_TYPE_META,
  NOTIFICATION_TYPES,
} from '../api/notificationTemplateApi';
import type {
  NotificationTemplate,
  NotificationTemplatePayload,
  NotificationTemplateType,
} from '../api/notificationTemplateApi';
import NotificationTemplateFormModal from '../components/NotificationTemplateFormModal';
import { PageCard, PageHeader, StatusTag } from '../components/ui';
import { formatDateTime } from '../components/ui/format';

/** Một dòng trong bảng: ghép loại (luôn đủ tám) với mẫu đã soạn (có thể null). */
interface Row {
  type: NotificationTemplateType;
  template: NotificationTemplate | null;
}

/** Gom các lỗi theo trường (errors.title/errors.body) thành các câu hiển thị được. */
function errorMessages(error: unknown): string[] {
  const err = error as { customMessage?: string; errors?: Record<string, string[]> };
  const fieldErrors = Object.values(err.errors ?? {}).flat();
  return fieldErrors.length > 0 ? fieldErrors : [err.customMessage || 'Thao tác thất bại.'];
}

function statusOf(template: NotificationTemplate | null) {
  if (!template) return <StatusTag>Chưa soạn</StatusTag>;
  return template.isActive ? (
    <StatusTag color="success">Đang dùng</StatusTag>
  ) : (
    <StatusTag color="warning">Đang tắt</StatusTag>
  );
}

export default function NotificationTemplateManagePage() {
  const [templates, setTemplates] = useState<NotificationTemplate[]>([]);
  const [loading, setLoading] = useState(true);

  const [modalOpen, setModalOpen] = useState(false);
  const [editingType, setEditingType] = useState<NotificationTemplateType>('CancellationApproved');
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setTemplates(await notificationTemplateApi.list());
    } catch (error) {
      message.error((error as Error).message || 'Không tải được danh sách mẫu thông báo.');
      setTemplates([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void load();
  }, [load]);

  const templateByType = new Map(templates.map((template) => [template.type, template]));
  const rows: Row[] = NOTIFICATION_TYPES.map((type) => ({
    type,
    template: templateByType.get(type) ?? null,
  }));

  const openEdit = (type: NotificationTemplateType) => {
    setEditingType(type);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: NotificationTemplatePayload) => {
    setSubmitting(true);
    try {
      await notificationTemplateApi.upsert(editingType, payload);
      message.success('Đã lưu mẫu thông báo.');
      setModalOpen(false);
      await load();
    } catch (error) {
      message.error(errorMessages(error).join(' '));
    } finally {
      setSubmitting(false);
    }
  };

  const columns: TableProps<Row>['columns'] = [
    {
      title: 'Loại thông báo',
      key: 'type',
      width: 280,
      render: (_, row) => {
        const meta = NOTIFICATION_TYPE_META[row.type];
        return (
          <Space direction="vertical" size={0}>
            <Space size={8}>
              <Typography.Text strong>{meta.label}</Typography.Text>
              {statusOf(row.template)}
            </Space>
            <Typography.Text type="secondary">{meta.description}</Typography.Text>
          </Space>
        );
      },
    },
    {
      title: 'Tiêu đề',
      key: 'title',
      render: (_, row) =>
        row.template?.title || <Typography.Text type="secondary">—</Typography.Text>,
    },
    {
      title: 'Nội dung',
      key: 'body',
      render: (_, row) => {
        if (!row.template) {
          return <Typography.Text type="secondary">Dùng câu mặc định</Typography.Text>;
        }
        // Giữ nguyên ký tự xuống dòng; cắt bớt chiều cao để bảng không bị một nội dung dài kéo giãn.
        return (
          <div style={{ whiteSpace: 'pre-wrap', maxHeight: 64, overflow: 'hidden' }}>
            {row.template.body}
          </div>
        );
      },
    },
    {
      title: 'Cập nhật lần cuối',
      key: 'updatedAt',
      width: 160,
      render: (_, row) => {
        const at = row.template?.updatedAt ?? row.template?.createdAt;
        return at ? (
          <Typography.Text type="secondary">{formatDateTime(at)}</Typography.Text>
        ) : (
          <Typography.Text type="secondary">—</Typography.Text>
        );
      },
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 100,
      render: (_, row) => (
        <Button type="link" size="small" onClick={() => openEdit(row.type)}>
          {row.template ? 'Sửa' : 'Soạn'}
        </Button>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="Mẫu thông báo"
        subtitle="Soạn câu chữ cho từng loại thông báo. Mẫu là phần ghi đè — loại chưa soạn hoặc đang tắt sẽ dùng câu mặc định."
        extra={
          <Button icon={<ReloadOutlined />} onClick={() => void load()}>
            Làm mới
          </Button>
        }
      />

      <PageCard>
        <Table<Row>
          rowKey="type"
          columns={columns}
          dataSource={rows}
          loading={loading}
          pagination={false}
          locale={{ emptyText: 'Không có loại thông báo nào' }}
        />
      </PageCard>

      <NotificationTemplateFormModal
        open={modalOpen}
        type={editingType}
        editing={templateByType.get(editingType) ?? null}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
}
