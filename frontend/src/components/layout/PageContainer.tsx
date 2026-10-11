import type { ReactNode } from 'react';

interface PageContainerProps {
  children: ReactNode;
  /** Có thanh tab đáy: chừa thêm khoảng trống để nội dung không bị tab che. */
  hasBottomNav?: boolean;
}

/** Khung nội dung dùng chung: bề ngang tối đa 1200px, lề co theo màn hình. */
export default function PageContainer({ children, hasBottomNav = false }: PageContainerProps) {
  return (
    <div
      style={{
        width: '100%',
        maxWidth: 1200,
        margin: '0 auto',
        padding: 'clamp(16px, 2.5vw, 24px)',
        paddingBottom: hasBottomNav
          ? 'calc(var(--app-bottom-nav-h) + env(safe-area-inset-bottom) + 24px)'
          : undefined,
      }}
    >
      {children}
    </div>
  );
}
