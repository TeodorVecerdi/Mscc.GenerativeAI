namespace Mscc.GenerativeAI
{
    public sealed class VertexAICustomUrl
    {
        public string ProjectId { get; }
        public string Url { get; }
        public string? ApiKey { get; }
        public string? Region { get; }

        public VertexAICustomUrl(string projectId, string url, string? apiKey = null, string? region = null)
        {
            ProjectId = projectId;
            Url = url;
            ApiKey = apiKey;
            Region = region;
        }
    }
}