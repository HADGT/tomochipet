using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;

namespace qlthucung.Services.chat
{
    public class TfidfIndexer
    {
        private readonly string _path;
        private readonly AppDbContext _context;

        // id -> token -> tfidf weight
        private Dictionary<string, Dictionary<string, double>> _docVectors = new();
        // token -> idf
        private Dictionary<string, double> _idf = new();
        // doc id -> metadata type ("P" or "S" or "Q") and numeric id
        private Dictionary<string, (string type, int id)> _docMeta = new();
        // raw text for each doc id (used to rebuild index and to allow incremental adds)
        private Dictionary<string, string> _docTexts = new();

        public bool IsReady { get; private set; } = false;

        public TfidfIndexer(string path, AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        private static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            // note: keep tokens distinct to stay consistent with previous logic;
            // you can remove Distinct() to use true term frequency if desired.
            return text
                .ToLowerInvariant()
                .Replace(",", " ")
                .Replace(".", " ")
                .Replace("-", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();
        }

        // Build index from DB data (products + services)
        public async Task BuildIndexAsync()
        {
            // load raw texts from DB into _docTexts and _docMeta
            _docTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _docMeta = new Dictionary<string, (string type, int id)>();

            var products = await _context.SanPhams.ToListAsync();
            var services = await _context.SPDichVu.ToListAsync();

            foreach (var p in products)
            {
                var docId = $"P_{p.Masp}";
                var text = $"{p.Tensp} {p.Mota}";
                _docTexts[docId] = text ?? "";
                _docMeta[docId] = ("P", Convert.ToInt32(p.Masp));
            }

            foreach (var s in services)
            {
                var docId = $"S_{s.DichVuID}";
                var text = $"{s.TenDichVu} {s.MoTa}";
                _docTexts[docId] = text ?? "";
                _docMeta[docId] = ("S", Convert.ToInt32(s.DichVuID));
            }

            RebuildFromTexts();
        }

        // Add an in-memory temporary document and rebuild index (best-effort, in-memory).
        // If persist == true the index will be saved to disk.
        public async Task AddTemporaryDocumentAsync(string docId, string text, bool persist = false)
        {
            if (string.IsNullOrWhiteSpace(docId))
                docId = "Q_" + Guid.NewGuid().ToString("N");

            // ensure base index exists (load from disk or build from DB) before adding
            if ((_docTexts == null || _docTexts.Count == 0) && File.Exists(_path))
            {
                await LoadAsync();
            }

            if (_docTexts == null || _docTexts.Count == 0)
            {
                // try to build from DB
                await BuildIndexAsync();
            }

            // add temporary doc
            _docTexts[docId] = text ?? "";
            _docMeta[docId] = ("Q", 0);

            // rebuild vectors using updated _docTexts
            RebuildFromTexts();

            if (persist)
            {
                await SaveAsync();
            }
        }

        // Rebuild internal structures (idf, docVectors) from _docTexts
        private void RebuildFromTexts()
        {
            _docVectors = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            _idf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            var docs = _docTexts.Select(kvp => (docId: kvp.Key, text: kvp.Value)).ToList();
            int N = docs.Count;
            if (N == 0)
            {
                IsReady = false;
                return;
            }

            // Document frequencies
            var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var docTermCounts = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (docId, text) in docs)
            {
                var tokens = Tokenize(text);
                var termCounts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in tokens)
                {
                    if (!termCounts.ContainsKey(t))
                        termCounts[t] = 0;
                    termCounts[t] += 1;
                }

                foreach (var t in termCounts.Keys.Distinct())
                {
                    df[t] = df.ContainsKey(t) ? df[t] + 1 : 1;
                }

                docTermCounts[docId] = termCounts;
            }

            // compute idf (with smoothing)
            foreach (var kv in df)
            {
                _idf[kv.Key] = Math.Log((double)N / (1 + kv.Value)) + 1.0;
            }

