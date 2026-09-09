using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace JoinFS
{
    /// <summary>
    /// Single point for fetching data files that live in the JoinFS GitHub repository
    /// (seed hubs, ban lists, model/type classifiers, add-on lists, ...).
    ///
    /// Files are pulled through the jsDelivr CDN rather than raw.githubusercontent.com,
    /// because the CDN is faster, cached close to the user and far less likely to be
    /// blocked by corporate/ISP filtering. Each file is tried against the main
    /// repository first and then the <c>joeherwig</c> fork; callers may pass further
    /// absolute fallback URLs to try after those.
    /// </summary>
    public static class GitHubData
    {
        /// <summary>
        /// CDN mirrors, tried in order. A repository-relative path
        /// (e.g. "JoinFS/util/seedhubs.txt") is appended to each entry.
        /// </summary>
        static readonly string[] Mirrors =
        [
            "https://cdn.jsdelivr.net/gh/tuduce/JoinFS@main/",
            "https://cdn.jsdelivr.net/gh/joeherwig/JoinFS@main/",
        ];

        /// <summary>
        /// Shared client. Uses the default (system) proxy, like the rest of JoinFS.
        /// </summary>
        static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>
        /// Build the ordered list of URLs that will be tried for a repository file.
        /// </summary>
        /// <param name="repoPath">Path relative to the repository root.</param>
        /// <param name="extraFallbackUrls">Absolute URLs to try after the CDN mirrors.</param>
        public static List<string> BuildUrls(string repoPath, params string[] extraFallbackUrls)
        {
            List<string> urls = [];
            foreach (var mirror in Mirrors)
            {
                urls.Add(mirror + repoPath.TrimStart('/'));
            }
            if (extraFallbackUrls != null)
            {
                urls.AddRange(extraFallbackUrls);
            }
            return urls;
        }

        /// <summary>
        /// Download a text file from the repository, trying each mirror (and any extra
        /// fallback URLs) in turn.
        /// </summary>
        /// <param name="repoPath">Path relative to the repository root, e.g. "JoinFS/util/seedhubs.txt".</param>
        /// <param name="log">Optional sink for progress/error messages.</param>
        /// <param name="extraFallbackUrls">Absolute URLs to try after the CDN mirrors.</param>
        /// <returns>The file contents, or <c>null</c> if every source failed.</returns>
        public static async Task<string> GetTextAsync(string repoPath, Action<string> log, params string[] extraFallbackUrls)
        {
            // buffer per-source failures and only report them if every source fails,
            // so the expected 404s for files that are not in the repository tree
            // (e.g. banlist.txt) do not spam the log on every startup
            List<string> failures = [];

            foreach (var url in BuildUrls(repoPath, extraFallbackUrls))
            {
                try
                {
                    string content = await httpClient.GetStringAsync(url);
                    log?.Invoke($"Downloaded {repoPath} from {url}");
                    return content;
                }
                catch (Exception ex)
                {
                    failures.Add($"  {url}: {ex.Message}");
                }
            }

            log?.Invoke($"Could not download {repoPath} from any source:\n" + string.Join("\n", failures));
            return null;
        }

        /// <summary>
        /// Download a text file from the repository and save it to <paramref name="destinationPath"/>.
        /// The destination is only written when a download succeeds, so an existing copy
        /// is preserved if every source fails.
        /// </summary>
        /// <returns><c>true</c> when the file was downloaded and saved.</returns>
        public static async Task<bool> DownloadToFileAsync(string repoPath, string destinationPath, Action<string> log, params string[] extraFallbackUrls)
        {
            string content = await GetTextAsync(repoPath, log, extraFallbackUrls);
            if (content == null)
            {
                return false;
            }

            try
            {
                await File.WriteAllTextAsync(destinationPath, content);
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Failed to save {repoPath} to {destinationPath}: {ex.Message}");
                return false;
            }
        }
    }
}
