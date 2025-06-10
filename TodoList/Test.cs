using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text.Json; // Или using Newtonsoft.Json;

public class FirestoreRestClient
{
    private readonly HttpClient _httpClient;
    private readonly string _projectId;
    private readonly string _databaseId; // Обычно "(default)"
    private readonly string _baseUrl = "https://firestore.googleapis.com/v1/";

    public FirestoreRestClient(string projectId, string databaseId = "(default)")
    {
        _projectId = projectId;
        _databaseId = databaseId;
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(_baseUrl);
    }

    // Метод для установки токена аутентификации перед запросом
    public void SetAuthToken(string firebaseIdToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", firebaseIdToken);
    }

    // Пример асинхронного метода для получения документа
    public async Task<JsonDocument> GetDocumentAsync(string collectionId, string documentId)
    {
        if (_httpClient.DefaultRequestHeaders.Authorization == null)
        {
            throw new UnauthorizedAccessException("Authentication token is not set.");
        }

        string requestUri = $"projects/{_projectId}/databases/{_databaseId}/documents/{collectionId}/{documentId}";

        var response = await _httpClient.GetAsync(requestUri);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Invalid or expired authentication token.");
        }

        response.EnsureSuccessStatusCode();
        string jsonResponse = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(jsonResponse);
    }

}
