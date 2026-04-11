using System;
using System.Diagnostics;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core
{
    /// <summary>
    /// Opens validated external links in the user's default browser.
    /// </summary>
    internal static class BrowserLauncher
    {
        /// <summary>
        /// Open an absolute HTTP or HTTPS URL in the system browser.
        /// </summary>
        internal static void OpenUrl(string url, ILogger logger, bool isServer)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be empty.", nameof(url));
            if (isServer)
                throw new InvalidOperationException("Cannot open external URLs on a dedicated server.");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException($"URL must be an absolute URI: {url}", nameof(url));

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                throw new NotSupportedException($"Unsupported URI scheme '{uri.Scheme}'. Only http and https are supported.");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true,
                };

                var process = Process.Start(startInfo);
                if (process == null)
                    throw new InvalidOperationException($"The operating system did not launch a browser for '{uri.AbsoluteUri}'.");

                logger.Debug($"Opened external URL: {uri.AbsoluteUri}");
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to open external URL '{uri.AbsoluteUri}'", ex);
                throw new InvalidOperationException($"Failed to open external URL '{uri.AbsoluteUri}'.", ex);
            }
        }
    }
}