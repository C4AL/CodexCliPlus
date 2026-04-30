import type { SVGProps } from 'react';
import {
  BookOpen,
  Bot,
  ChartLine,
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronUp,
  Code,
  Diamond,
  DollarSign,
  Download,
  ExternalLink,
  Eye,
  EyeOff,
  FileText,
  Inbox,
  Info,
  Key,
  LayoutDashboard,
  Satellite,
  ScrollText,
  Search,
  Settings,
  Shield,
  SlidersHorizontal,
  Timer,
  Trash2,
  TrendingUp,
  X,
  type LucideIcon,
  type LucideProps,
} from 'lucide-react';

export type IconProps = LucideProps;

const baseSvgProps: SVGProps<SVGSVGElement> = {
  xmlns: 'http://www.w3.org/2000/svg',
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': 'true',
  focusable: 'false',
};

const sidebarSvgProps: SVGProps<SVGSVGElement> = {
  ...baseSvgProps,
  strokeWidth: 1.72,
  strokeLinecap: 'square',
  strokeLinejoin: 'miter',
  strokeMiterlimit: 10,
};

function renderLucideIcon(Icon: LucideIcon, { size = 20, ...props }: IconProps) {
  return <Icon aria-hidden="true" focusable="false" size={size} {...props} />;
}

export function IconSlidersHorizontal(props: IconProps) {
  return renderLucideIcon(SlidersHorizontal, props);
}

export function IconKey(props: IconProps) {
  return renderLucideIcon(Key, props);
}

export function IconBot(props: IconProps) {
  return renderLucideIcon(Bot, props);
}

export function IconModelCluster({ size = 20, ...props }: IconProps) {
  return (
    <svg {...baseSvgProps} width={size} height={size} {...props}>
      <rect x="3" y="5" width="6" height="6" rx="1.5" />
      <rect x="15" y="5" width="6" height="6" rx="1.5" />
      <rect x="9" y="13" width="6" height="6" rx="1.5" />
      <path d="M9 8h6" />
      <path d="M12 11v2" />
      <path d="M7.5 11v2" />
      <path d="M16.5 11v2" />
    </svg>
  );
}

export function IconFilterAll({ size = 20, ...props }: IconProps) {
  return (
    <svg {...baseSvgProps} width={size} height={size} {...props}>
      <rect x="3.5" y="3.5" width="5" height="5" rx="1.4" />
      <rect x="15.5" y="3.5" width="5" height="5" rx="1.4" />
      <rect x="3.5" y="15.5" width="5" height="5" rx="1.4" />
      <rect x="15.5" y="15.5" width="5" height="5" rx="1.4" />
      <path d="M8.5 8.5 10.75 10.75" />
      <path d="M15.5 8.5 13.25 10.75" />
      <path d="M8.5 15.5 10.75 13.25" />
      <path d="M15.5 15.5 13.25 13.25" />
      <circle cx="12" cy="12" r="1.6" fill="currentColor" stroke="none" />
    </svg>
  );
}

export function IconFileText(props: IconProps) {
  return renderLucideIcon(FileText, props);
}

export function IconShield(props: IconProps) {
  return renderLucideIcon(Shield, props);
}

export function IconChartLine(props: IconProps) {
  return renderLucideIcon(ChartLine, props);
}

export function IconSettings(props: IconProps) {
  return renderLucideIcon(Settings, props);
}

export function IconScrollText(props: IconProps) {
  return renderLucideIcon(ScrollText, props);
}

export function IconInfo(props: IconProps) {
  return renderLucideIcon(Info, props);
}

export function IconDownload(props: IconProps) {
  return renderLucideIcon(Download, props);
}

export function IconTrash2(props: IconProps) {
  return renderLucideIcon(Trash2, props);
}

export function IconChevronUp(props: IconProps) {
  return renderLucideIcon(ChevronUp, props);
}

export function IconChevronDown(props: IconProps) {
  return renderLucideIcon(ChevronDown, props);
}

export function IconChevronLeft(props: IconProps) {
  return renderLucideIcon(ChevronLeft, props);
}

export function IconSearch(props: IconProps) {
  return renderLucideIcon(Search, props);
}

export function IconX(props: IconProps) {
  return renderLucideIcon(X, props);
}

export function IconCheck(props: IconProps) {
  return renderLucideIcon(Check, props);
}

export function IconEye(props: IconProps) {
  return renderLucideIcon(Eye, props);
}

export function IconEyeOff(props: IconProps) {
  return renderLucideIcon(EyeOff, props);
}

export function IconInbox(props: IconProps) {
  return renderLucideIcon(Inbox, props);
}

export function IconSatellite(props: IconProps) {
  return renderLucideIcon(Satellite, props);
}

export function IconDiamond(props: IconProps) {
  return renderLucideIcon(Diamond, props);
}

export function IconTimer(props: IconProps) {
  return renderLucideIcon(Timer, props);
}

