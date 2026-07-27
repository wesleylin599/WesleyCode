using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Services;

internal sealed class NetworkRequestProvider : AIContextProvider
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NetworkRequestProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(
            new AIContext
            {
                Instructions = """
                ## Network Request
                你可以使用 `network_request` 发起网络请求。
                当需要请求 HTTP 或 HTTPS 接口时，优先使用这个工具。
                支持自定义 method、headers、body、content_type 和超时。
                默认会返回精简后的响应结果，包括状态码、响应头和响应体；失败信息也放在响应体中。
                """,
                Tools =
                [
                    AIFunctionFactory.Create(
                        NetworkRequestAsync,
                        new AIFunctionFactoryOptions { Name = "network_request", Description = "发送 HTTP/HTTPS 网络请求" }
                    ),
                ],
            }
        );
    }

    private async Task<HttpResponseResult> NetworkRequestAsync(
        [Description("请求地址")] string url,
        [Description("请求方式")] string method,
        [Description("请求正文")] string? body,
        [Description("请求头")] Dictionary<string, string>? headers,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new HttpResponseResult { StatusCode = 400, Body = "Url 不能为空。" };
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var requestUri))
        {
            return new HttpResponseResult { StatusCode = 400, Body = $"Url 无效：{url}" };
        }

        if (
            !string.Equals(requestUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        )
        {
            return new HttpResponseResult { StatusCode = 400, Body = $"仅支持 HTTP/HTTPS 请求：{url}" };
        }

        var methodName = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant();

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(methodName), requestUri);
            request.Content ??= new StringContent(body ?? string.Empty, Encoding.UTF8);
            if (headers is not null)
            {
                foreach (var header in headers)
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }

            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var resultHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
            {
                resultHeaders[header.Key] = header.Value.ToArray();
            }
            foreach (var header in response.Content.Headers)
            {
                resultHeaders[header.Key] = header.Value.ToArray();
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseResult
            {
                StatusCode = (int)response.StatusCode,
                Headers = resultHeaders,
                Body = responseBody,
            };
        }
        catch (Exception ex)
        {
            return new HttpResponseResult { StatusCode = 500, Body = $"请求失败：{ex.GetBaseException().Message}" };
        }
    }

    sealed class HttpResponseResult
    {
        [JsonPropertyName("status_code")]
        public int StatusCode { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string[]> Headers { get; set; } = [];

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;
    }
}
