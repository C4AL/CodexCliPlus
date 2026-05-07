export function BrowserManagementBlocked() {
  return (
    <main
      style={{
        minHeight: '100vh',
        display: 'grid',
        placeItems: 'center',
        padding: 24,
        background: 'var(--bg-secondary)',
      }}
    >
      <section
        role="alert"
        aria-labelledby="browser-management-blocked-title"
        style={{
          width: 'min(100%, 520px)',
          border: '1px solid var(--border-color)',
          borderRadius: 8,
          background: 'var(--bg-primary)',
          boxShadow: 'var(--shadow-md)',
          padding: 28,
          textAlign: 'left',
        }}
      >
        <div
          id="browser-management-blocked-title"
          style={{ color: 'var(--text-primary)', fontSize: 22, fontWeight: 800 }}
        >
          请在桌面应用内打开管理界面
        </div>
        <p style={{ color: 'var(--text-secondary)', lineHeight: 1.7, margin: '14px 0 0' }}>
          普通浏览器入口已关闭，无法登录或进入本地 WebUI 管理界面。请从 CodexCliPlus
          桌面应用进入管理界面。
        </p>
      </section>
    </main>
  );
}
