using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace allyza.Services;

public class FirestoreService : IFirestoreService
{
    private readonly HttpClient _http = new();

    // ── USER PROFILE ─────────────────────────────────────────────────────────
    public async Task<bool> SaveUserProfileAsync(string uid, string idToken, Dictionary<string, object> data)
        => await SetDocumentAsync($"users/{uid}", idToken, data);

    public async Task<Dictionary<string, object>?> GetUserProfileAsync(string uid, string idToken)
        => await GetDocumentAsync($"users/{uid}", idToken);

    // ── SET DOCUMENT ─────────────────────────────────────────────────────────
    public async Task<bool> SetDocumentAsync(string path, string idToken, Dictionary<string, object> data)
    {
        try
        {
            var url = $"{FirebaseConfig.FirestoreBase}/{path}";
            var body = new JObject { ["fields"] = ToFirestoreFields(data) };
            var req = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            var res = await _http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── DELETE DOCUMENT ──────────────────────────────────────────────────────
    public async Task<bool> DeleteDocumentAsync(string path, string idToken)
    {
        try
        {
            var url = $"{FirebaseConfig.FirestoreBase}/{path}";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            var res = await _http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── GET COLLECTION ───────────────────────────────────────────────────────
    public async Task<List<Dictionary<string, object>>> GetCollectionAsync(string path, string idToken)
    {
        var results = new List<Dictionary<string, object>>();
        try
        {
            var url = $"{FirebaseConfig.FirestoreBase}/{path}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return results;

            var json = await res.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json)) return results;

            var root = JObject.Parse(json);
            if (!root.ContainsKey("documents")) return results;

            var docs = root["documents"] as JArray;
            if (docs == null) return results;

            foreach (var doc in docs)
            {
                var fields = FromFirestoreFields(doc["fields"] as JObject);
                if (fields != null) results.Add(fields);
            }
        }
        catch { }
        return results;
    }

    // ── GET OTP DOCUMENT (public, any path) ──────────────────────────────────
    public async Task<Dictionary<string, object>?> GetOtpDocumentAsync(string path, string idToken)
        => await GetDocumentAsync(path, idToken);

    // ── GET SINGLE DOCUMENT (private) ────────────────────────────────────────
    private async Task<Dictionary<string, object>?> GetDocumentAsync(string path, string idToken)
    {
        try
        {
            var url = $"{FirebaseConfig.FirestoreBase}/{path}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            var json = await res.Content.ReadAsStringAsync();
            var doc = JObject.Parse(json);
            return FromFirestoreFields(doc["fields"] as JObject);
        }
        catch { return null; }
    }

    // ── SERIALIZE ────────────────────────────────────────────────────────────
    private static JObject ToFirestoreFields(Dictionary<string, object> data)
    {
        var fields = new JObject();
        foreach (var kv in data)
        {
            fields[kv.Key] = kv.Value switch
            {
                string s => new JObject { ["stringValue"] = s },
                int i => new JObject { ["integerValue"] = i.ToString() },
                long l => new JObject { ["integerValue"] = l.ToString() },
                double d => new JObject { ["doubleValue"] = d },
                bool b => new JObject { ["booleanValue"] = b },
                IEnumerable<string> list => new JObject
                {
                    ["arrayValue"] = new JObject
                    {
                        ["values"] = new JArray(
                            list.Select(s => new JObject { ["stringValue"] = s }))
                    }
                },
                _ => new JObject { ["stringValue"] = kv.Value?.ToString() ?? "" }
            };
        }
        return fields;
    }

    // ── DESERIALIZE ──────────────────────────────────────────────────────────
    private static Dictionary<string, object>? FromFirestoreFields(JObject? fields)
    {
        if (fields is null) return null;
        var result = new Dictionary<string, object>();
        foreach (var prop in fields.Properties())
        {
            if (prop.Value is not JObject v) continue;
            if (v["stringValue"] != null) result[prop.Name] = (string)v["stringValue"]!;
            else if (v["doubleValue"] != null) result[prop.Name] = (double)v["doubleValue"]!;
            else if (v["integerValue"] != null)
            {
                var raw = (string)v["integerValue"]!;
                result[prop.Name] = double.TryParse(raw, out var d) ? d : 0.0;
            }
            else if (v["booleanValue"] != null) result[prop.Name] = (bool)v["booleanValue"]!;
            else if (v["timestampValue"] != null) result[prop.Name] = (string)v["timestampValue"]!;
            else if (v["arrayValue"] != null)
            {
                var values = v["arrayValue"]!["values"] as JArray;
                var list = new List<string>();
                if (values != null)
                    foreach (var item in values)
                        if (item["stringValue"] != null)
                            list.Add((string)item["stringValue"]!);
                result[prop.Name] = list;
            }
        }
        return result;
    }
}