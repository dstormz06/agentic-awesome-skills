using System.Net.Http.Headers;

namespace AndaCompanion.Services;

/// <summary>
/// Fetches a newer Drugs@FDA data file when the person presses Update, from an https address they supply. Never automatic, never
/// in the background. The download is checked as a real Drugs@FDA file before it replaces anything, so a wrong address or a web
/// page can never become the reference data. This app does not know or guess the address; it is the file link published on
/// FDA's "Drugs@FDA data files" page.
/// </summary>
public static class ReferenceDownloader
{
    public const long MaxBytes = 250L * 1024 * 1024;

    public static async Task<(bool Ok, string Message)> UpdateAsync(string? url, string destination, HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        string address = (url ?? "").Trim();
        if (address.Length == 0) return (false, "Paste the address of the Drugs@FDA data file first. Nothing was fetched.");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            return (false, "The address must start with https:// (http:// is accepted only for localhost). Nothing was fetched.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) return (false, "Do not put a user name or password in the address. Nothing was fetched.");

        string temp = destination + ".part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var http = new HttpClient(handler ?? new HttpClientHandler { MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AndaHoverCompanion", "0"));   // generic: no account or address is ever sent
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (false, $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}. Your earlier file is unchanged.");
            if (response.Content.Headers.ContentLength is > MaxBytes) return (false, "That file is larger than 250 MB, so it was not downloaded. Your earlier file is unchanged.");

            long total = 0;
            await using (var src = await response.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(temp))
            {
                var buffer = new byte[81920]; int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    total += n;
                    if (total > MaxBytes) return (false, "That file is larger than 250 MB, so it was not kept. Your earlier file is unchanged.");
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            if (DrugsAtFdaFile.TryLoad(temp, out string message) is null)
                return (false, "That address did not give a Drugs@FDA data file: " + message + " Your earlier file is unchanged.");
            File.Move(temp, destination, overwrite: true);
            File.SetLastWriteTime(destination, DateTime.Now);
            var loaded = DrugsAtFdaFile.TryLoad(destination, out string ok)!;
            return (true, "Updated. " + ok);
        }
        catch (OperationCanceledException) { return (false, "Cancelled. Your earlier file is unchanged."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return (false, $"Could not fetch the file ({ex.GetType().Name}). Your earlier file is unchanged.");
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }
}
