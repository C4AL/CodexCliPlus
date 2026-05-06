import {
  Suspense,
  lazy,
  useEffect,
  type ComponentType,
  type ReactElement,
} from "react";
import { Navigate, useRoutes, type Location } from "react-router-dom";
import { DashboardPage } from "@/pages/DashboardPage";
import { LoadingSpinner } from "@/components/ui/LoadingSpinner";

const DashboardOverviewPage = lazyPage(
  () => import("@/pages/DashboardOverviewPage"),
  "DashboardOverviewPage",
);
const AccountCenterPage = lazyPage(
  () => import("@/pages/AccountCenterPage"),
  "AccountCenterPage",
);
const UsagePage = lazyPage(() => import("@/pages/UsagePage"), "UsagePage");
const CodexConfigPage = lazyPage(
  () => import("@/pages/CodexConfigPage"),
  "CodexConfigPage",
);
const ConfigPage = lazyPage(() => import("@/pages/ConfigPage"), "ConfigPage");
const LogsPage = lazyPage(() => import("@/pages/LogsPage"), "LogsPage");
const SystemPage = lazyPage(() => import("@/pages/SystemPage"), "SystemPage");

function lazyPage<TModule extends Record<string, unknown>>(
  loader: () => Promise<TModule>,
  exportName: keyof TModule,
) {
  return lazy(async () => ({
    default: (await loader())[exportName] as ComponentType,
  }));
}

function RouteFallback() {
  return (
    <div
      aria-busy="true"
      style={{ display: "grid", minHeight: 220, placeItems: "center" }}
    >
      <LoadingSpinner size={28} />
    </div>
  );
}

function RoutePerformanceMarker({
  name,
  children,
}: {
  name: string;
  children: ReactElement;
}) {
  useEffect(() => {
    if (typeof performance === "undefined") {
      return;
    }

    const markName = `ccp-route-rendered:${name}`;
    performance.mark(markName);
    if (performance.getEntriesByName("ccp-entry-start").length > 0) {
      performance.measure(
        `ccp-entry-to-route:${name}`,
        "ccp-entry-start",
        markName,
      );
    }
  }, [name]);

  return children;
}

const route = (name: string, element: ReactElement) => (
  <RoutePerformanceMarker name={name}>
    <Suspense fallback={<RouteFallback />}>{element}</Suspense>
  </RoutePerformanceMarker>
);

const dashboardRoute = (
  <RoutePerformanceMarker name="dashboard">
    <DashboardPage />
  </RoutePerformanceMarker>
);

const mainRoutes = [
  { path: "/", element: dashboardRoute },
  {
    path: "/dashboard/overview",
    element: route("dashboard-overview", <DashboardOverviewPage />),
  },
  { path: "/dashboard", element: dashboardRoute },
  {
    path: "/console",
    element: <Navigate to="/dashboard/overview" replace />,
  },
  { path: "/settings", element: <Navigate to="/config" replace /> },
  { path: "/api-keys", element: <Navigate to="/config" replace /> },
  {
    path: "/accounts",
    element: route("accounts", <AccountCenterPage />),
  },
  {
    path: "/ai-providers",
    element: <Navigate to="/accounts#codex-config" replace />,
  },
  {
    path: "/ai-providers/*",
    element: <Navigate to="/accounts#codex-config" replace />,
  },
  {
    path: "/auth-files",
    element: <Navigate to="/accounts#auth-files" replace />,
  },
  {
    path: "/auth-files/*",
    element: <Navigate to="/accounts#auth-files" replace />,
  },
  { path: "/oauth", element: <Navigate to="/accounts#oauth-login" replace /> },
  { path: "/quota", element: <Navigate to="/accounts#quota-management" replace /> },
  { path: "/usage", element: route("usage", <UsagePage />) },
  { path: "/codex-config", element: route("codex-config", <CodexConfigPage />) },
  { path: "/config", element: route("config", <ConfigPage />) },
  { path: "/logs", element: route("logs", <LogsPage />) },
  { path: "/system", element: route("system", <SystemPage />) },
  { path: "*", element: <Navigate to="/" replace /> },
];

export function MainRoutes({ location }: { location?: Location }) {
  return useRoutes(mainRoutes, location);
}
