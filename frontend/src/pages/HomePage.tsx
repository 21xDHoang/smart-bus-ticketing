import type { ReactNode } from 'react';
import { Flex, Tag, Typography } from 'antd';
import { useNavigate } from 'react-router-dom';
import { PageHeader } from '../components/ui';
import { visibleNav } from '../components/ui/navItems';
import type { NavGroup, NavLeaf } from '../components/ui/navItems';
import { getRoleMeta } from '../api/adminUserApi';
import { useAuth } from '../contexts';

const { Title } = Typography;

/** Thẻ lối vào lớn — CẢ thẻ là một nút, đủ cao để bấm trên điện thoại. */
function ShortcutTile({
  to,
  label,
  icon,
  onOpen,
}: {
  to: string;
  label: string;
  icon: ReactNode;
  onOpen: (to: string) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => onOpen(to)}
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'flex-start',
        gap: 8,
        minHeight: 96,
        padding: 16,
        textAlign: 'left',
        cursor: 'pointer',
        background: 'var(--app-surface)',
        border: '1px solid var(--app-border)',
        borderRadius: 12,
        color: 'inherit',
        font: 'inherit',
      }}
    >
      <span aria-hidden="true" style={{ fontSize: 22, lineHeight: 1, color: 'var(--app-primary)' }}>
        {icon}
      </span>
      <span style={{ fontWeight: 600 }}>{label}</span>
    </button>
  );
}

function TileGrid({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
        gap: 12,
      }}
    >
      {children}
    </div>
  );
}

/**
 * Trang chủ — bản tối giản của đợt nền tảng: lời chào + vai trò tiếng Việt + thẻ lối vào
 * theo vai trò (nguồn: navItems, không chép tay nhãn). Nội dung đầy đủ (ô tìm tuyến, vé
 * đang dùng, khuyến mãi…) sẽ bổ sung ở đợt thiết kế màn hành khách.
 */
export default function HomePage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const role = user?.role ?? null;
  const roleMeta = getRoleMeta(user?.role ?? '');

  const entries = visibleNav(role);
  // "Trang chủ" không lên thẻ lối vào — đang đứng ngay đây rồi.
  const leaves = entries.filter((entry): entry is NavLeaf => !('children' in entry) && entry.to !== '/');
  const groups = entries.filter((entry): entry is NavGroup => 'children' in entry);

  return (
    <div>
      <PageHeader
        title={`Xin chào, ${user?.fullName || 'Bạn'}! 👋`}
        subtitle={
          <Flex align="center" gap={8} wrap>
            <span>Bạn đang đăng nhập với vai trò</span>
            {/* Nền đậm sẵn có của vai trò + chữ trắng (variant="solid") — màu preset của antd
                (green/cyan/geekblue/purple) chỉ đạt ~3.4–4.2:1 ở một trong hai chế độ, không
                đủ chuẩn AA; cách này đạt >=4.7:1 ở cả hai chế độ. */}
            <Tag variant="solid" color={roleMeta.bg} style={{ marginInlineEnd: 0 }}>
              {roleMeta.label}
            </Tag>
          </Flex>
        }
      />

      {leaves.length > 0 && (
        <section>
          <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
            Lối vào nhanh
          </Title>
          <TileGrid>
            {leaves.map((leaf) => (
              <ShortcutTile key={leaf.to} to={leaf.to} label={leaf.label} icon={leaf.icon} onOpen={navigate} />
            ))}
          </TileGrid>
        </section>
      )}

      {groups.map((group) => (
        <section key={group.key} style={{ marginTop: 24 }}>
          <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
            {group.label}
          </Title>
          <TileGrid>
            {group.children.map((leaf) => (
              <ShortcutTile key={leaf.to} to={leaf.to} label={leaf.label} icon={leaf.icon} onOpen={navigate} />
            ))}
          </TileGrid>
        </section>
      ))}
    </div>
  );
}
