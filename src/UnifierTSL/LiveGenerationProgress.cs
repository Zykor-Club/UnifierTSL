using System.Text.Json;
using Terraria;
using Terraria.WorldBuilding;

namespace UnifierTSL
{
    /// <summary>
    /// 把"正在生成沙漠 / 正在清理树 / 正在放置陷阱"这类阶段提示实时吐给外部 UI。
    ///
    /// 背景：这些提示只写控制台（<c>Main.statusText</c>），UTSL 的业务日志里没有，
    /// 生成器 UI 想显示就只能要么抓控制台、要么由引擎自己写出来 —— 这里选后者：
    /// 生成期间每 500ms 采样一次，写成原子 JSON（tmp + move），UI 轮询读取。
    ///
    /// 输出：<c>&lt;部署根&gt;\live\progress.json</c>
    ///   { "stage": "正在生成沙漠", "pct": 0.42, "ts": "2026-10-23T13:05:00.123+08:00", "done": false }
    ///
    /// 只在 GenerateWorld 期间运行，生成结束就停（done=true 一份），不影响正常开服。
    /// </summary>
    internal static class LiveGenerationProgress
    {
        private static readonly object Gate = new();
        private static Thread? _thread;
        private static volatile bool _running;
        private static UnifiedServerProcess.RootContext? _root;
        private static GenerationProgress? _progress;

        private static string? OutPath
        {
            get
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) { return null; }
                string dir = Path.Combine(Path.GetDirectoryName(exe)!, "live");
                return Path.Combine(dir, "progress.json");
            }
        }

        public static void Attach(Terraria.WorldGenSystemContext gen, GenerationProgress? progress)
        {
            lock (Gate)
            {
                _root = gen?.root;
                _progress = progress;
                if (_running) { return; }
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "UTSL-LiveProgress" };
                _thread.Start();
            }
        }

        public static void Detach()
        {
            lock (Gate)
            {
                _running = false;
                _root = null;
                _progress = null;
            }
            Write("", 0, done: true);
        }

        private static void Loop()
        {
            while (_running)
            {
                try
                {
                    var root = _root;
                    string stage = root?.Main.statusText ?? "";
                    double pct = 0;
                    try { pct = _progress?.TotalProgress ?? 0; } catch { }
                    Write(stage, pct, done: false);
                }
                catch { }
                Thread.Sleep(500);
            }
        }

        private static void Write(string stage, double pct, bool done)
        {
            string? path = OutPath;
            if (path is null) { return; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                string json = JsonSerializer.Serialize(new
                {
                    stage,
                    pct = Math.Round(Math.Clamp(pct, 0, 1), 4),
                    ts = DateTimeOffset.Now.ToString("O"),
                    done,
                });
                File.WriteAllText(tmp, json);
                for (int i = 0; i < 6; i++)
                {
                    try
                    {
                        if (File.Exists(path)) { File.Replace(tmp, path, null); }
                        else { File.Move(tmp, path); }
                        return;
                    }
                    catch (IOException) { Thread.Sleep(8 * (i + 1)); }
                    catch (UnauthorizedAccessException) { Thread.Sleep(8 * (i + 1)); }
                }
                try { File.Delete(tmp); } catch { }
            }
            catch { }
        }
    }
}
