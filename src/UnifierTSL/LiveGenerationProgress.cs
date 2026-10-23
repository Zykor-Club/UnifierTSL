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
                    WriteFrame(root);
                }
                catch { }
                Thread.Sleep(500);
            }
        }

        private static DateTime _lastFrame = DateTime.MinValue;

        /// <summary>
        /// 写一帧缩略图：每 2 s 一次，<c>live\frame.bin</c>。
        /// 格式（小端）：int32 magic('UTWF'=0x46575455) · int32 w · int32 h · ushort[w*h]
        /// 每个 ushort：bit15=1 表示是墙（值是墙 id），否则是图格 id；0 = 空（天空/无）
        /// 尺寸上限照社区那份协议的 200 万像素走，这里取 1200×240 = 28.8 万，够看形状又不拖慢生成。
        /// </summary>
        private static void WriteFrame(UnifiedServerProcess.RootContext? root)
        {
            if (root is null) { return; }
            var now = DateTime.UtcNow;
            if ((now - _lastFrame).TotalMilliseconds < 2000) { return; }
            _lastFrame = now;

            try
            {
                var tiles = root.Main.tile;
                if (tiles is null) { return; }
                int W = tiles.Width, H = tiles.Height;
                if (W <= 0 || H <= 0) { return; }

                const int OutW = 1200;                        // 输出宽（像素）
                int stepX = Math.Max(1, W / OutW);
                int outW = Math.Min(OutW, W / stepX);
                int stepY = stepX;                              // 等比，保证不变形
                int outH = Math.Max(1, H / stepY);

                var px = new ushort[outW * outH];
                for (int y = 0; y < outH; y++)
                {
                    int sy = Math.Min(H - 1, y * stepY);
                    int row = y * outW;
                    for (int x = 0; x < outW; x++)
                    {
                        int sx = Math.Min(W - 1, x * stepX);
                        ref var td = ref tiles[sx, sy];
                        if (td.type != 0 && td.type != ushort.MaxValue) { px[row + x] = td.type; }
                        else if (td.wall != 0) { px[row + x] = (ushort)(0x8000 | td.wall); }
                        else { px[row + x] = 0; }
                    }
                }

                string? prog = OutPath;
                if (prog is null) { return; }
                string path = Path.Combine(Path.GetDirectoryName(prog)!, "frame.bin");
                string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(0x46575455);   // 'UTWF'
                    bw.Write(outW);
                    bw.Write(outH);
                    foreach (var v in px) { bw.Write(v); }
                }
                for (int i = 0; i < 6; i++)
                {
                    try
                    {
                        if (File.Exists(path)) { File.Replace(tmp, path, null); } else { File.Move(tmp, path); }
                        return;
                    }
                    catch (IOException) { Thread.Sleep(8 * (i + 1)); }
                    catch (UnauthorizedAccessException) { Thread.Sleep(8 * (i + 1)); }
                }
                try { File.Delete(tmp); } catch { }
            }
            catch { }
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
