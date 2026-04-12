using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
                // Pass the URL as an argument to a fixed, known system launcher rather than
                // as a FileName with UseShellExecute=true, which would treat user-supplied
                // text as a shell command and is a potential code-execution vector.
                string launcher;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    launcher = "explorer.exe";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    launcher = "open";
                else
                    launcher = "xdg-open"; // Linux / BSD

                var startInfo = new ProcessStartInfo
                {
                    FileName = launcher,
                    Arguments = uri.AbsoluteUri,
                    UseShellExecute = false,
                };

                Process.Start(startInfo);
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