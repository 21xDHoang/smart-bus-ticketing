import { useCallback, useEffect, useState } from 'react';
import {
  Button,
  Col,
  Descriptions,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Row,
  Select,
  Space,
  Statistic,
  Table,
  Tabs,
  Typography,
  message,
} from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import {
  MONTHLY_PASS_STORED_STATUS_OPTIONS,
  createPassType,
  deletePassType,
  fetchMonthlyPass,
  fetchMonthlyPassStatistics,
  fetchMonthlyPasses,
  fetchPassTypes,
  fetchRouteOptions,
  updateMonthlyPassStatus,
  updatePassType,
} from '../api/monthlyPassAdminApi';
import type {
  AdminMonthlyPass,
  MonthlyPassStatistics,
  MonthlyPassStoredStatus,
  PassTypeRecord,
  RouteOption,
} from '../api/monthlyPassAdminApi';
import type { AppError } from '../api/axiosClient';
import { FilterBar, PageCard, PageHeader, StatusTag } from '../components/ui';
import { formatDate, formatDateTime, formatVnd } from '../components/ui/format';
import { tablePagination } from '../components/ui/table';

/** Bốn nhóm hiệu lực THẬT của một vé — dùng cho bảng và hộp chi tiết. */
type EffectiveStatus = 'Active' | 'Upcoming' | 'Expired' | 'Cancelled';

/**
 * Trạng thái hiệu lực suy từ cặp mốc [validFrom, validTo], giống cách backend tính thống kê.
 * Cột `status` của bản ghi chỉ có độ chính xác cao với Cancelled; Active/Expired lệch nhịp vì
 * job quét hết hạn chạy nền. Vì vậy trên màn này hiển thị "trạng thái hiệu lực" chứ không
 * hiển thị thẳng cột `status`.
 */
function effectiveStatusOf(pass: AdminMonthlyPass): EffectiveStatus {
  if (pass.status === 'Cancelled') return 'Cancelled';

  const now = Date.now();
  if (new Date(pass.validFrom).getTime() > now) return 'Upcoming';
  if (new Date(pass.validTo).getTime() < now) return 'Expired';
  return 'Active';
}

const EFFECTIVE_STATUS_META: Record<EffectiveStatus, { label: string; color?: string }> = {
  Active: { label: 'Đang hiệu lực', color: 'success' },
  Upcoming: { label: 'Sắp hiệu lực', color: 'processing' },
  Expired: { label: 'Đã hết hạn', color: 'error' },
  Cancelled: { label: 'Đã huỷ' },
};

/** Câu lỗi ưu tiên lỗi theo trường (backend trả), rồi mới tới thông báo chung. */
function errorText(error: unknown): string {
  const appError = error as AppError;
  const fieldErrors = Object.values(appError.errors ?? {}).flat();
  return fieldErrors.length > 0
    ? fieldErrors.join(' ')
    : appError.customMessage || 'Thao tác thất bại.';
}

/** Giá trị của form loại vé — InputNumber có thể trả null khi ô trống. */
interface PassTypeFormValues {
  code?: string;
  name?: string;
  durationMonths?: number;
  price?: number;
}

