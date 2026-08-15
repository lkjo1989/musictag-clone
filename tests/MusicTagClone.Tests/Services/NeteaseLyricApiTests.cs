using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

/// <summary>
/// 网易云音乐歌词 API 综合测试
///
/// 端点：
///   搜索: POST https://music.163.com/api/cloudsearch/pc
///         body: s=&lt;keyword&gt;&amp;type=1&amp;limit=&lt;n&gt;&amp;offset=0
///         → result.songs[].id
///   歌词: GET http://music.163.com/api/song/lyric?os=pc&amp;id={id}&amp;lv=-1&amp;kv=-1&amp;tv=-1
///         → lrc.lyric (LRC 格式正文), tlyric.lyric (翻译歌词)
///
/// 已知限制：
///   - 周杰伦歌曲因版权问题已于 2018 年从网易云下架，
///     搜索周杰伦返回的结果都是翻唱版本，歌词可能不带 LRC 时间轴。
///     测试使用 Beyond / 陈奕迅 / Taylor Swift 等确认有版权的歌曲。
/// </summary>
public class NeteaseLyricApiTests
{
    #region 单曲歌词直连

    /// <summary>通过已知 song ID 直连获取歌词</summary>
    [Theory]
    [InlineData(2131237571, "谁伴我闯荡")]         // Beyond — 经典粤语
    [InlineData(347230, "海阔天空")]                // Beyond
    [InlineData(66842, "十年")]                     // 陈奕迅
    [InlineData(19292984, "Love Story")]            // Taylor Swift
    [InlineData(449818741, "光年之外")]             // 邓紫棋
    public async Task GetLyricById_已知歌曲_应返回LRC歌词(long songId, string expectedSong)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"http://music.163.com/api/song/lyric?os=pc&id={songId}&lv=-1&kv=-1&tv=-1&rv=-1";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Referer", "https://music.163.com");
            request.Headers.Add("User-Agent", "Mozilla/5.0");

            var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var obj = Newtonsoft.Json.Linq.JObject.Parse(body);

            var code = obj["code"]?.ToObject<int>() ?? -1;
            Assert.Equal(200, code);

            var lrc = obj["lrc"]?["lyric"]?.ToString();
            Assert.False(string.IsNullOrEmpty(lrc), $"lrc.lyric 不应为空 (songId={songId})");
            Assert.True(lrc!.Length > 50, $"歌词长度不足 ({lrc.Length}): songId={songId}");

            // 检查歌词是否有时间轴标记
            var hasTimestamps = lrc.Contains("[00:") || lrc.Contains("[01:") || lrc.Contains("[02:");
            Assert.True(hasTimestamps, $"[{expectedSong}] 歌词没有时间轴标记 (songId={songId})");

