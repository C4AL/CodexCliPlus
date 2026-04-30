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
const AiProvidersPage = lazyPage(
  () => import("@/pages/AiProvidersPage"),
  "AiProvidersPage",
);
const AiProvidersAmpcodeEditPage = lazyPage(
  () => import("@/pages/AiProvidersAmpcodeEditPage"),
  "AiProvidersAmpcodeEditPage",
);
const AiProvidersClaudeEditLayout = lazyPage(
  () => import("@/pages/AiProvidersClaudeEditLayout"),
  "AiProvidersClaudeEditLayout",
);
const AiProvidersClaudeEditPage = lazyPage(
  () => import("@/pages/AiProvidersClaudeEditPage"),
  "AiProvidersClaudeEditPage",
);
const AiProvidersClaudeModelsPage = lazyPage(
  () => import("@/pages/AiProvidersClaudeModelsPage"),
  "AiProvidersClaudeModelsPage",
);
const AiProvidersCodexEditPage = lazyPage(
  () => import("@/pages/AiProvidersCodexEditPage"),
  "AiProvidersCodexEditPage",
);
const AiProvidersGeminiEditPage = lazyPage(
  () => import("@/pages/AiProvidersGeminiEditPage"),
  "AiProvidersGeminiEditPage",
);
const AiProvidersOpenAIEditLayout = lazyPage(
  () => import("@/pages/AiProvidersOpenAIEditLayout"),
  "AiProvidersOpenAIEditLayout",
);
const AiProvidersOpenAIEditPage = lazyPage(
  () => import("@/pages/AiProvidersOpenAIEditPage"),
  "AiProvidersOpenAIEditPage",
);
const AiProvidersOpenAIModelsPage = lazyPage(
  () => import("@/pages/AiProvidersOpenAIModelsPage"),
  "AiProvidersOpenAIModelsPage",
);
const AiProvidersVertexEditPage = lazyPage(
  () => import("@/pages/AiProvidersVertexEditPage"),
  "AiProvidersVertexEditPage",
);
const AuthFilesPage = lazyPage(
  () => import("@/pages/AuthFilesPage"),
  "AuthFilesPage",
);
const AuthFilesOAuthExcludedEditPage = lazyPage(
  () => import("@/pages/AuthFilesOAuthExcludedEditPage"),
  "AuthFilesOAuthExcludedEditPage",
);
const AuthFilesOAuthModelAliasEditPage = lazyPage(
  () => import("@/pages/AuthFilesOAuthModelAliasEditPage"),
  "AuthFilesOAuthModelAliasEditPage",
);
const OAuthPage = lazyPage(() => import("@/pages/OAuthPage"), "OAuthPage");
const QuotaPage = lazyPage(() => import("@/pages/QuotaPage"), "QuotaPage");
const UsagePage = lazyPage(() => import("@/pages/UsagePage"), "UsagePage");
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
  { path: "/settings", element: <Navigate to="/config" replace /> },
  { path: "/api-keys", element: <Navigate to="/config" replace /> },
  {
    path: "/ai-providers/gemini/new",
    element: route("ai-providers-gemini-edit", <AiProvidersGeminiEditPage />),
  },
  {
    path: "/ai-providers/gemini/:index",
    element: route("ai-providers-gemini-edit", <AiProvidersGeminiEditPage />),
  },
  {
    path: "/ai-providers/codex/new",
    element: route("ai-providers-codex-edit", <AiProvidersCodexEditPage />),
  },
  {
    path: "/ai-providers/codex/:index",
    element: route("ai-providers-codex-edit", <AiProvidersCodexEditPage />),
  },
  {
    path: "/ai-providers/claude/new",
    element: route(
      "ai-providers-claude-layout",
      <AiProvidersClaudeEditLayout />,
    ),
    children: [
      {
        index: true,
        element: route(
          "ai-providers-claude-edit",
          <AiProvidersClaudeEditPage />,
        ),
      },
      {
        path: "models",
        element: route(
          "ai-providers-claude-models",
          <AiProvidersClaudeModelsPage />,
        ),
      },
    ],
  },
  {
    path: "/ai-providers/claude/:index",
    element: route(
      "ai-providers-claude-layout",
      <AiProvidersClaudeEditLayout />,
    ),
    children: [
      {
        index: true,
        element: route(
          "ai-providers-claude-edit",
          <AiProvidersClaudeEditPage />,
        ),
      },
      {
        path: "models",
        element: route(
          "ai-providers-claude-models",
          <AiProvidersClaudeModelsPage />,
        ),
      },
    ],
  },
  {
    path: "/ai-providers/vertex/new",
    element: route("ai-providers-vertex-edit", <AiProvidersVertexEditPage />),
  },
  {
    path: "/ai-providers/vertex/:index",
    element: route("ai-providers-vertex-edit", <AiProvidersVertexEditPage />),
  },
  {
    path: "/ai-providers/openai/new",
    element: route(
      "ai-providers-openai-layout",
      <AiProvidersOpenAIEditLayout />,
    ),
    children: [
      {
        index: true,
        element: route(
          "ai-providers-openai-edit",
          <AiProvidersOpenAIEditPage />,
        ),
      },
      {
        path: "models",
        element: route(
          "ai-providers-openai-models",
          <AiProvidersOpenAIModelsPage />,
        ),
      },
    ],
  },
  {
    path: "/ai-providers/openai/:index",
    element: route(
      "ai-providers-openai-layout",
      <AiProvidersOpenAIEditLayout />,
    ),
    children: [
      {
        index: true,
        element: route(
          "ai-providers-openai-edit",
          <AiProvidersOpenAIEditPage />,
        ),
      },
      {
        path: "models",
        element: route(
          "ai-providers-openai-models",
          <AiProvidersOpenAIModelsPage />,
        ),
      },
    ],
  },
  {
    path: "/ai-providers/ampcode",
    element: route("ai-providers-ampcode-edit", <AiProvidersAmpcodeEditPage />),
  },
  {
    path: "/ai-providers",
    element: route("ai-providers", <AiProvidersPage />),
  },
  {
    path: "/ai-providers/*",
    element: route("ai-providers", <AiProvidersPage />),
  },
  { path: "/auth-files", element: route("auth-files", <AuthFilesPage />) },
  {
    path: "/auth-files/oauth-excluded",
    element: route(
      "auth-files-oauth-excluded",
      <AuthFilesOAuthExcludedEditPage />,
    ),
  },
  {
    path: "/auth-files/oauth-model-alias",
    element: route(
      "auth-files-oauth-model-alias",
      <AuthFilesOAuthModelAliasEditPage />,
    ),
  },
  { path: "/oauth", element: route("oauth", <OAuthPage />) },
  { path: "/quota", element: route("quota", <QuotaPage />) },
  { path: "/usage", element: route("usage", <UsagePage />) },
  { path: "/config", element: route("config", <ConfigPage />) },
  { path: "/logs", element: route("logs", <LogsPage />) },
  { path: "/system", element: route("system", <SystemPage />) },
  { path: "*", element: <Navigate to="/" replace /> },
];

export function MainRoutes({ location }: { location?: Location }) {
  return useRoutes(mainRoutes, location);
}