export default function MonthlyPassManagePage() {
  // ── Vé tháng ────────────────────────────────────────────────────────────────────────────
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<MonthlyPassStoredStatus>();
  const [routeId, setRouteId] = useState<string>();
  const [passTypeCode, setPassTypeCode] = useState<string>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  /** Tăng lên sau mỗi thao tác ghi để cả bảng vé lẫn bảng loại vé tự tải lại. */
  const [refreshKey, setRefreshKey] = useState(0);

  const [passes, setPasses] = useState<AdminMonthlyPass[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);

  const [stats, setStats] = useState<MonthlyPassStatistics | null>(null);
  const [statsLoading, setStatsLoading] = useState(false);

  const [detailOpen, setDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detail, setDetail] = useState<AdminMonthlyPass | null>(null);

  // ── Bộ chọn lọc ─────────────────────────────────────────────────────────────────────────
  const [routeOptions, setRouteOptions] = useState<RouteOption[]>([]);
  const [passTypes, setPassTypes] = useState<PassTypeRecord[]>([]);
  const [passTypeLoading, setPassTypeLoading] = useState(false);

  // ── Loại vé ─────────────────────────────────────────────────────────────────────────────
  const [passTypeModalOpen, setPassTypeModalOpen] = useState(false);
  const [editingPassType, setEditingPassType] = useState<PassTypeRecord | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm<PassTypeFormValues>();

  const loadStatistics = useCallback(async () => {
    setStatsLoading(true);
    try {
      setStats(await fetchMonthlyPassStatistics());
    } catch (error) {
      message.error(errorText(error));
    } finally {
      setStatsLoading(false);
    }
  }, []);

  const loadPassTypes = useCallback(async () => {
    setPassTypeLoading(true);
    try {
      setPassTypes(await fetchPassTypes());
    } catch (error) {
      message.error(errorText(error));
    } finally {
      setPassTypeLoading(false);
    }
  }, []);

  const reload = useCallback(() => {
    setRefreshKey((key) => key + 1);
    void loadStatistics();
  }, [loadStatistics]);

  // Danh sách vé — tải lại mỗi khi bộ lọc/trang đổi hoặc có thao tác ghi.
  useEffect(() => {
    let cancelled = false;

    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    fetchMonthlyPasses({
      page,
      pageSize,
      search: search || undefined,
      status,
      routeId,
      passTypeCode,
    })
      .then((result) => {
        if (cancelled) return;
        setPasses(result.items);
        setTotal(result.total);
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        message.error(errorText(error));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [page, pageSize, search, status, routeId, passTypeCode, refreshKey]);

  // Thống kê + danh mục loại vé + danh sách tuyến: tải một lần, và tải lại loại vé sau thao tác.
  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadStatistics();
  }, [loadStatistics]);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadPassTypes();
  }, [loadPassTypes, refreshKey]);

  useEffect(() => {
    fetchRouteOptions()
      .then(setRouteOptions)
      .catch(() => {
        // Ô lọc tuyến chỉ là tiện ích — hỏng thì vẫn dùng được các bộ lọc còn lại.
        setRouteOptions([]);
      });
  }, []);

  // ── Thao tác trên vé ────────────────────────────────────────────────────────────────────

  const openDetail = async (pass: AdminMonthlyPass) => {
    setDetailOpen(true);
    setDetail(null);
    setDetailLoading(true);
    try {
      // Gọi đúng endpoint chi tiết thay vì dùng dòng đang có: dữ liệu trong bảng có thể đã cũ
      // sau khi người khác huỷ vé ở phiên khác.
      setDetail(await fetchMonthlyPass(pass.id));
    } catch (error) {
      message.error(errorText(error));
      setDetailOpen(false);
    } finally {
      setDetailLoading(false);
    }
  };

  const changeStatus = async (pass: AdminMonthlyPass, target: 'Active' | 'Cancelled') => {
    try {
      await updateMonthlyPassStatus(pass.id, target);
      message.success(target === 'Cancelled' ? 'Đã huỷ vé tháng.' : 'Đã mở lại vé tháng.');
      reload();
    } catch (error) {
      // 409 ở đây là luật nghiệp vụ (vé đã hết hạn / đã ở trạng thái đích) — câu của backend
      // đã đủ rõ để hiện thẳng.
      message.error(errorText(error));
    }
  };

  // ── Thao tác trên loại vé ──────────────────────────────────────────────────────────────

  const openCreatePassType = () => {
    setEditingPassType(null);
    form.resetFields();
    form.setFieldsValue({ code: '', name: '', durationMonths: 1, price: 0 });
    setPassTypeModalOpen(true);
  };

  const openEditPassType = (record: PassTypeRecord) => {
    setEditingPassType(record);
    form.resetFields();
    form.setFieldsValue({
      code: record.code,
      name: record.name,
      durationMonths: record.durationMonths,
      price: record.price,
    });
    setPassTypeModalOpen(true);
  };

  const submitPassType = async () => {
    let values: PassTypeFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // Lỗi hiển thị ngay dưới ô nhập — không cần toast.
      return;
    }

    setSubmitting(true);
    try {
      if (editingPassType) {
        await updatePassType(editingPassType.id, {
          name: values.name!.trim(),
          durationMonths: values.durationMonths!,
          price: values.price!,
        });
        message.success('Đã cập nhật loại vé.');
      } else {
        await createPassType({
          code: values.code!.trim(),
          name: values.name!.trim(),
          durationMonths: values.durationMonths!,
          price: values.price!,
        });
        message.success('Đã thêm loại vé.');
      }

      setPassTypeModalOpen(false);
      reload();
    } catch (error) {
      message.error(errorText(error));
    } finally {
      setSubmitting(false);
    }
  };

  const removePassType = async (record: PassTypeRecord) => {
    try {
      await deletePassType(record.id);
      message.success('Đã xoá loại vé.');
      reload();
    } catch (error) {
      message.error(errorText(error));
    }
  };

  // ── Cột bảng ───────────────────────────────────────────────────────────────────────────

  const passColumns: TableProps<AdminMonthlyPass>['columns'] = [
    {
      title: 'Mã vé',
      key: 'code',
      width: 190,
      render: (_, record) => (
        <Space direction="vertical" size={0}>
          <Typography.Text strong>{record.code}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            Tạo {formatDateTime(record.createdAt)}
          </Typography.Text>
        </Space>
      ),
    },
    {
      title: 'Hành khách',
      key: 'user',
      width: 200,
      render: (_, record) => (
        <Space direction="vertical" size={0}>
          <Typography.Text>{record.userFullName || '—'}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {record.userPhoneNumber || '—'}
          </Typography.Text>
        </Space>
      ),
    },
    {
      title: 'Tuyến',
      key: 'route',
      render: (_, record) => (
        <Space direction="vertical" size={0}>
          <Typography.Text>
            {record.routeCode} · {record.routeName}
          </Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {record.passTypeName || record.passTypeCode}
          </Typography.Text>
        </Space>
      ),
    },
    {
      title: 'Giá',
      dataIndex: 'price',
      key: 'price',
      width: 130,
      render: (price: number) => formatVnd(price),
    },
    {
      title: 'Hiệu lực',
      key: 'validity',
      width: 200,
      render: (_, record) => (
        <Typography.Text>
          {formatDate(record.validFrom)} → {formatDate(record.validTo)}
        </Typography.Text>
      ),
    },
    {
      title: 'Trạng thái',
      key: 'status',
      width: 140,
      render: (_, record) => {
        const effective = effectiveStatusOf(record);
        const meta = EFFECTIVE_STATUS_META[effective];

        // Vé đã quá hạn theo ngày mà cột status còn Active: nói rõ job chưa quét để người vận
        // hành không tưởng bảng hiển thị sai.
        const title =
          record.status === 'Active' && effective === 'Expired'
            ? 'Đã quá hạn sử dụng; cột trạng thái lưu chưa được job quét lại.'
            : undefined;

        return (
          <StatusTag color={meta.color} title={title}>
            {meta.label}
          </StatusTag>
        );
      },
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 150,
      render: (_, record) => {
        const expiredByDate = new Date(record.validTo).getTime() < Date.now();

        return (
          <Space size={0}>
            <Button type="link" size="small" onClick={() => void openDetail(record)}>
              Xem
            </Button>

            {record.status === 'Cancelled' ? (
              // Mở lại chỉ có nghĩa khi vé còn hạn — backend cũng trả 409 nếu đã quá validTo.
              <Popconfirm
                title="Mở lại vé tháng?"
                description="Vé trở về trạng thái Active và có hiệu lực trở lại (nếu còn trong hạn)."
                okText="Mở lại"
                cancelText="Thôi"
                disabled={expiredByDate}
                onConfirm={() => changeStatus(record, 'Active')}
              >
                <Button type="link" size="small" disabled={expiredByDate}>
                  Mở lại
                </Button>
              </Popconfirm>
            ) : record.status === 'Active' ? (
              <Popconfirm
                title="Huỷ vé tháng?"
                description="Vé mất hiệu lực ngay và không hoàn tiền. Có thể mở lại khi vé chưa quá hạn."
                okText="Huỷ vé"
                okButtonProps={{ danger: true }}
                cancelText="Thôi"
                disabled={expiredByDate}
                onConfirm={() => changeStatus(record, 'Cancelled')}
              >
                <Button type="link" size="small" danger disabled={expiredByDate}>
                  Huỷ vé
                </Button>
              </Popconfirm>
            ) : null}
          </Space>
        );
      },
    },
  ];

  const passTypeColumns: TableProps<PassTypeRecord>['columns'] = [
    {
      title: 'Mã',
      dataIndex: 'code',
      key: 'code',
      width: 140,
      render: (code: string) => <Typography.Text strong>{code}</Typography.Text>,
    },
    {
      title: 'Tên loại vé',
      dataIndex: 'name',
      key: 'name',
    },
    {
      title: 'Thời hạn',
      dataIndex: 'durationMonths',
      key: 'durationMonths',
      width: 120,
      render: (months: number) => `${months} tháng`,
    },
    {
      title: 'Giá',
      dataIndex: 'price',
      key: 'price',
      width: 140,
      render: (price: number) => formatVnd(price),
    },
    {
      title: 'Vé đang dùng',
      dataIndex: 'monthlyPassCount',
      key: 'monthlyPassCount',
      width: 130,
      render: (count: number) => count.toLocaleString('vi-VN'),
    },
    {
      title: 'Ngày tạo',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 130,
      render: (createdAt: string) => formatDate(createdAt),
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 140,
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" size="small" onClick={() => openEditPassType(record)}>
            Sửa
          </Button>
          <Popconfirm
            title="Xoá loại vé?"
            description={
              record.monthlyPassCount > 0
                ? 'Loại vé đã có vé tháng sử dụng — không xoá được.'
                : 'Loại vé chưa được dùng sẽ bị xoá khỏi danh mục.'
            }
            okText="Xoá"
            okButtonProps={{ danger: true }}
            cancelText="Thôi"
            // Chặn ngay ở giao diện cho đỡ một vòng gọi mạng; backend vẫn là nơi quyết định.
            disabled={record.monthlyPassCount > 0}
            onConfirm={() => removePassType(record)}
          >
            <Button type="link" size="small" danger disabled={record.monthlyPassCount > 0}>
              Xoá
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  // ── Thẻ thống kê ───────────────────────────────────────────────────────────────────────

  const statCards: { title: string; value: string | number; color?: string; hint?: string }[] = [
    {
      title: 'Đang hiệu lực',
      value: stats?.activeCount ?? 0,
      color: '#16a34a',
      hint: 'Chưa huỷ, trong khoảng hiệu lực',
    },
    {
      title: 'Sắp hiệu lực',
      value: stats?.upcomingCount ?? 0,
      color: '#2563eb',
      hint: 'Đã mua trước, chưa tới ngày bắt đầu',
    },
    {
      title: 'Đã hết hạn',
      value: stats?.expiredCount ?? 0,
      color: '#dc2626',
      hint: 'Đã quá ngày kết thúc',
    },
    { title: 'Đã huỷ', value: stats?.cancelledCount ?? 0, hint: 'Quản trị viên huỷ' },
    { title: 'Tổng doanh thu', value: formatVnd(stats?.totalRevenue ?? 0), hint: 'Không gồm hoàn tiền' },
    {
      title: 'Doanh thu tháng này',
      value: formatVnd(stats?.revenueThisMonth ?? 0),
      hint: 'Vé tạo trong tháng hiện tại',
    },
  ];

  return (
    <div>
      <PageHeader
        title="Quản lý vé tháng"
        subtitle="Tra cứu mọi vé tháng, huỷ/mở lại vé và quản lý danh mục loại vé."
        extra={
          <Button icon={<ReloadOutlined />} onClick={reload}>
            Làm mới
          </Button>
        }
      />

      <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
        {statCards.map((card) => (
          <Col key={card.title} xs={24} sm={12} lg={8} xl={4}>
            <PageCard>
              <Statistic
                title={card.title}
                value={card.value}
                loading={statsLoading}
                valueStyle={card.color ? { color: card.color, fontSize: 22 } : { fontSize: 22 }}
              />
              {card.hint && (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {card.hint}
                </Typography.Text>
              )}
            </PageCard>
          </Col>
        ))}
      </Row>

      <Tabs
        defaultActiveKey="passes"
        items={[
          {
            key: 'passes',
            label: 'Vé tháng',
            children: (
              <PageCard
                toolbar={
                  <FilterBar>
                    <Input.Search
                      allowClear
                      placeholder="Mã vé, họ tên hoặc SĐT"
                      style={{ width: 280 }}
                      onSearch={(value) => {
                        setSearch(value.trim());
                        setPage(1);
                      }}
                      onChange={(e) => {
                        // Bấm X (allowClear) thì xoá lọc ngay, không chờ Enter.
                        if (!e.target.value) {
                          setSearch('');
                          setPage(1);
                        }
                      }}
                    />

                    <Select<MonthlyPassStoredStatus>
                      allowClear
                      placeholder="Trạng thái lưu"
                      style={{ width: 210 }}
                      options={MONTHLY_PASS_STORED_STATUS_OPTIONS}
                      onChange={(value) => {
                        setStatus(value ?? undefined);
                        setPage(1);
                      }}
                    />

                    <Select<string>
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      placeholder="Tuyến"
                      style={{ width: 220 }}
                      options={routeOptions.map((route) => ({
                        value: route.id,
                        label: `${route.code} · ${route.name}`,
                      }))}
                      onChange={(value) => {
                        setRouteId(value ?? undefined);
                        setPage(1);
                      }}
                    />

                    <Select<string>
                      allowClear
                      placeholder="Loại vé"
                      style={{ width: 180 }}
                      options={passTypes.map((type) => ({
                        value: type.code,
                        label: type.name,
                      }))}
                      onChange={(value) => {
                        setPassTypeCode(value ?? undefined);
                        setPage(1);
                      }}
                    />
                  </FilterBar>
                }
              >
                <Table<AdminMonthlyPass>
                  rowKey="id"
                  columns={passColumns}
                  dataSource={passes}
                  loading={loading}
                  scroll={{ x: 1180 }}
                  locale={{ emptyText: 'Không tìm thấy vé tháng nào' }}
                  pagination={tablePagination({
                    page,
                    pageSize,
                    total,
                    unitLabel: 'vé tháng',
                    onChange: (nextPage, nextPageSize) => {
                      // Đổi cỡ trang thì về trang 1 để không đứng ở trang không còn tồn tại.
                      setPage(nextPageSize !== pageSize ? 1 : nextPage);
                      setPageSize(nextPageSize);
                    },
                  })}
                />
              </PageCard>
            ),
          },
          {
            key: 'pass-types',
            label: 'Loại vé',
            children: (
              <PageCard
                toolbar={
                  <FilterBar
                    actions={
                      <Button type="primary" icon={<PlusOutlined />} onClick={openCreatePassType}>
                        Thêm loại vé
                      </Button>
                    }
                  >
                    <Typography.Text type="secondary">
                      Năm loại vé dùng khi hành khách đăng ký/gia hạn. Đổi giá không làm thay
                      đổi giá của vé đã bán.
                    </Typography.Text>
                  </FilterBar>
                }
              >
                <Table<PassTypeRecord>
                  rowKey="id"
                  columns={passTypeColumns}
                  dataSource={passTypes}
                  loading={passTypeLoading}
                  scroll={{ x: 900 }}
                  locale={{ emptyText: 'Chưa có loại vé nào' }}
                  pagination={false}
                />
              </PageCard>
            ),
          },
        ]}
      />

      {/* Chi tiết một vé — đọc từ GET /monthly-passes/{id}. */}
      <Modal
        open={detailOpen}
        title="Chi tiết vé tháng"
        footer={
          <Button onClick={() => setDetailOpen(false)}>Đóng</Button>
        }
        onCancel={() => setDetailOpen(false)}
        loading={detailLoading}
        width={720}
      >
        {detail && (
          <Descriptions bordered size="small" column={2}>
            <Descriptions.Item label="Mã vé" span={2}>
              <Typography.Text strong copyable>
                {detail.code}
              </Typography.Text>
            </Descriptions.Item>
            <Descriptions.Item label="Hành khách">{detail.userFullName || '—'}</Descriptions.Item>
            <Descriptions.Item label="Số điện thoại">
              {detail.userPhoneNumber || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Tuyến" span={2}>
              {detail.routeCode} · {detail.routeName}
            </Descriptions.Item>
            <Descriptions.Item label="Loại vé">
              {detail.passTypeName} ({detail.passTypeCode})
            </Descriptions.Item>
            <Descriptions.Item label="Giá">{formatVnd(detail.price)}</Descriptions.Item>
            <Descriptions.Item label="Hiệu lực từ">
              {formatDateTime(detail.validFrom)}
            </Descriptions.Item>
            <Descriptions.Item label="Hiệu lực đến">
              {formatDateTime(detail.validTo)}
            </Descriptions.Item>
            <Descriptions.Item label="Trạng thái hiệu lực">
              <StatusTag color={EFFECTIVE_STATUS_META[effectiveStatusOf(detail)].color}>
                {EFFECTIVE_STATUS_META[effectiveStatusOf(detail)].label}
              </StatusTag>
            </Descriptions.Item>
            <Descriptions.Item label="Trạng thái lưu">
              <Typography.Text type="secondary">{detail.status}</Typography.Text>
            </Descriptions.Item>
            <Descriptions.Item label="Ngày tạo">
              {formatDateTime(detail.createdAt)}
            </Descriptions.Item>
            <Descriptions.Item label="Cập nhật cuối">
              {detail.updatedAt ? formatDateTime(detail.updatedAt) : '—'}
            </Descriptions.Item>
          </Descriptions>
        )}
      </Modal>

      {/* Thêm/sửa loại vé — mã bất biến sau khi tạo nên ô mã chỉ đọc khi sửa. */}
      <Modal
        open={passTypeModalOpen}
        title={editingPassType ? 'Sửa loại vé tháng' : 'Thêm loại vé tháng'}
        okText="Lưu"
        cancelText="Huỷ"
        confirmLoading={submitting}
        onOk={() => void submitPassType()}
        onCancel={() => setPassTypeModalOpen(false)}
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="code"
            label="Mã loại vé"
            extra={
              editingPassType
                ? 'Mã không sửa được sau khi tạo — vé đã bán đang tham chiếu mã này.'
                : 'Chỉ chữ và số, tối đa 20 ký tự. Ví dụ: OneMonth.'
            }
            rules={[
              { required: true, message: 'Nhập mã loại vé' },
              { max: 20, message: 'Mã tối đa 20 ký tự' },
              {
                pattern: /^[A-Za-z0-9]+$/,
                message: 'Mã chỉ gồm chữ và số, không dấu cách',
              },
            ]}
          >
            <Input maxLength={20} disabled={Boolean(editingPassType)} placeholder="OneMonth" />
          </Form.Item>

          <Form.Item
            name="name"
            label="Tên loại vé"
            rules={[
              { required: true, message: 'Nhập tên loại vé' },
              { max: 100, message: 'Tên tối đa 100 ký tự' },
            ]}
          >
            <Input maxLength={100} placeholder="Vé tháng 1 tháng" />
          </Form.Item>

          <Form.Item
            name="durationMonths"
            label="Thời hạn (tháng)"
            rules={[{ required: true, message: 'Nhập thời hạn' }]}
          >
            <InputNumber min={1} max={120} style={{ width: '100%' }} />
          </Form.Item>

          <Form.Item
            name="price"
            label="Giá (₫)"
            rules={[{ required: true, message: 'Nhập giá' }]}
          >
            <InputNumber
              min={0}
              max={9_999_999_999.99}
              step={10_000}
              style={{ width: '100%' }}
              addonAfter="₫"
            />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
