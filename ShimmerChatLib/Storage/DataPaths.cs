namespace ShimmerChatLib.Storage
{
    /// <summary>
    /// 持久化数据目录布局的唯一事实来源。
    /// <para>
    /// 所有落盘位置都必须从这里取，禁止在别处再拼 "Data/..."，也禁止再引用旧目录名
    ///（LiteDBKVData / KVData / ChatMessages / DebugOutput / VectorStore / Memory / UserUploadImage）。
    /// </para>
    /// <para>
    /// 根目录默认是应用目录下的 <c>Data/</c>，可由 appsettings 的 <c>Data:RootPath</c> 覆盖；
    /// 相对路径按应用目录解析。
    /// </para>
    /// </summary>
    public sealed class DataPaths
    {
        /// <summary>默认数据根目录名。</summary>
        public const string DefaultRootDirectoryName = "Data";

        /// <summary>存储配置文件（KV / Debug / 消息 / 向量各自的后端选择都在这里）。</summary>
        public const string StorageSettingsFileName = "storage_settings.json";

        /// <summary>LiteDB 数据库目录名。</summary>
        public const string LiteDbDirectoryName = "LiteDB";

        /// <summary>文件存储目录名。</summary>
        public const string LocalFileStorageDirectoryName = "LocalFileStorage";

        /// <summary>用户上传图像目录名（Program 以 /userimages 暴露）。</summary>
        public const string UserUploadImageDirectoryName = "UserUploadImage";

        /// <summary>storage_settings.json 的配置节名。</summary>
        public const string RootConfigurationKey = "Data:RootPath";

        public const string KvDataDatabaseFileName = "kvdata.db";
        public const string ChatMessageDatabaseFileName = "chat_message.db";
        public const string DebugOutputDatabaseFileName = "debug_output.db";
        public const string VectorDatabaseFileName = "vector.db";
        public const string MemoryDatabaseFileName = "memory.db";

        public const string LocalKvDataDirectoryName = "kvdata";
        public const string LocalChatMessageDirectoryName = "chat_message";
        public const string LocalDebugOutputDirectoryName = "debug_output";
        public const string LocalVectorDirectoryName = "vector";

        /// <summary>存储后端迁移标记（LocalFileStorage ↔ LiteDB）。</summary>
        public const string MigrationMarkerFileName = ".migration_marker";

        /// <summary>旧目录结构 → Data/ 结构的布局迁移标记。</summary>
        public const string LayoutMigrationMarkerFileName = ".layout_migration";

        public DataPaths(string rootDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
            Root = Path.GetFullPath(rootDirectory);
        }

        /// <summary>数据根目录的绝对路径。</summary>
        public string Root { get; }

        public string StorageSettingsFile => Path.Combine(Root, StorageSettingsFileName);

        public string LiteDbDirectory => Path.Combine(Root, LiteDbDirectoryName);

        public string KvDataDatabase => Path.Combine(LiteDbDirectory, KvDataDatabaseFileName);

        public string ChatMessageDatabase => Path.Combine(LiteDbDirectory, ChatMessageDatabaseFileName);

        public string DebugOutputDatabase => Path.Combine(LiteDbDirectory, DebugOutputDatabaseFileName);

        public string VectorDatabase => Path.Combine(LiteDbDirectory, VectorDatabaseFileName);

        public string MemoryDatabase => Path.Combine(LiteDbDirectory, MemoryDatabaseFileName);

        public string LocalFileStorageDirectory => Path.Combine(Root, LocalFileStorageDirectoryName);

        public string LocalKvDataDirectory => Path.Combine(LocalFileStorageDirectory, LocalKvDataDirectoryName);

        public string LocalChatMessageDirectory => Path.Combine(LocalFileStorageDirectory, LocalChatMessageDirectoryName);

        public string LocalDebugOutputDirectory => Path.Combine(LocalFileStorageDirectory, LocalDebugOutputDirectoryName);

        public string LocalVectorDirectory => Path.Combine(LocalFileStorageDirectory, LocalVectorDirectoryName);

        public string UserUploadImageDirectory => Path.Combine(Root, UserUploadImageDirectoryName);

        public string MigrationMarkerFile => Path.Combine(Root, MigrationMarkerFileName);

        public string LayoutMigrationMarkerFile => Path.Combine(Root, LayoutMigrationMarkerFileName);

        /// <summary>
        /// 按「应用目录 + 可选的 Data:RootPath」解析布局。配置为空时使用 <c>Data</c>；
        /// 绝对路径原样使用，相对路径按应用目录解析。
        /// </summary>
        public static DataPaths Create(string baseDirectory, string? configuredRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

            var relative = string.IsNullOrWhiteSpace(configuredRoot)
                ? DefaultRootDirectoryName
                : configuredRoot.Trim();

            var root = Path.IsPathRooted(relative)
                ? relative
                : Path.Combine(
                    baseDirectory,
                    relative.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return new DataPaths(root);
        }

        /// <summary>
        /// 进程级当前布局。静态消费者（Agent 导入导出、记忆真值库等）从这里取路径；
        /// 启动时由 Program 用实际配置覆盖，测试可自行构造 <see cref="DataPaths"/> 实例。
        /// </summary>
        public static DataPaths Current { get; private set; } =
            new(Path.Combine(AppContext.BaseDirectory, DefaultRootDirectoryName));

        /// <summary>覆盖进程级布局。只允许在启动装配阶段调用一次。</summary>
        public static void Configure(DataPaths paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            Current = paths;
        }

        /// <summary>创建根目录与固定子目录；按 store 的子目录由各自服务在首次写入时创建。</summary>
        public void EnsureDirectories()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LiteDbDirectory);
            Directory.CreateDirectory(LocalFileStorageDirectory);
            Directory.CreateDirectory(UserUploadImageDirectory);
        }
    }
}
