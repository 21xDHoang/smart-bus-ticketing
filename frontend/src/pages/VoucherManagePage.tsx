import { useEffect, useState } from 'react';
import { Button, Input, Popconfirm, Select, Space, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import voucherApi, { VOUCHER_STATUS_META, VOUCHER_STATUS_OPTIONS } from '../api/voucherApi';
import type { Voucher, VoucherPayload, VoucherStatus } from '../api/voucherApi';
import type { AppError } from '../api/axiosClient';
import VoucherFormModal from '../components/VoucherFormModal';
import { FilterBar, PageCard, PageHeader } from '../components/ui';
import { formatVnd } from '../components/ui/format';
import { tablePagination } from '../components/ui/table';

const { Text } = Typography;

/** Giá trị giảm hiển thị gọn — "10%" hoặc "20.000 đ". */
function formatDiscount(voucher: Voucher): string {
  if (voucher.discountType === 'Percent') return `${voucher.discountValue}%`;
  return formatVnd(voucher.discountValue);
}

/** Dòng điều kiện kèm theo — đơn tối thiểu + mức giảm tối đa (nếu có). */
function conditionsText(voucher: Voucher): string {
  const parts: string[] = [];
  if (voucher.minOrderValue > 0) parts.push(`Đơn từ ${formatVnd(voucher.minOrderValue)}`);
  if (voucher.discountType === 'Percent' && voucher.maxDiscount != null) {
    parts.push(`giảm tối đa ${formatVnd(voucher.maxDiscount)}`);
  }
  return parts.length > 0 ? parts.join(' · ') : 'Không kèm điều kiện';
}

// Màn hình quản lý voucher (story 18 "Quản lý Voucher", Sprint 5 — task của Dương Thị Hạnh):
// bảng + tìm kiếm + lọc trạng thái + phân trang, thêm/sửa qua form, bật/tắt áp dụng voucher.
// Backend chưa có bảng Vouchers nên màn hình chạy dữ liệu giả (voucherApi.ts).
export default function VoucherManagePage() {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<VoucherStatus>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [data, setData] = useState<Voucher[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách sau khi thêm/sửa/đổi trạng thái thành công.
  const [reloadKey, setReloadKey] = useState(0);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Voucher | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (voucher: Voucher) => {
    setEditing(voucher);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: VoucherPayload, id?: string) => {
    setSubmitting(true);
    try {
      if (id) {
        await voucherApi.update(id, payload);
        message.success('Đã cập nhật voucher.');
      } else {
        await voucherApi.create(payload);
        message.success('Đã thêm voucher.');
      }
      setModalOpen(false);
      setReloadKey((key) => key + 1);
    } catch (error) {
      const appError = error as AppError;
      message.error(appError.customMessage || 'Thao tác thất bại.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleStatus = async (voucher: Voucher) => {
    const nextStatus: VoucherStatus = voucher.status === 'Active' ? 'Inactive' : 'Active';
    try {
      await voucherApi.updateStatus(voucher.id, nextStatus);
      message.success(
        nextStatus === 'Active' ? 'Đã kích hoạt voucher.' : 'Đã ngừng áp dụng voucher.',
      );
      setReloadKey((key) => key + 1);
    } catch (error) {
      const appError = error as AppError;
      message.error(appError.customMessage || 'Đổi trạng thái thất bại.');
    }
  };

  const columns: TableProps<Voucher>['columns'] = [
    {
      title: 'Mã',
      dataIndex: 'code',
      key: 'code',
      width: 130,
      render: (code: string) => <Tag color="blue">{code}</Tag>,
    },
    {
      title: 'Tên chương trình',
      dataIndex: 'name',
      key: 'name',
      ellipsis: true,
    },
    {
      title: 'Giá trị giảm',
      key: 'discount',
      width: 200,
      render: (_, voucher) => (
        <div>
          <div style={{ fontWeight: 600 }}>{formatDiscount(voucher)}</div>
          <Text type="secondary" style={{ fontSize: 12 }}>
            {conditionsText(voucher)}
          </Text>
        </div>
      ),
    },
    {
      title: 'Lượt dùng',
      key: 'usage',
      width: 120,
      align: 'right',
      render: (_, voucher) => `${voucher.usedCount}/${voucher.quantity}`,
    },
    {
      title: 'Hiệu lực',
      key: 'validity',
      width: 170,
      render: (_, voucher) => (
        <Text type="secondary">
          {dayjs(voucher.validFrom).format('DD/MM')} → {dayjs(voucher.validUntil).format('DD/MM/YYYY')}
        </Text>
      ),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 150,
      render: (value: VoucherStatus) => {
        const meta = VOUCHER_STATUS_META[value];
        return meta ? <Tag color={meta.color}>{meta.label}</Tag> : <Tag>{value}</Tag>;
      },
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 190,
      render: (_, voucher) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(voucher)}>
            Sửa
          </Button>
          {voucher.status === 'Active' ? (
            <Popconfirm
              title="Ngừng áp dụng voucher?"
              description={`Voucher “${voucher.code}” sẽ không còn áp dụng cho lượt đặt mới.`}
              okText="Ngừng"
              cancelText="Huỷ"
              okButtonProps={{ danger: true }}
              onConfirm={() => handleToggleStatus(voucher)}
            >
              <Button type="link" size="small" danger>
                Ngừng áp dụng
              </Button>
            </Popconfirm>
          ) : (
            <Popconfirm
              title="Kích hoạt voucher?"
              description={`Voucher “${voucher.code}” sẽ được áp dụng trở lại.`}
              okText="Kích hoạt"
              cancelText="Huỷ"
              onConfirm={() => handleToggleStatus(voucher)}
            >
              <Button type="link" size="small">
                Kích hoạt
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  useEffect(() => {
    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    voucherApi
      .list({ page, pageSize, search: search || undefined, status })
      .then((result) => {
        if (cancelled) return;
        setData(result.items);
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không thể tải danh sách voucher.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [search, status, page, pageSize, reloadKey]);

  return (
    <div>
      <PageHeader
        title="Quản lý voucher"
        subtitle="Tạo và quản lý mã giảm giá áp dụng cho vé — bật/tắt từng voucher theo chiến dịch khuyến mãi."
      />

      <PageCard
        toolbar={
          <FilterBar
            actions={
              <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
                Thêm voucher
              </Button>
            }
          >
            <Input.Search
              allowClear
              placeholder="Tìm theo mã hoặc tên chương trình"
              style={{ width: 300 }}
              onSearch={(value) => {
                setSearch(value.trim());
                setPage(1);
              }}
              onChange={(e) => {
                // Bấm nút X (allowClear) thì cập nhật ngay, không cần chờ Enter.
                if (!e.target.value) {
                  setSearch('');
                  setPage(1);
                }
              }}
            />

            <Select<VoucherStatus>
              allowClear
              placeholder="Trạng thái"
              style={{ width: 200 }}
              options={VOUCHER_STATUS_OPTIONS}
              onChange={(value) => {
                setStatus(value ?? undefined);
                setPage(1);
              }}
            />
          </FilterBar>
        }
      >
        <Table<Voucher>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          scroll={{ x: 1080 }}
          locale={{ emptyText: 'Không tìm thấy voucher nào' }}
          pagination={tablePagination({
            page,
            pageSize,
            total,
            unitLabel: 'voucher',
            onChange: (nextPage, nextPageSize) => {
              // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
              setPage(nextPageSize !== pageSize ? 1 : nextPage);
              setPageSize(nextPageSize);
            },
          })}
        />
      </PageCard>

      <VoucherFormModal
        open={modalOpen}
        editing={editing}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
}
