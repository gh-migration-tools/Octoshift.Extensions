using OctoshiftCLI.Contracts;
using OctoshiftCLI.Extensions;
using System.Net.Http.Headers;
using System.Reflection;

namespace OctoshiftCLI.Services;

public abstract class VersionCheckerBase : IVersionProvider
{
    protected abstract string ProductName { get; }
    protected abstract string LatestVersionFileUrl { get; }

    private readonly HttpClient _httpClient;
    private readonly OctoLogger _logger;

    private string? _latestVersion;

    protected VersionCheckerBase(
        HttpClient httpClient,
        OctoLogger logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> IsLatest()
    {
        var currentVersion = Version.Parse(GetCurrentVersion());
        var latestVersion = Version.Parse(await GetLatestVersion().ConfigureAwait(false));

        return currentVersion >= latestVersion;
    }

    public string GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);

    public string GetVersionComments() =>
        CliContext.RootCommand.HasValue() && CliContext.ExecutingCommand.HasValue()
            ? $"({CliContext.RootCommand}/{CliContext.ExecutingCommand})"
            : null!;

    public async Task<string> GetLatestVersion()
    {
        if (_latestVersion.IsNullOrWhiteSpace())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ProductName, GetCurrentVersion()));

            if (GetVersionComments() is { } comments)
            {
                _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(comments));
            }

            var uri = new Uri(LatestVersionFileUrl);

            _logger.LogVerbose($"HTTP GET: {LatestVersionFileUrl}");

            var response = await _httpClient.GetAsync(uri).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            _logger.LogVerbose($"RESPONSE ({response.StatusCode}): {content}");

            foreach (var header in response.Headers)
            {
                _logger.LogDebug($"RESPONSE HEADER: {header.Key} = {string.Join(",", header.Value)}");
            }

            response.EnsureSuccessStatusCode();

            _latestVersion = content.TrimStart('v', 'V').Trim();
        }

        return _latestVersion!;
    }
}
