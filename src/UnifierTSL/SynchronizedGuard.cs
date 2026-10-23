using Terraria;
using Terraria.Utilities;
using UnifierTSL.FileSystem;

namespace UnifierTSL
{
    public static class SynchronizedGuard
    {

        // Just trigger the static constructor

        public static readonly Lock ConsoleLock = new();
        public static void Load() { }

        private static readonly Lock cultureFileLock = new();
        private static readonly Lock creativeSacrificesLock = new();
        private static readonly Lock favoritesFileLock = new();

        static SynchronizedGuard() {
            On.Terraria.GameContent.Creative.CreativeItemSacrificesCatalog.Initialize
                += (orig, self)
                => { lock (creativeSacrificesLock) { orig(self); } };
            On.Terraria.IO.FavoritesFile.Save
                += (orig, file, root)
                => {
                    lock (favoritesFileLock) {
                        // ★ 本地 fork 补丁（2026-10-23）：新建世界时 Terraria 会顺手往
                        //   %USERPROFILE%\Documents\My Games\Terraria\favorites.json 写收藏标记
                        //   （CreateMetadata → FileData.SetFavorite → FavoritesFile.Save）。
                        //   实测某些机器上这一步会被系统拒绝（UnauthorizedAccessException，
                        //   而同一目录下 cmd 建文件是成功的），结果【新建世界直接崩】，
                        //   连带"整链重置"也跑不了 —— favorites 只是收藏列表这类装饰数据，
                        //   不值得让生成流程陪葬，所以这里兜底：失败只记一条 warning。
                        //   （UTSL 的 ApplySavePathOverride 只改了 Program.SavePath，
                        //     FavoritesFile 用的仍是覆盖前的旧路径，所以覆盖救不了它。）
                        try { orig(file, root); }
                        catch (Exception ex) {
                            UnifierApi.Logger.Warning(
                                $"写 favorites.json 失败（已忽略，不影响世界生成）: {ex.GetType().Name} {ex.Message}",
                                category: "Favorites");
                        }
                    }
                };
            On.Terraria.IO.FavoritesFile.Load
                += (orig, file, root)
                => { lock (favoritesFileLock) { orig(file, root); } };
            // ★ 本地 fork 补丁（2026-10-23）：生成期间把"正在生成沙漠/清理树/放置陷阱"这类
            //   阶段提示实时写出来（这些只写控制台 Main.statusText，日志里没有），
            //   给 UTSL 世界生成器 UI 轮询用。只读，不改生成行为；结束自动停。
            On.Terraria.WorldGenSystemContext.GenerateWorld
                += (orig, self, customProgressObject, customController) => {
                    LiveGenerationProgress.Attach(self, customProgressObject);
                    try { return orig(self, customProgressObject, customController); }
                    finally { LiveGenerationProgress.Detach(); }
                };
            On.Terraria.IO.WorldFileSystemContext._SaveWorld
                += (orig, self, _, resetTime, useTemps, canBeSkipped)
                => {
                    UnifiedServerProcess.RootContext s = self.root;
                    if (s.Main.worldName == "") {
                        s.Main.worldName = "World";
                    }
                    while (s.WorldGen.TransformingWorld) {
                        s.Main.statusText = Lang.gen[48].Value;
                    }
                    using (FileLockManager.Enter(self.root.Main.worldPathName)) {
                        FileUtilities.ProtectedInvoke(delegate {
                            self.InternalSaveWorld(false, resetTime, useTemps);
                        });
                    }
                };
        }
    }
}
