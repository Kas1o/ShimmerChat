namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 消息存储双向迁移服务接口。
    /// </summary>
    public interface IMessageStoreMigrationService
    {
        int MigrateFileToLite(bool clearSource = false);
        int MigrateLiteToFile(bool clearSource = false);
        int GetFileStoreCount();
        int GetLiteStoreCount();
    }
}
