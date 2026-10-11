import { NavLink, useLocation } from 'react-router-dom';
import { bottomNavTabs, navKeyOf } from '../ui/navItems';

/**
 * Thanh tab đáy cho hành khách trên điện thoại — 5 lối vào hay dùng nhất, tuyển tay trong
 * `bottomNavTabs`. Mục đang mở nhận diện bằng MÀU + vạch 3px + `aria-current`, không chỉ màu.
 */
export default function BottomNav({ role }: { role: string | null }) {
  const tabs = bottomNavTabs(role);
  const { pathname } = useLocation();
  const selected = navKeyOf(pathname);

  if (tabs.length === 0) return null;

  return (
    <nav
      aria-label="Điều hướng chính"
      style={{
        position: 'fixed',
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: 100,
        display: 'flex',
        boxSizing: 'content-box',
        height: 'var(--app-bottom-nav-h)',
        paddingBottom: 'env(safe-area-inset-bottom)',
        background: 'var(--app-surface)',
        borderTop: '1px solid var(--app-border)',
      }}
    >
      {tabs.map((tab) => {
        const active = tab.activeFor.includes(selected);
        return (
          <NavLink
            key={tab.key}
            to={tab.to}
            aria-current={active ? 'page' : undefined}
            style={{
              position: 'relative',
              flex: 1,
              minWidth: 0,
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              gap: 2,
              textDecoration: 'none',
              color: active ? 'var(--app-primary)' : 'var(--app-text-muted)',
              fontSize: 12,
              fontWeight: active ? 600 : 500,
            }}
          >
            {active && (
              <span
                aria-hidden="true"
                style={{
                  position: 'absolute',
                  top: 0,
                  left: '50%',
                  transform: 'translateX(-50%)',
                  width: 28,
                  height: 3,
                  borderRadius: '0 0 3px 3px',
                  background: 'var(--app-primary)',
                }}
              />
            )}
            <span aria-hidden="true" style={{ fontSize: 20, lineHeight: 1 }}>
              {tab.icon}
            </span>
            <span style={{ whiteSpace: 'nowrap' }}>{tab.label}</span>
          </NavLink>
        );
      })}
    </nav>
  );
}