export function IconTrendingUp(props: IconProps) {
  return renderLucideIcon(TrendingUp, props);
}

export function IconDollarSign(props: IconProps) {
  return renderLucideIcon(DollarSign, props);
}

export function IconGithub({ size = 20, ...props }: IconProps) {
  return (
    <svg {...baseSvgProps} width={size} height={size} {...props}>
      <path d="M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4" />
      <path d="M9 18c-4.51 2-5-2-7-2" />
    </svg>
  );
}

export function IconExternalLink(props: IconProps) {
  return renderLucideIcon(ExternalLink, props);
}

export function IconBookOpen(props: IconProps) {
  return renderLucideIcon(BookOpen, props);
}

export function IconCode(props: IconProps) {
  return renderLucideIcon(Code, props);
}

export function IconLayoutDashboard(props: IconProps) {
  return renderLucideIcon(LayoutDashboard, props);
}

export function IconSidebarDashboard({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <rect x="3" y="3" width="7.5" height="8" rx="1.5" />
      <rect x="13.5" y="3" width="7.5" height="5" rx="1.5" fill="currentColor" fillOpacity="0.12" />
      <rect x="3" y="14" width="7.5" height="7" rx="1.5" fill="currentColor" fillOpacity="0.12" />
      <rect x="13.5" y="11" width="7.5" height="10" rx="1.5" />
    </svg>
  );
}

export function IconSidebarConfig({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <path d="M4 8h16" />
      <path d="M4 16h16" />
      <circle cx="9.5" cy="8" r="2.8" fill="currentColor" fillOpacity="0.12" />
      <circle cx="15" cy="16" r="2.8" fill="currentColor" fillOpacity="0.12" />
    </svg>
  );
}

export function IconSidebarConsole({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 8.5h18" />
      <path d="M7 12l3 2.5L7 17" />
      <path d="M13 17h4" />
      <rect x="14" y="11" width="4" height="3" rx="0.8" fill="currentColor" fillOpacity="0.12" />
    </svg>
  );
}

export function IconSidebarProviders({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <circle cx="12" cy="5.5" r="2.8" fill="currentColor" fillOpacity="0.12" />
      <circle cx="5.5" cy="18.5" r="2.8" />
      <circle cx="18.5" cy="18.5" r="2.8" />
      <path d="M10.2 7.8 7 16.2" />
      <path d="M13.8 7.8 17 16.2" />
      <path d="M8.3 18.5h7.4" />
    </svg>
  );
}

export function IconSidebarAuthFiles({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <path d="M7 3h7l4 4v12a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Z" />
      <path d="M14 3v4h4" fill="currentColor" fillOpacity="0.12" />
      <path d="M9 13l2 2 4-4" />
    </svg>
  );
}

export function IconSidebarOauth({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <path
        d="M12 3l8 4v5c0 5.25-3.4 8.25-8 10-4.6-1.75-8-4.75-8-10V7Z"
        fill="currentColor"
        fillOpacity="0.08"
      />
      <circle cx="12" cy="11" r="1.5" fill="currentColor" stroke="none" />
      <path d="M12 12.5v2.5" />
    </svg>
  );
}

export function IconSidebarQuota({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <circle cx="12" cy="12" r="8" />
      <path d="M12 12V4a8 8 0 0 1 8 8Z" fill="currentColor" fillOpacity="0.12" />
    </svg>
  );
}

export function IconSidebarUsage({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <path d="M3.5 20h17" />
      <rect x="5" y="13" width="3.5" height="7" rx="0.5" />
      <rect
        x="10.25"
        y="7"
        width="3.5"
        height="13"
        rx="0.5"
        fill="currentColor"
        fillOpacity="0.12"
      />
      <rect x="15.5" y="10" width="3.5" height="10" rx="0.5" />
    </svg>
  );
}

export function IconSidebarLogs({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 8.5h18" />
      <circle cx="5.5" cy="6.2" r="0.8" fill="currentColor" stroke="none" />
      <circle cx="7.8" cy="6.2" r="0.8" fill="currentColor" fillOpacity="0.4" stroke="none" />
      <path d="M7 12l3 2.5-3 2.5" />
      <path d="M13 17h4" />
    </svg>
  );
}

export function IconSidebarSystem({ size = 20, ...props }: IconProps) {
  return (
    <svg {...sidebarSvgProps} width={size} height={size} {...props}>
      <rect x="6" y="6" width="12" height="12" rx="2" />
      <rect x="9" y="9" width="6" height="6" rx="1" fill="currentColor" fillOpacity="0.12" />
      <path d="M6 10H3" />
      <path d="M6 14H3" />
      <path d="M21 10h-3" />
      <path d="M21 14h-3" />
      <path d="M10 6V3" />
      <path d="M14 6V3" />
      <path d="M10 21v-3" />
      <path d="M14 21v-3" />
    </svg>
  );
}
