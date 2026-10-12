import { useEffect, useState } from 'react';
import { Button, Input, Popconfirm, Space, Table, message } from 'antd';
import type { TableProps } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { AppError } from '../api/axiosClient';
import seatLayoutApi from '../api/seatLayoutApi';
import type { SeatLayout, SeatLayoutPayload } from '../api/seatLayoutApi';
import { layoutGridConfig, vipSeatCount } from '../api/seatLayoutApi';
import SeatLayoutFormModal from '../components/SeatLayoutFormModal';
import SeatMap from '../components/SeatMap';
import { FilterBar, PageCard, PageHeader, StatusTag } from '../components/ui';
import { formatDateTime } from '../components/ui/format';

// Màn hình Admin cấu hình sơ đồ ghế theo loại xe (US 2, Sprint 3, task của Dương Thị Hạnh):
// danh sách các sơ đồ ghế theo loại xe + thêm/sửa/xoá. Mỗi sơ đồ là một "khuôn" gồm số tầng,
// số ghế và ghế VIP. Lưu sơ đồ là backend sinh luôn ghế thật cho mọi xe cùng loại đang thiếu
// ghế (mục "Lưu sơ đồ là ghế có ngay" của docs/api-contract.md) — nên thông báo thành công
// đọc `busesSeated` để nói rõ đã sinh ghế cho mấy xe.

/** Câu thông báo kèm số xe vừa được sinh ghế — `busesSeated` chỉ có nghĩa ở response POST/PUT. */
const successMessage = (dongTu: string, busesSeated: number) =>
  busesSeated > 0 ? `${dongTu} và sinh ghế cho ${busesSeated} xe.` : `${dongTu}.`;
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
        <StatusTag>Không có</StatusTag>
      ) : (
        <StatusTag color="gold">{vipSeatCount(layout)} ghế VIP</StatusTag>
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
        // Body sửa cố ý KHÔNG gửi busType: loại xe là khoá nghiệp vụ của sơ đồ, backend bỏ
        // qua trường này khi sửa (đổi khoá là xoá-rồi-tạo) — xem seatLayoutApi.update.
        const saved = await seatLayoutApi.update(id, {
          numberOfFloors: payload.numberOfFloors,
          rowsPerFloor: payload.rowsPerFloor,
          columnsPerRow: payload.columnsPerRow,
          vipSeatPositions: payload.vipSeatPositions,
        });
        message.success(successMessage('Đã cập nhật sơ đồ ghế', saved.busesSeated));
      } else {
        const saved = await seatLayoutApi.create(payload);
        message.success(successMessage('Đã thêm sơ đồ ghế', saved.busesSeated));
      }
      setModalOpen(false);
      setReloadKey((key) => key + 1);
    } catch (error) {
      const appError = error as AppError;
      // Có lỗi theo từng trường thì để modal gắn vào ô input; ngược lại mới hiện toast.
      if (!appError.errors) {
        message.error(appError.customMessage || 'Thao tác thất bại.');
      }
      // Ném lại để modal biết lượt lưu thất bại — không thì nó đóng như thành công và lỗi
      // errors.busType (trùng loại xe) của backend biến mất. Cùng lối BusManagePage.
      throw error;
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
