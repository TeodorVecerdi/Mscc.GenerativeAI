#if NET472_OR_GREATER || NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Threading;
#endif
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;

namespace Mscc.GenerativeAI
{
    public abstract class BaseModel : BaseLogger
    {
        protected const string BaseUrlGoogleAi = "https://generativelanguage.googleapis.com/{version}";
        protected const string BaseUrlVertexAi = "https://{region}-aiplatform.googleapis.com/{version}/projects/{projectId}/locations/{region}";

        protected readonly string _publisher = "google";
        protected readonly JsonSerializerOptions _readOptions;
        protected readonly JsonSerializerOptions _writeOptions;

        protected string _model;
        protected string? _apiKey;
        protected string _apiVersion;
        protected string? _accessToken;
        protected string? _projectId;
        protected string _region = "us-central1";
        protected string? _endpointId;
        protected string? _customUrlVertexAi;

#if NET472_OR_GREATER || NETSTANDARD2_0
        protected static readonly Version _httpVersion = HttpVersion.Version11;
        protected static readonly HttpClient Client = new HttpClient(new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12
        });
#else
        protected static readonly Version _httpVersion = HttpVersion.Version11;
        protected static readonly HttpClient Client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(30),
            EnableMultipleHttp2Connections = true
        })
        {
            DefaultRequestVersion = _httpVersion,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
        };
#endif

        internal virtual string Version
        {
            get => _apiVersion;
            set => _apiVersion = value;
        }

        /// <summary>
        /// Gets or sets the name of the model to use.
        /// </summary>
        public string Model
        {
            get => _model;
            set => _model = value.SanitizeModelName();
        }

        /// <summary>
        /// Returns the name of the model. 
        /// </summary>
        /// <returns>Name of the model.</returns>
        public string Name => _model;

        /// <summary>
        /// Sets the API key to use for the request.
        /// </summary>
        /// <remarks>
        /// The value can only be set or modified before the first request is made.
        /// </remarks>
        public string? ApiKey { set => _apiKey = value.GuardApiKey(); }

        /// <summary>
        /// Specify API key in HTTP header
        /// </summary>
        /// <seealso href="https://cloud.google.com/docs/authentication/api-keys-use#using-with-rest">Using an API key with REST</seealso>
        /// <param name="request"><see cref="HttpRequestMessage"/> to send to the API.</param>
        protected virtual void AddApiKeyHeader(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_apiKey))
            {
                if (request.Headers.Contains("x-goog-api-key"))
                    request.Headers.Remove("x-goog-api-key");
                request.Headers.Add("x-goog-api-key", _apiKey);

                if (request.Headers.Contains("x-api-key"))
                    request.Headers.Remove("x-api-key");
                request.Headers.Add("x-api-key", _apiKey);
            }
        }

        /// <summary>
        /// Sets the access token to use for the request.
        /// </summary>
        public string? AccessToken { set => _accessToken = value; }

        protected virtual void AddAccessTokenHeader(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_accessToken))
            {
                if (request.Headers.Contains("Authorization"))
                {
                    request.Headers.Remove("Authorization");
                }
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }
        }

        /// <summary>
        /// Sets the project ID to use for the request.
        /// </summary>
        /// <remarks>
        /// The value can only be set or modified before the first request is made.
        /// </remarks>
        public string? ProjectId { set => _projectId = value; }

        protected virtual void AddProjectIdHeader(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_projectId))
            {
                if (request.Headers.Contains("x-goog-user-project"))
                {
                    request.Headers.Remove("x-goog-user-project");
                }
                request.Headers.Add("x-goog-user-project", _projectId);
            }
        }

        /// <summary>
        /// Returns the region to use for the request.
        /// </summary>
        public string Region
        {
            get => _region;
            set => _region = value;
        }

        /// <summary>
        /// Gets or sets the timespan to wait before the request times out.
        /// </summary>
        public TimeSpan Timeout
        {
            get => Client.Timeout;
            set => Client.Timeout = value;
        }

        /// <summary>
        /// Throws a <see cref="NotSupportedException"/>, if the functionality is not supported by combination of settings.
        /// </summary>
        protected virtual void ThrowIfUnsupportedRequest<T>(T request) { }

        // Instance fields for default headers
        private readonly ProductInfoHeaderValue _defaultUserAgent;
        private readonly KeyValuePair<string, string> _defaultApiClientHeader;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="logger">Optional. Logger instance used for logging</param>
        public BaseModel(ILogger? logger = null) : base(logger)
        {
            // Initialize the default headers in constructor
            var productHeaderValue = new ProductHeaderValue(
                name: Assembly.GetExecutingAssembly().GetName().Name ?? "Mscc.GenerativeAI",
                version: Assembly.GetExecutingAssembly().GetName().Version?.ToString());
            _defaultUserAgent = new ProductInfoHeaderValue(productHeaderValue);
            _defaultApiClientHeader = new KeyValuePair<string, string>(
                "x-goog-api-client",
                _defaultUserAgent.ToString());
            _readOptions = DefaultReadJsonSerializerOptions();
            _writeOptions = DefaultWriteJsonSerializerOptions();

            GenerativeAIExtensions.ReadDotEnv();
            ApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ??
                     Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            AccessToken = Environment.GetEnvironmentVariable("GOOGLE_ACCESS_TOKEN"); // ?? GetAccessTokenFromAdc();
            Model = Environment.GetEnvironmentVariable("GOOGLE_AI_MODEL") ??
                    GenerativeAI.Model.Gemini15Pro;
            _projectId = Environment.GetEnvironmentVariable("GOOGLE_PROJECT_ID") ??
                         Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
            _region = Environment.GetEnvironmentVariable("GOOGLE_REGION") ??
                      Environment.GetEnvironmentVariable("GOOGLE_CLOUD_LOCATION") ?? _region;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="region"></param>
        /// <param name="model"></param>
        /// <param name="logger">Optional. Logger instance used for logging</param>
        public BaseModel(string? projectId = null, string? region = null,
            string? model = null, ILogger? logger = null) : this(logger)
        {
            AccessToken = _accessToken;
            ProjectId = projectId ?? _projectId;
            _region = region ?? _region;
            Model = model ?? _model;
        }

        /// <summary>
        /// Parses the URL template and replaces the placeholder with current values.
        /// Given two API endpoints for Google AI Gemini and Vertex AI Gemini this
        /// method uses regular expressions to replace placeholders in a URL template with actual values.
        /// </summary>
        /// <param name="url">API endpoint to parse.</param>
        /// <param name="method">Method part of the URL to inject</param>
        /// <returns></returns>
        protected string ParseUrl(string url, string method = "")
        {
            var replacements = GetReplacements();
            replacements.Add("method", method);

            do
            {
                url = Regex.Replace(url, @"\{(?<name>.*?)\}",
                    match => replacements.TryGetValue(match.Groups["name"].Value, out var value) ? value : "");
            } while (url.Contains("{"));

            return url;

            Dictionary<string, string> GetReplacements()
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "BaseUrlGoogleAi", BaseUrlGoogleAi },
                    { "BaseUrlVertexAi", BaseUrlVertexAi },
                    { "CustomBaseUrl", _customUrlVertexAi },
                    { "version", Version },
                    { "model", _model },
                    { "modelsId", _model },
                    { "apikey", _apiKey ?? "" },
                    { "projectId", _projectId ?? "" },
                    { "region", _region },
                    { "location", _region },
                    { "publisher", _publisher },
                    { "endpointId", _endpointId }
                };
            }
        }

        /// <summary>
        /// Return serialized JSON string of request payload.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        protected string Serialize<T>(T request)
        {
            var json = JsonSerializer.Serialize(request, _writeOptions);

            Logger.LogJsonRequest(json);

            return json;
        }

        /// <summary>
        /// Return deserialized object from JSON response.
        /// </summary>
        /// <typeparam name="T">Type to deserialize response into.</typeparam>
        /// <param name="response">Response from an API call in JSON format.</param>
        /// <returns>An instance of type T.</returns>
        protected async Task<T> Deserialize<T>(HttpResponseMessage response)
        {
            var json = await response.Content.ReadAsStringAsync();

            Logger.LogJsonResponse(json);

#if NET472_OR_GREATER || NETSTANDARD2_0
            return JsonSerializer.Deserialize<T>(json, _readOptions);
#else
            return await response.Content.ReadFromJsonAsync<T>(_readOptions);
#endif
        }

        /// <summary>
        /// Get default options for JSON serialization.
        /// </summary>
        /// <returns>default options for JSON serialization.</returns>
        private JsonSerializerOptions DefaultWriteJsonSerializerOptions()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
