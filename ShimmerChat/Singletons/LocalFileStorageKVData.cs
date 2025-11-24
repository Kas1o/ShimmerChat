using ShimmerChatLib.Interface;
using System;
using System.IO;
using System.Text;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 本地文件存储KV数据服务实现
    /// </summary>
    public class LocalFileStorageKVData : IKVDataService
    {
        private readonly string root;

		/// <summary>
		/// 初始化 LocalFileStorageKVData 实例
		/// </summary>
		public LocalFileStorageKVData()
        {
            // 创建KV数据存储根目录
            root = Path.Combine(AppContext.BaseDirectory, "KVData");
            InitializeKVDataFolder();
        }

        /// <summary>
        /// 初始化KV数据文件夹
        /// </summary>
        private void InitializeKVDataFolder()
        {
            // 确保根目录存在
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
        }

        /// <summary>
        /// 读取KV数据
        /// </summary>
        /// <param name="spaceId">空间Id</param>
        /// <param name="key">数据键名</param>
        /// <returns>存储的数据，如果不存在则返回null</returns>
        public string? Read(string spaceId, string key)
        {
            // 参数验证
            if (string.IsNullOrWhiteSpace(spaceId))
                throw new ArgumentNullException(nameof(spaceId), "Space ID cannot be null or whitespace");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key), "Key cannot be null or whitespace");

            try
            {
                // 为每个空间创建独立的文件夹
                string spaceFolder = GetSpaceFolderPath(spaceId);
                if (!Directory.Exists(spaceFolder))
                    return null;

                // 获取数据文件路径
                string dataFilePath = GetDataFilePath(spaceFolder, key);
                if (!File.Exists(dataFilePath))
                    return null;

                // 读取数据
                return File.ReadAllText(dataFilePath, Encoding.UTF8);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied when reading space data: {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO error when reading space data: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error reading space data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入数据到空间
        /// </summary>
        /// <param name="spaceId">空间ID</param>
        /// <param name="key">数据键名</param>
        /// <param name="value">要存储的数据值</param>
        public void Write(string spaceId, string key, string value)
        {
            // 参数验证
            if (string.IsNullOrWhiteSpace(spaceId))
                throw new ArgumentNullException(nameof(spaceId), "Space ID cannot be null or whitespace");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key), "Key cannot be null or whitespace");
            if (value == null)
                throw new ArgumentNullException(nameof(value), "Value cannot be null");

            try
            {
                // 为每个空间创建独立的文件夹
                string spaceFolder = GetSpaceFolderPath(spaceId);
                if (!Directory.Exists(spaceFolder))
                {
                    Directory.CreateDirectory(spaceFolder);
                }

                // 获取数据文件路径
                string dataFilePath = GetDataFilePath(spaceFolder, key);

                // 写入数据
                File.WriteAllText(dataFilePath, value, Encoding.UTF8);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied when writing space data: {ex.Message}");
                throw new IOException("Access denied when writing space data", ex);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO error when writing space data: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error writing space data: {ex.Message}");
                throw new IOException("Failed to write space data", ex);
            }
        }

        /// <summary>
        /// 获取空间文件夹路径
        /// </summary>
        /// <param name="spaceId">空间ID</param>
        /// <returns>空间文件夹的绝对路径</returns>
        private string GetSpaceFolderPath(string spaceId)
        {
            // 对空间ID进行清理，确保它是一个有效的文件夹名称
            string safeSpaceId = SanitizeFileName(spaceId);
            return Path.Combine(root, safeSpaceId);
        }

        /// <summary>
        /// 获取数据文件路径
        /// </summary>
        /// <param name="spaceFolder">空间文件夹路径</param>
        /// <param name="key">数据键名</param>
        /// <returns>数据文件的绝对路径</returns>
        private string GetDataFilePath(string spaceFolder, string key)
        {
            // 对键名进行清理，确保它是一个有效的文件名称
            string safeKey = SanitizeFileName(key);
            return Path.Combine(spaceFolder, safeKey + ".json");
        }

        /// <summary>
        /// 清理文件名，移除或替换不允许的字符
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>清理后的安全文件名</returns>
        private string SanitizeFileName(string input)
        {
            // 移除或替换文件系统中不允许的字符
            string invalidChars = new string(Path.GetInvalidFileNameChars());
            string sanitized = input;
            foreach (char c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }
            return sanitized;
        }
    }
}