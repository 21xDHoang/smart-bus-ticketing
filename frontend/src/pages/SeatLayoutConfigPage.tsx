import { useEffect, useState } from 'react';
import { Button, Input, Popconfirm, Space, Table, Tag, message } from 'antd';
import type { TableProps } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { AppError } from '../api/axiosClient';
import seatLayoutApi from '../api/seatLayoutApi';
import type { SeatLayout, SeatLayoutPayload } from '../api/seatLayoutApi';
import { layoutGridConfig, vipSeatCount } from '../api/seatLayoutApi';
import SeatLayoutFormModal from '../components/SeatLayoutFormModal';
import SeatMap from '../components/SeatMap';
import { FilterBar, PageCard, PageHeader } from '../components/ui';
import { formatDateTime } from '../components/ui/format';

// Màn hình Admin cấu hình sơ đồ ghế theo loại xe (US 2, Sprint 3, task của Dương Thị Hạnh):
// danh sách các sơ đồ ghế theo loại xe + thêm/sửa/xoá. Mỗi sơ đồ là một "khuôn" gồm số tầng,
// số ghế và ghế VIP — chưa gắn vào từng xe cụ thể (bảng SeatLayouts thuộc migration Sprint 3
// của Dăm, backend chưa có nên màn hình chạy dữ liệu giả).
const BASE_COLUMNS: TableProps<SeatLayout>['columns'] = [
  {
    title: 'Loại xe',
    dataIndex: 'busType',
    key: 'busType',
    width: 240,
    render: (busType: string) => <span style={{ fontWeight: 600 }}>{busType}</span>,
  },
  {
    title: 'Số tầng',
    dataIndex: 'numberOfFloors',
    key: 'numberOfFloors',
    width: 100,
    render: (numberOfFloors: number) => `${numberOfFloors} tầng`,
  },
  {
    title: 'Số ghế',
    dataIndex: 'totalSeats',
    key: 'totalSeats',
    width: 110,
    render: (totalSeats: number) => `${totalSeats} ghế`,
  },
  {
    title: 'Ghế VIP',
    key: 'vip',
    width: 160,
    render: (_, layout) =>
      vipSeatCount(layout) === 0 ? (
        <Tag>Không có</Tag>
      ) : (
        <Tag color="gold">{vipSeatCount(layout)} ghế VIP</Tag>
      ),
  },
  {
    title: 'Cập nhật lần cuối',
    key: 'updatedAt',
    width: 160,
    render: (_, layout) => formatDateTime(layout.updatedAt ?? layout.createdAt),
  },
];

const SeatLayoutConfigPage = () => {
  const [search, setSearch] = useState('');
  const [layouts, setLayouts] = useState<SeatLayout[]>([]);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách sau khi thêm/sửa/xoá thành công.
  const [reloadKey, setReloadKey] = useState(0);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<SeatLayout | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    seatLayoutApi
      .list(search || undefined)
      .then((result) => {
        if (cancelled) return;
        setLayouts(result);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không tải được danh sách sơ đồ ghế.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [search, reloadKey]);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (layout: SeatLayout) => {
    setEditing(layout);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: SeatLayoutPayload, id?: string) => {
    setSubmitting(true);
    try {
      if (id) {
        await seatLayoutApi.update(id, payload);
        message.success('Đã cập nhật sơ đồ ghế.');
      } else {
        await seatLayoutApi.create(payload);
        message.success('Đã thêm sơ đồ ghế.');
      }
      setModalOpen(false);
      setReloadKey((key) => key + 1);
    } catch (error) {
      message.error((error as AppError).customMessage || 'Thao tác thất bại.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await seatLayoutApi.remove(id);
      message.success('Đã xoá sơ đồ ghế.');
      setReloadKey((key) => key + 1);
    } catch (error) {
      message.error((error as AppError).customMessage || 'Xoá thất bại.');
    }
  };

  // Cột "Hành động" cần gọi openEdit/handleDelete nên ghép ở đây, không để ở module.
  const columns: TableProps<SeatLayout>['columns'] = [
    ...BASE_COLUMNS,
    {
      title: 'Hành động',
      key: 'actions',
      width: 140,
      render: (_, layout) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(layout)}>
            Sửa
          </Button>
          <Popconfirm
            title="Xoá sơ đồ ghế?"
            description={`Sơ đồ “${layout.busType}” sẽ bị xoá khỏi danh sách.`}
            okText="Xoá"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => handleDelete(layout.id)}
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
        title="Cấu hình sơ đồ ghế"
        subtitle="Định nghĩa sơ đồ ghế theo loại xe — số tầng, số ghế và ghế VIP."
      />

      <PageCard
        toolbar={
          <FilterBar
            actions={
              <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
                Thêm sơ đồ
              </Button>
            }
          >
            <Input.Search
              allowClear
              placeholder="Tìm theo loại xe"
              style={{ width: 280 }}
              onSearch={(value) => {
                setSearch(value.trim());
              }}
              onChange={(e) => {
                // Bấm nút X (allowClear) thì cập nhật ngay, không cần chờ Enter.
                if (!e.target.value) {
                  setSearch('');
                }
              }}
            />
          </FilterBar>
        }
      >
        <Table<SeatLayout>
          rowKey="id"
          columns={columns}
          dataSource={layouts}
          loading={loading}
          pagination={false}
          locale={{ emptyText: 'Chưa có sơ đồ ghế nào' }}
          expandable={{
            expandedRowRender: (layout) => {
              const config = layoutGridConfig(layout);
              return (
                <SeatMap
                  floors={layout.numberOfFloors}
                  rowsPerFloor={config.rowsPerFloor}
                  columnsPerRow={config.columnsPerRow}
                  vipKeys={config.vipKeys}
                />
              );
            },
          }}
        />
      </PageCard>

      <SeatLayoutFormModal
        open={modalOpen}
        editing={editing}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
};

export default SeatLayoutConfigPage;
