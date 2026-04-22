import { extractGenericPage } from "./generic-page.mjs";
import { extractYouTubeVideo, isYouTubeWatchUrl } from "./youtube.mjs";

export function detectAdapter(url) {
  if (isYouTubeWatchUrl(url)) {
    return "youtube-video";
  }
  return "generic-page";
}

export async function extractStructuredContent(cdp, tabId, url, options = {}) {
  const adapter = options.adapter || detectAdapter(url);

  switch (adapter) {
    case "youtube-video":
      return extractYouTubeVideo(cdp, tabId, options);
    case "generic-page":
    default:
      return extractGenericPage(cdp, tabId, options);
  }
}