#if DEBUG
                WriteIndented = true,
#endif
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
#if NET9_0_OR_GREATER
                RespectNullableAnnotations = true
#endif
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
            options.Converters.Add(new DateTimeFormatJsonConverter());

            return options;
        }
        private JsonSerializerOptions DefaultReadJsonSerializerOptions()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
#if DEBUG
                WriteIndented = true,
#endif
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
#if NET9_0_OR_GREATER
                RespectNullableAnnotations = true
#endif
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
            options.Converters.Add(new DateTimeFormatJsonConverter());

            return options;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <param name="url"></param>
        /// <param name="method"></param>
        /// <param name="completionOption"></param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        protected async Task<TResponse> PostAsync<TRequest, TResponse>(TRequest request,
            string url, string method,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var requestUri = ParseUrl(url, method);
            var json = Serialize(request);
            var payload = new StringContent(json, Encoding.UTF8, Constants.MediaType);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
            httpRequest.Content = payload;
            var response = await SendAsync(httpRequest, cancellationToken, completionOption);
            await response.EnsureSuccessAsync();
            return await Deserialize<TResponse>(response);
        }

        protected async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken = default,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
        {
            // Add auth headers specific to this request
            AddApiKeyHeader(request);
            AddAccessTokenHeader(request);
            AddProjectIdHeader(request);

            // Add instance default headers
            request.Headers.UserAgent.Add(_defaultUserAgent);
            request.Headers.Add(_defaultApiClientHeader.Key, _defaultApiClientHeader.Value);

            Logger.LogParsedRequest(
                request.Method,
                request.RequestUri,
                $"{request.Headers.ToFormattedString()}{(request.Content is null ? string.Empty : request.Content.Headers.ToFormattedString())}",
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync()
            );

            return await Client.SendAsync(request, completionOption, cancellationToken);
        }
    }
}