            // build tf-idf vectors and normalize
            foreach (var docId in docTermCounts.Keys)
            {
                var vec = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var termCounts = docTermCounts[docId];

                double sumSquares = 0;
                foreach (var kv in termCounts)
                {
                    var tf = kv.Value;
                    var idfVal = _idf.ContainsKey(kv.Key) ? _idf[kv.Key] : 0.0;
                    var w = tf * idfVal;
                    vec[kv.Key] = w;
                    sumSquares += w * w;
                }

                var norm = sumSquares > 0 ? Math.Sqrt(sumSquares) : 1.0;
                var normalized = vec.ToDictionary(k => k.Key, k => k.Value / norm, StringComparer.OrdinalIgnoreCase);
                _docVectors[docId] = normalized;
            }

            IsReady = _docVectors.Count > 0 && _idf.Count > 0;
        }

        // Query returns topN doc ids + score
        public List<(string docId, double score)> Query(string text, int topN = 5)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(text))
                return new List<(string, double)>();

            var tokens = Tokenize(text);
            var qCounts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tokens)
                qCounts[t] = qCounts.ContainsKey(t) ? qCounts[t] + 1 : 1;

            // build query vector tf-idf
            var qVec = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double sumSquares = 0;
            foreach (var kv in qCounts)
            {
                if (!_idf.TryGetValue(kv.Key, out var idfVal))
                    continue;
                var w = kv.Value * idfVal;
                qVec[kv.Key] = w;
                sumSquares += w * w;
            }

            if (qVec.Count == 0)
                return new List<(string, double)>();

            var qNorm = Math.Sqrt(sumSquares);
            var qNormed = qVec.ToDictionary(k => k.Key, k => k.Value / qNorm, StringComparer.OrdinalIgnoreCase);

            var scores = new List<(string docId, double score)>();

            foreach (var doc in _docVectors)
            {
                double dot = 0;
                foreach (var kv in qNormed)
                {
                    if (doc.Value.TryGetValue(kv.Key, out var w))
                        dot += kv.Value * w;
                }
                if (dot > 0)
                    scores.Add((doc.Key, dot));
            }

            return scores.OrderByDescending(x => x.score).Take(topN).ToList();
        }

        public (string type, int id)? GetMeta(string docId)
        {
            if (_docMeta.TryGetValue(docId, out var m))
                return m;
            return null;
        }

        // Save/load index to disk (includes raw texts so we can rebuild later or add incremental docs)
        public async Task SaveAsync()
        {
            var dto = new
            {
                Idf = _idf,
                DocVectors = _docVectors,
                DocMeta = _docMeta,
                DocTexts = _docTexts
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(dto, options);
            await File.WriteAllTextAsync(_path, json);
        }

        public async Task LoadAsync()
        {
            if (!File.Exists(_path))
            {
                IsReady = false;
                return;
            }

            var json = await File.ReadAllTextAsync(_path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // idf
            _idf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("Idf", out var idfEl))
            {
                foreach (var p in idfEl.EnumerateObject())
                {
                    _idf[p.Name] = p.Value.GetDouble();
                }
            }

            _docVectors = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("DocVectors", out var dvEl))
            {
                foreach (var docP in dvEl.EnumerateObject())
                {
                    var inner = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var t in docP.Value.EnumerateObject())
                        inner[t.Name] = t.Value.GetDouble();
                    _docVectors[docP.Name] = inner;
                }
            }

            _docMeta = new Dictionary<string, (string type, int id)>();
            if (root.TryGetProperty("DocMeta", out var metaEl))
            {
                foreach (var p in metaEl.EnumerateObject())
                {
                    var type = p.Value.GetProperty("type").GetString() ?? "P";
                    var id = p.Value.GetProperty("id").GetInt32();
                    _docMeta[p.Name] = (type, id);
                }
            }

            _docTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("DocTexts", out var textsEl))
            {
                foreach (var p in textsEl.EnumerateObject())
                {
                    _docTexts[p.Name] = p.Value.GetString() ?? "";
                }
            }

            IsReady = _docVectors.Count > 0 && _idf.Count > 0;
        }
    }
}