using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;

namespace WPF
{
    public class ChatHandler : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _systemPrompt, _roslynValidatorPrompt;
        private CancellationTokenSource _appCts = new();

        // ====== CHAT SETTINGS ======
        private static string ApiKey => LoadKey();
        
        private static string LoadKey()
            {
                try
                {
                    // Ścieżka do pliku w folderze uruchomieniowym (.exe)
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apikey.txt");
        
                    if (File.Exists(path))
                    {
                        return File.ReadAllText(path).Trim();
                    }

  if (!File.Exists(path)) throw new FileNotFoundException($"Nie znaleziono pliku klucza API: {path}");
                    // Pomocniczo: szukamy o 3 foldery wyżej (tam gdzie jest kod źródłowy podczas debugowania)
                    string projectPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "apikey.txt");
                    if (File.Exists(projectPath))
                    {
                        return File.ReadAllText(projectPath).Trim();
                    }
        
                    return "MISSING_KEY"; 
                }
                catch (Exception)
                {
                    return "ERROR_READING_KEY";
                }
            }

        private const string ModelId = "gpt-4o-mini";
        private const string Url     = "https://api.openai.com/v1/chat/completions";
        // ==================================

        public ChatHandler()
        {
            _httpClient = new HttpClient();
            _systemPrompt = LoadSystemPromptWithDynamicExamples();
            _roslynValidatorPrompt = LoadRoslynValidatorPrompt();
        }

        private static string LoadRoslynValidatorPrompt()
        {
            var promptPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Prompt",
                "checkRoslyn.txt"
            );
System.Console.WriteLine(   $"Ładowanie promptu walidatora Roslyn z: {promptPath}");
            if (!File.Exists(promptPath)) throw new FileNotFoundException($"Nie znaleziono pliku promptu: {promptPath}");

            return File.ReadAllText(promptPath);
        }
        private static string LoadSystemPrompt()
        {
            var promptPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Prompt",
                "pascal_to_csharp.txt"
            );

            if (!File.Exists(promptPath)) throw new FileNotFoundException($"Nie znaleziono pliku promptu: {promptPath}");

            return File.ReadAllText(promptPath);
        }
        
private static string LoadSystemPromptWithDynamicExamples()
{
    // 1) BASE PROMPT
    var basePrompt = LoadSystemPrompt();

    // 2) GET RAW JSONs from Prompt/Examples
    var exampleJsonBlocks = LoadJsonExamplesFromFolder();
    if (exampleJsonBlocks.Count == 0) return basePrompt;

    // 3) INJECT JSONs to <prompt_examples>...</prompt_examples>
    return InjectExamplesIntoPromptExamples(basePrompt, exampleJsonBlocks);
}

        private static List<string> LoadJsonExamplesFromFolder()
        {
            var list = new List<string>();

            try
            {
                var dir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Prompt",
                    "Examples"
                );

                if (!Directory.Exists(dir))
                    return list;

                var files = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
                                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    var text = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    try
                    {
                        using var _ = JsonDocument.Parse(text);
                        list.Add(text.Trim());
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch
            {}

            return list;
        }

        private static string InjectExamplesIntoPromptExamples(string basePrompt, List<string> jsonBlocks)
        {
            const string openTag = "<prompt_examples>";
            const string closeTag = "</prompt_examples>";

            var openIdx = basePrompt.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
            var closeIdx = basePrompt.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);

            static string JoinBlocks(IEnumerable<string> blocks)
                => string.Join("\n\n", blocks) + "\n";

            if (openIdx >= 0 && closeIdx > openIdx)
            {
                var insertPos = closeIdx;
                var insertion = JoinBlocks(jsonBlocks);

                var sb = new StringBuilder(basePrompt.Length + insertion.Length + 64);
                sb.Append(basePrompt, 0, insertPos);
                if (insertPos > 0 && basePrompt[insertPos - 1] != '\n')
                    sb.Append('\n');

                sb.Append(insertion);
                sb.Append(basePrompt, insertPos, basePrompt.Length - insertPos);
                return sb.ToString();
            }
            else
            {
                var sb = new StringBuilder(basePrompt.Length + jsonBlocks.Sum(b => b.Length + 4) + 64);
                sb.Append(basePrompt);
                if (!basePrompt.EndsWith("\n"))
                    sb.Append('\n');

                sb.AppendLine(openTag);
                sb.Append(JoinBlocks(jsonBlocks));
                sb.AppendLine(closeTag);
                return sb.ToString();
            }
        }

        public void Cancel()
        {
            try
            {
                _httpClient.CancelPendingRequests();
                _appCts.Cancel();
            }
            catch {}
        }
        private CancellationToken GetFreshToken()
        {
            if (_appCts is { IsCancellationRequested: true })
            {
                _appCts.Dispose();
                _appCts = new CancellationTokenSource();
            }
            return _appCts.Token;
        }

