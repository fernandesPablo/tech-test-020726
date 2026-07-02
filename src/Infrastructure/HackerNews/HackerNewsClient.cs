using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace TechTest.Infrastructure.HackerNews;

internal sealed class HackerNewsClient : IHackerNewsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HackerNewsClient> _logger;

    public HackerNewsClient(HttpClient httpClient, ILogger<HackerNewsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _httpClient.GetFromJsonAsync<int[]>("/v0/beststories.json", cancellationToken)
                  ?? [];

        return ids;
    }

    public async Task<HackerNewsStory?> GetStoryAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<HackerNewsStory>($"/v0/item/{id}.json", cancellationToken);
    }
}
