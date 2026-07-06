using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeVoiceStudio;

public class EngineDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("tier")] public int Tier { get; set; }
    [JsonPropertyName("clones")] public bool Clones { get; set; }
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";
    [JsonPropertyName("sec_per_word")] public double SecPerWord { get; set; }
}

public class VoiceDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("seconds")] public double Seconds { get; set; }
    [JsonPropertyName("transcript")] public string Transcript { get; set; } = "";
}

public class JobDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("status_text")] public string StatusText { get; set; } = "";
    [JsonPropertyName("done")] public int Done { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("eta_seconds")] public int? EtaSeconds { get; set; }
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("words")] public int Words { get; set; }
}

public class OutputDto
{
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("mtime")] public double Mtime { get; set; }
    [JsonPropertyName("size_kb")] public long SizeKb { get; set; }
}

public class StateDto
{
    [JsonPropertyName("engines")] public List<EngineDto> Engines { get; set; } = new();
    [JsonPropertyName("effects")] public List<string> Effects { get; set; } = new();
    [JsonPropertyName("voices")] public List<VoiceDto> Voices { get; set; } = new();
    [JsonPropertyName("kokoro_presets")] public List<string> KokoroPresets { get; set; } = new();
    [JsonPropertyName("jobs")] public List<JobDto> Jobs { get; set; } = new();
    [JsonPropertyName("outputs")] public List<OutputDto> Outputs { get; set; } = new();
}

/// <summary>Thin async client for the local FreeVoice engine server.</summary>
public sealed class ServerClient
{
    public const string BaseUrl = "http://127.0.0.1:7899";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<StateDto?> GetStateAsync()
    {
        try
        {
            return await Http.GetFromJsonAsync<StateDto>($"{BaseUrl}/api/state");
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Ok, string Message)> GenerateAsync(object payload)
    {
        try
        {
            var resp = await Http.PostAsJsonAsync($"{BaseUrl}/api/generate", payload);
            if (resp.IsSuccessStatusCode) return (true, "queued");
            var body = await resp.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(body);
                return (false, doc.RootElement.GetProperty("error").GetString() ?? body);
            }
            catch { return (false, body); }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public Task CancelJobAsync(string id)
        => Http.PostAsync($"{BaseUrl}/api/job/{id}/cancel", null);

    public Task RemoveJobAsync(string id)
        => Http.DeleteAsync($"{BaseUrl}/api/job/{id}");

    public async Task<(bool Ok, string Message)> AddVoiceAsync(string filePath, string name, string transcript)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(filePath);
            var fileContent = new ByteArrayContent(bytes);
            form.Add(fileContent, "file", Path.GetFileName(filePath));
            form.Add(new StringContent(name), "name");
            form.Add(new StringContent(transcript), "transcript");
            var resp = await Http.PostAsync($"{BaseUrl}/api/voices", form);
            if (resp.IsSuccessStatusCode) return (true, "saved");
            var body = await resp.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(body);
                return (false, doc.RootElement.GetProperty("error").GetString() ?? body);
            }
            catch { return (false, body); }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public Task DeleteVoiceAsync(string name)
        => Http.DeleteAsync($"{BaseUrl}/api/voices/{Uri.EscapeDataString(name)}");

    public Task DeleteOutputAsync(string file)
        => Http.DeleteAsync($"{BaseUrl}/api/outputs/{Uri.EscapeDataString(file)}");

    public Task OpenFolderAsync()
        => Http.PostAsync($"{BaseUrl}/api/open-folder", null);
}