public async Task<string> ValidateAndFixCSharpCodeAsync(string generatedCode)
{
    if (string.IsNullOrWhiteSpace(generatedCode)) return string.Empty;

    var request = new
    {
        model = ModelId,
        messages = new object[]
        {
            new { role = "system", content = _roslynValidatorPrompt },
            new { role = "user", content = $"Sprawdź i popraw następujący kod C#:\n\n{generatedCode}" }
        },
        temperature = 0.1 
    };

    var json = JsonSerializer.Serialize(request);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");
    
    using var requestMessage = new HttpRequestMessage(HttpMethod.Post, Url) { Content = content };
    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

    try
    {
        var response = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(responseString);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        return ExtractCode(message);
    }
    catch (Exception)
    {
        return generatedCode; 
    }
}

public async Task<string> FullPascalToValidatedCSharpAsync(string pascalCode)
{
    // Krok 1: Konwersja z Pascala na C#
    string rawCSharp = await ConvertPascalToCSharpAsync(pascalCode);
    
    if (string.IsNullOrEmpty(rawCSharp)) return "Błąd konwersji.";

    // Krok 2: Walidacja semantyczna i poprawki "Roslyn" przez AI
    string validatedCSharp = await ValidateAndFixCSharpCodeAsync(rawCSharp);

    return validatedCSharp;
}
public async Task<string> ConvertPascalToCSharpAsync(string pascalCode)
        {
            if (string.IsNullOrWhiteSpace(pascalCode))
                throw new ArgumentException("Kod Pascala jest pusty.");

            var request = new
            {
                model = ModelId,
                messages = new object[]
                {
                    new { role = "system", content = _systemPrompt },
                    new { role = "user", content = BuildUserPrompt(pascalCode) }
                },
                temperature = 0.1
            };

            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_appCts.Token);
            var token = cts.Token;

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, Url)
            {
                Content = content,
                Version = System.Net.HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            requestMessage.Headers.ConnectionClose = true;
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;

            try
            {
                using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var responseString = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(responseString);
                var message = doc.RootElement
                                 .GetProperty("choices")[0]
                                 .GetProperty("message")
                                 .GetProperty("content")
                                 .GetString();

                return ExtractCode(message);
            }
            catch (OperationCanceledException) { return string.Empty; }
        }



private static string BuildUserPrompt(string pascalCode)
{
    return $"<source_code>\n{pascalCode}\n</source_code>";
}

        private static string ExtractCode(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;

            try
            {
                using var json = JsonDocument.Parse(content);
                return json.RootElement.GetProperty("code").GetString() ?? "";
            }
            catch
            {
                return content.Trim();
            }
        }

        public void Dispose()
        {
            try
            {
                _httpClient.CancelPendingRequests();
                _appCts.Cancel();
            }
            catch {}
            finally
            {
                _appCts.Dispose();
                _httpClient.Dispose();
            }
        }
    }
}