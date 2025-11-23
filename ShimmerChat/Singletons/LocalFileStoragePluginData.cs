using ShimmerChatLib.Interface;
using System;
using System.IO;
using System.Text;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 本地文件存储插件数据服务实现
    /// </summary>
    public class LocalFileStoragePluginData : IPluginDataService
    {
        private readonly string root;

        /// <summary>
        /// 初始化 LocalFileStoragePluginData 实例
        /// </summary>
        public LocalFileStoragePluginData()
        {
            // 创建插件数据存储根目录
            root = Path.Combine(AppContext.BaseDirectory, "PluginData");
            InitializePluginDataFolder();
        }

        /// <summary>
        /// 初始化插件数据文件夹
        /// </summary>
        private void InitializePluginDataFolder()
        {
            // 确保根目录存在
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
        }

        /// <summary>
        /// 读取插件数据
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <param name="key">数据键名</param>
        /// <returns>存储的数据，如果不存在则返回null</returns>
        public string? Read(string pluginId, string key)
        {
            // 参数验证
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentNullException(nameof(pluginId), "Plugin ID cannot be null or whitespace");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key), "Key cannot be null or whitespace");

            try
            {
                // 为每个插件创建独立的文件夹
                string pluginFolder = GetPluginFolderPath(pluginId);
                if (!Directory.Exists(pluginFolder))
                    return null;

                // 获取数据文件路径
                string dataFilePath = GetDataFilePath(pluginFolder, key);
                if (!File.Exists(dataFilePath))
                    return null;

                // 读取数据
                return File.ReadAllText(dataFilePath, Encoding.UTF8);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied when reading plugin data: {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO error when reading plugin data: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error reading plugin data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入插件数据
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <param name="key">数据键名</param>
        /// <param name="value">要存储的数据值</param>
        public void Write(string pluginId, string key, string value)
        {
            // 参数验证
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentNullException(nameof(pluginId), "Plugin ID cannot be null or whitespace");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key), "Key cannot be null or whitespace");
            if (value == null)
                throw new ArgumentNullException(nameof(value), "Value cannot be null");

            try
            {
                // 为每个插件创建独立的文件夹
                string pluginFolder = GetPluginFolderPath(pluginId);
                if (!Directory.Exists(pluginFolder))
                {
                    Directory.CreateDirectory(pluginFolder);
                }

                // 获取数据文件路径
                string dataFilePath = GetDataFilePath(pluginFolder, key);

                // 写入数据
                File.WriteAllText(dataFilePath, value, Encoding.UTF8);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied when writing plugin data: {ex.Message}");
                throw new IOException("Access denied when writing plugin data", ex);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO error when writing plugin data: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error writing plugin data: {ex.Message}");
                throw new IOException("Failed to write plugin data", ex);
            }
        }

        /// <summary>
        /// 获取插件文件夹路径
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>插件文件夹的绝对路径</returns>
        private string GetPluginFolderPath(string pluginId)
        {
            // 对插件ID进行清理，确保它是一个有效的文件夹名称
            string safePluginId = SanitizeFileName(pluginId);
            return Path.Combine(root, safePluginId);
        }

        /// <summary>
        /// 获取数据文件路径
        /// </summary>
        /// <param name="pluginFolder">插件文件夹路径</param>
        /// <param name="key">数据键名</param>
        /// <returns>数据文件的绝对路径</returns>
        private string GetDataFilePath(string pluginFolder, string key)
        {
            // 对键名进行清理，确保它是一个有效的文件名称
            string safeKey = SanitizeFileName(key);
            return Path.Combine(pluginFolder, safeKey + ".json");
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