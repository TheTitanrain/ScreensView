(function () {
  "use strict";

  var DEFAULT_PRIMARY_LABEL = "Download Viewer";
  var DEFAULT_FALLBACK_LABEL = "Open latest release";
  var DEFAULT_DIRECT_STATUS = "Direct download is available from the latest ScreensView release.";
  var DEFAULT_FALLBACK_STATUS = "If a single Viewer executable is not identified, the button opens the latest release page.";
  var RELEASE_API_URL = "https://api.github.com/repos/titanrain/ScreensView/releases/latest";
  var RELEASE_FALLBACK_URL = "https://github.com/titanrain/ScreensView/releases";
  var VIEWER_ASSET_PATTERN = /^ScreensView\.Viewer.*\.exe$/i;

  function toText(value) {
    return typeof value === "string" ? value.trim() : "";
  }

  function selectViewerAssets(release) {
    if (!release || !Array.isArray(release.assets)) {
      return [];
    }

    return release.assets.filter(function (asset) {
      return (
        asset &&
        VIEWER_ASSET_PATTERN.test(toText(asset.name)) &&
        toText(asset.browser_download_url).length > 0
      );
    });
  }

  function resolveDownloadState(release, fallbackUrl, labels) {
    var resolvedFallbackUrl = toText(fallbackUrl) || RELEASE_FALLBACK_URL;
    var ui = labels || {};
    var primaryLabel = toText(ui.primary) || DEFAULT_PRIMARY_LABEL;
    var fallbackLabel = toText(ui.fallback) || DEFAULT_FALLBACK_LABEL;
    var directStatus = toText(ui.directStatus) || DEFAULT_DIRECT_STATUS;
    var fallbackStatus = toText(ui.fallbackStatus) || DEFAULT_FALLBACK_STATUS;
    var version = toText(release && release.tag_name);
    var matches = selectViewerAssets(release);

    if (matches.length === 1) {
      return {
        href: matches[0].browser_download_url,
        label: primaryLabel,
        source: "direct",
        statusText: version ? directStatus + " " + version + "." : directStatus,
        versionText: version ? "Viewer " + version : ""
      };
    }

    return {
      href: resolvedFallbackUrl,
      label: fallbackLabel,
      source: "fallback",
      statusText: version ? fallbackStatus + " " + version + "." : fallbackStatus,
      versionText: version ? "Viewer " + version : ""
    };
  }

  function documentLabels(doc) {
    var root = doc && doc.documentElement;
    var data = (root && root.dataset) || {};

    return {
      primary: toText(data.downloadPrimaryLabel) || DEFAULT_PRIMARY_LABEL,
      fallback: toText(data.downloadFallbackLabel) || DEFAULT_FALLBACK_LABEL,
      directStatus: toText(data.downloadDirectStatus) || DEFAULT_DIRECT_STATUS,
      fallbackStatus: toText(data.downloadFallbackStatus) || DEFAULT_FALLBACK_STATUS
    };
  }

  function applyDownloadState(doc, state) {
    doc.querySelectorAll("[data-download-link]").forEach(function (link) {
      link.setAttribute("href", state.href);

      if (link.dataset.dynamicLabel === "true") {
        link.textContent = state.label;
      }
    });

    doc.querySelectorAll("[data-download-status]").forEach(function (node) {
      node.textContent = state.statusText;
    });

    doc.querySelectorAll("[data-download-version]").forEach(function (node) {
      node.textContent = state.versionText;
      node.hidden = state.versionText.length === 0;
    });
  }

  function applyMenuState(doc, expanded) {
    var toggle = doc.querySelector("[data-menu-toggle]");
    var nav = doc.querySelector("[data-site-nav]");

    if (!toggle || !nav) {
      return;
    }

    toggle.setAttribute("aria-expanded", expanded ? "true" : "false");
    nav.dataset.open = expanded ? "true" : "false";
  }

  function setupMenu(doc) {
    var toggle = doc.querySelector("[data-menu-toggle]");
    if (!toggle) {
      return;
    }

    applyMenuState(doc, false);

    toggle.addEventListener("click", function () {
      var next = toggle.getAttribute("aria-expanded") !== "true";
      applyMenuState(doc, next);
    });

    doc.querySelectorAll("[data-site-nav] a").forEach(function (anchor) {
      anchor.addEventListener("click", function () {
        applyMenuState(doc, false);
      });
    });
  }

  async function hydrateDownloadLinks(options) {
    var settings = options || {};
    var doc = settings.document || (typeof document !== "undefined" ? document : null);

    if (!doc) {
      return resolveDownloadState(null, RELEASE_FALLBACK_URL, {});
    }

    var root = doc.documentElement;
    var labels = documentLabels(doc);
    var fallbackUrl = toText(root && root.dataset.releaseFallback) || RELEASE_FALLBACK_URL;
    var fallbackState = resolveDownloadState(null, fallbackUrl, labels);
    var fetchImpl = settings.fetchImpl || (typeof fetch === "function" ? fetch.bind(globalThis) : null);

    applyDownloadState(doc, fallbackState);

    if (!fetchImpl) {
      return fallbackState;
    }

    try {
      var response = await fetchImpl(RELEASE_API_URL, {
        headers: {
          Accept: "application/vnd.github+json"
        }
      });

      if (!response || !response.ok) {
        return fallbackState;
      }

      var release = await response.json();
      var state = resolveDownloadState(release, fallbackUrl, labels);
      applyDownloadState(doc, state);
      return state;
    } catch (_error) {
      return fallbackState;
    }
  }

  globalThis.ScreensViewSite = {
    selectViewerAssets: selectViewerAssets,
    resolveDownloadState: resolveDownloadState,
    hydrateDownloadLinks: hydrateDownloadLinks
  };

  if (typeof document !== "undefined") {
    document.addEventListener("DOMContentLoaded", function () {
      setupMenu(document);
      hydrateDownloadLinks().catch(function () {
        return undefined;
      });
    });
  }
})();
