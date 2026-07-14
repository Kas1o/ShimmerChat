using ShimmerChatLib.Models;
using System;
using System.Collections.Generic;

namespace ShimmerChatLib.Interface
{
    public interface IDebugOutputService
    {
        /// <summary>
        /// 写入一条调试输出，同时输出到控制台并持久化存储。
        /// </summary>
        void Write(string source, string category, string content);

        /// <summary>
        /// 分页查询调试输出条目，支持按来源和类别筛选。
        /// </summary>
        /// <param name="skip">跳过的条目数</param>
        /// <param name="take">获取的条目数</param>
        /// <param name="sourceFilter">按来源筛选（可选）</param>
        /// <param name="categoryFilter">按类别筛选（可选）</param>
        /// <returns>调试输出条目列表</returns>
        List<DebugOutputEntry> GetEntries(int skip, int take, string? sourceFilter = null, string? categoryFilter = null);

        /// <summary>
        /// 获取调试输出总条目数，可选按来源/类别筛选。
        /// </summary>
        int GetCount(string? sourceFilter = null, string? categoryFilter = null);

        /// <summary>
        /// 获取所有不重复的来源名称。
        /// </summary>
        List<string> GetSources();

        /// <summary>
        /// 获取所有不重复的类别名称。
        /// </summary>
        List<string> GetCategories();

        /// <summary>
        /// 删除指定条目。
        /// </summary>
        bool DeleteEntry(Guid id);

        /// <summary>
        /// 清空全部调试输出。
        /// </summary>
        void ClearAll();
    }
}