            // 检查翻译歌词（可选字段）
            var tlyric = obj["tlyric"]?["lyric"]?.ToString();
            // tlyric 可能为空 — 不强制断言
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"API请求失败 (songId={songId}): {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail($"API请求超时 (songId={songId})");
        }
    }

    /// <summary>验证歌词返回的完整 JSON 结构</summary>
    [Fact]
    public async Task GetLyricById_返回JSON结构完整()
    {
        const long songId = 2131237571;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var url = $"http://music.163.com/api/song/lyric?os=pc&id={songId}&lv=-1&kv=-1&tv=-1";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Referer", "https://music.163.com");
        request.Headers.Add("User-Agent", "Mozilla/5.0");

        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var obj = Newtonsoft.Json.Linq.JObject.Parse(body);

        // 检查标准字段
        Assert.True(obj.ContainsKey("lrc"), "缺少 lrc 字段");
        Assert.True(obj.ContainsKey("sgc"), "缺少 sgc 字段");
        Assert.True(obj.ContainsKey("qfy"), "缺少 qfy 字段");
        Assert.True(obj.ContainsKey("code"), "缺少 code 字段");

        var code = obj["code"]?.ToObject<int>() ?? -1;
        Assert.Equal(200, code);

        var lrcToken = obj["lrc"];
        Assert.NotNull(lrcToken);
        Assert.True(lrcToken!.Type == Newtonsoft.Json.Linq.JTokenType.Object, "lrc 应为对象");
        var lyricText = lrcToken["lyric"]?.ToString() ?? "";
        Assert.True(lyricText.Length > 50, $"歌词正文过短: {lyricText.Length} chars");
    }

    #endregion

    #region 搜索 + 获取歌词（端到端）

    /// <summary>通过 LyricService 搜索网易云歌词</summary>
    [Theory]
    [InlineData("周杰伦", "晴天")]        // 周杰伦在网易云只有翻唱版，但搜索应仍返回结果
    [InlineData("Beyond", "海阔天空")]
    [InlineData("Taylor Swift", "Love Story")]
    public async Task SearchLyricsFromSource_通过LyricService搜索_应返回网易云结果(string artist, string title)
    {
        try
        {
            var mockSettings = new Moq.Mock<ISettingsService>();
            mockSettings.Setup(s => s.ItunesSearchParamsCountry).Returns("CN");
            mockSettings.Setup(s => s.WebSearchItemsLimit).Returns(10);
            var mockLogger = new Moq.Mock<ILoggerService>();
            var httpClientFactoryMock = new Moq.Mock<IHttpClientFactory>();
            httpClientFactoryMock.Setup(f => f.CreateClient("default")).Returns(new HttpClient());
            var svc = new LyricService(httpClientFactoryMock.Object, mockSettings.Object, mockLogger.Object);

            var file = new MusicFile { Artist = artist, Title = title };
            var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 5 };
            var config = new LyricInfo.DownloadConfig();

            var results = await svc.SearchLyricsFromSourceAsync(
                file, "netease", condition, config,
                new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

            Assert.NotEmpty(results);
            Assert.All(results, r =>
            {
                Assert.Equal("网易云音乐", r.SourceName);
                Assert.False(string.IsNullOrEmpty(r.SourceUrl),
                    $"结果 [{r.Title}] 缺少 SourceUrl(歌词URL)");
                Assert.False(string.IsNullOrEmpty(r.Title),
                    "搜索结果缺少 Title");
            });
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"API请求失败 ({artist} - {title}): {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail($"API请求超时 ({artist} - {title})");
        }
    }

    /// <summary>真实验证网易云歌词搜索的 limit/offset 分页。</summary>
    [Fact]
    public async Task SearchLyricsFromSource_Netease_分页返回不同结果()
    {
        try
        {
            var mockSettings = new Moq.Mock<ISettingsService>();
            mockSettings.Setup(s => s.ItunesSearchParamsCountry).Returns("CN");
            mockSettings.Setup(s => s.WebSearchItemsLimit).Returns(10);
            var mockLogger = new Moq.Mock<ILoggerService>();
            var httpClientFactoryMock = new Moq.Mock<IHttpClientFactory>();
            httpClientFactoryMock.Setup(f => f.CreateClient("default")).Returns(new HttpClient());
            var service = new LyricService(
                httpClientFactoryMock.Object, mockSettings.Object, mockLogger.Object);

            var file = new MusicFile();
            var firstPageCondition = new SearchCondition
            {
                CustomQuery = "Beyond",
                WebSearchItemsLimit = 2,
                WebSearchItemsOffset = 0,
            };
            var secondPageCondition = new SearchCondition
            {
                CustomQuery = "Beyond",
                WebSearchItemsLimit = 2,
                WebSearchItemsOffset = 2,
            };
            var config = new LyricInfo.DownloadConfig();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var firstPage = await service.SearchLyricsFromSourceAsync(
                file, "netease", firstPageCondition, config, cts.Token);
            var secondPage = await service.SearchLyricsFromSourceAsync(
                file, "netease", secondPageCondition, config, cts.Token);

            Assert.InRange(firstPage.Count, 1, 2);
            Assert.InRange(secondPage.Count, 1, 2);
            var firstKeys = new HashSet<string>(firstPage.Select(r => r.GetIdentityKey()));
            Assert.DoesNotContain(secondPage, result => firstKeys.Contains(result.GetIdentityKey()));
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"网易云歌词分页 API 请求失败: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("网易云歌词分页 API 请求超时");
        }
    }

    /// <summary>完整流程：搜索 → 获取歌词 → 验证 LRC 格式</summary>
    [Theory]
    [InlineData("谁伴我闯荡")]   // Beyond
    [InlineData("海阔天空")]     // Beyond
    [InlineData("十年")]         // 陈奕迅
    [InlineData("Love Story")]  // Taylor Swift
    public async Task SearchSongAndGetLyric_搜索歌曲并获取歌词_应完整可用(string query)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        try
        {
            // 1. 搜索
            var searchContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("s", query),
                new KeyValuePair<string, string>("type", "1"),
                new KeyValuePair<string, string>("limit", "3"),
                new KeyValuePair<string, string>("offset", "0"),
            });
            var searchReq = new HttpRequestMessage(HttpMethod.Post,
                "https://music.163.com/api/cloudsearch/pc") { Content = searchContent };
            searchReq.Headers.Add("Referer", "https://music.163.com");
            searchReq.Headers.Add("User-Agent", "Mozilla/5.0");

            var searchResp = await http.SendAsync(searchReq);
            var searchBody = await searchResp.Content.ReadAsStringAsync();
            var searchObj = Newtonsoft.Json.Linq.JObject.Parse(searchBody);

            var code = searchObj["code"]?.ToObject<int>() ?? -1;
            Assert.Equal(200, code);

            var songs = searchObj["result"]?["songs"];
            Assert.NotNull(songs);
            Assert.True(songs!.HasValues, $"搜索结果为空: {query}");

            var firstSong = songs[0];
            var idToken = firstSong["id"];
            Assert.NotNull(idToken);
            var songId = (long)idToken!;
            Assert.True(songId > 0, $"songId 无效: {songId}");

            var songName = firstSong["name"]?.ToString() ?? "";
            Assert.NotEmpty(songName);

            // 2. 获取歌词
            var lyricUrl = $"http://music.163.com/api/song/lyric?os=pc&id={songId}&lv=-1&kv=-1&tv=-1&rv=-1";
            var lyricReq = new HttpRequestMessage(HttpMethod.Get, lyricUrl);
            lyricReq.Headers.Add("Referer", "https://music.163.com");
            lyricReq.Headers.Add("User-Agent", "Mozilla/5.0");

            var lyricResp = await http.SendAsync(lyricReq);
            var lyricBody = await lyricResp.Content.ReadAsStringAsync();
            var lyricObj = Newtonsoft.Json.Linq.JObject.Parse(lyricBody);

            var lyricCode = lyricObj["code"]?.ToObject<int>() ?? -1;
            Assert.Equal(200, lyricCode);

            var lrc = lyricObj["lrc"]?["lyric"]?.ToString();
            Assert.False(string.IsNullOrEmpty(lrc), $"[{songName}] 歌词不应为空");
            Assert.True(lrc!.Length > 50, $"[{songName}] 歌词长度不足: {lrc.Length}");

            // 歌词应有时间轴（部分翻唱可能没有，这里只对有版权歌曲严格检查）
            var hasTimestamps = lrc.Contains("[00:") || lrc.Contains("[01:");
            if (hasTimestamps)
            {
                Assert.True(lrc.Count(c => c == '[') >= 5,
                    $"[{songName}] 时间轴行数过少");
            }

            // 3. 验证翻译歌词（可选）
            var tlyric = lyricObj["tlyric"]?["lyric"]?.ToString();
            if (!string.IsNullOrEmpty(tlyric))
            {
                Assert.True(tlyric.Length > 10,
                    $"[{songName}] 翻译歌词过短: {tlyric.Length}");
            }
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"API请求失败 ({query}): {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail($"API请求超时 ({query})");
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Assert.Fail($"JSON解析失败 ({query}): {ex.Message}");
        }
    }

    #endregion

    #region 边缘场景

    /// <summary>不存在的歌曲搜索不应抛异常</summary>
    [Fact]
    public async Task Search_不存在的歌曲_返回空结果()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var searchContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("s", "xxxxxxxxxxyyyyyyyyyyzzzzzzzzzz"),
            new KeyValuePair<string, string>("type", "1"),
            new KeyValuePair<string, string>("limit", "3"),
            new KeyValuePair<string, string>("offset", "0"),
        });
        var searchReq = new HttpRequestMessage(HttpMethod.Post,
            "https://music.163.com/api/cloudsearch/pc") { Content = searchContent };
        searchReq.Headers.Add("Referer", "https://music.163.com");
        searchReq.Headers.Add("User-Agent", "Mozilla/5.0");

        var searchResp = await http.SendAsync(searchReq);
        var searchBody = await searchResp.Content.ReadAsStringAsync();
        var searchObj = Newtonsoft.Json.Linq.JObject.Parse(searchBody);

        Assert.Equal(200, searchObj["code"]?.ToObject<int>() ?? -1);
        // 不存在的歌曲可能返回空数组或者 null
        var songs = searchObj["result"]?["songs"];
        Assert.True(songs == null || songs.Type == Newtonsoft.Json.Linq.JTokenType.Array,
            "songs 应不存在或是数组");
    }

    /// <summary>通过 LyricService 下载歌词验证 LRC 格式</summary>
    [Fact]
    public async Task DownloadLyric_通过LyricService_验证LRC完整()
    {
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.ItunesSearchParamsCountry).Returns("CN");
        mockSettings.Setup(s => s.WebSearchItemsLimit).Returns(10);
        var mockLogger = new Moq.Mock<ILoggerService>();
        var httpClientFactoryMock2 = new Moq.Mock<IHttpClientFactory>();
        httpClientFactoryMock2.Setup(f => f.CreateClient("default")).Returns(new HttpClient());
        var svc = new LyricService(httpClientFactoryMock2.Object, mockSettings.Object, mockLogger.Object);

        var file = new MusicFile { Artist = "Beyond", Title = "海阔天空" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        var config = new LyricInfo.DownloadConfig();

        var results = await svc.SearchLyricsFromSourceAsync(
            file, "netease", condition, config,
            new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

        Assert.NotEmpty(results);

        var first = results[0];
        var lyric = await svc.DownloadLyricAsync(first, config,
            new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

        Assert.NotNull(lyric);
        Assert.NotNull(lyric!.OriginalLyric);
        Assert.Equal("网易云音乐", lyric.SourceName);
        Assert.True(lyric.OriginalLyric!.Length > 50,
            $"下载的歌词过短 ({lyric.OriginalLyric!.Length})");

        // 确认 LRC 格式：有时间轴标记，至少 5 行
        var hasTimestamps = lyric.OriginalLyric.Contains("[00:") || lyric.OriginalLyric.Contains("[01:");
        Assert.True(hasTimestamps, "歌词应含时间轴标记");

        var lrcLineCount = lyric.OriginalLyric.Split('\n')
            .Count(line => line.TrimStart().StartsWith("["));
        Assert.True(lrcLineCount >= 5,
            $"歌词行数过少 ({lrcLineCount})，不符合 LRC 格式");
    }

    #endregion
}
