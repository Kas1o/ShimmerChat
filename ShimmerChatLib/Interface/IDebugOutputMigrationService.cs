namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 调试输出仓库的双向迁移服务。
    /// </summary>
    public interface IDebugOutputMigrationService
    {
        int MigrateFileToLite(bool clearSource = false);
        int MigrateLiteToFile(bool clearSource = false);
        int GetFileStoreCount();
        int GetLiteStoreCount();
    }
}
