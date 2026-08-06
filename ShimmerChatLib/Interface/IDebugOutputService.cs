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
        /// 分页查询调试输出条目，支持按来源、类别、关键词和时间范围筛选。
        /// </summary>
        /// <param name="skip">跳过的条目数</param>
        /// <param name="take">获取的条目数</param>
        /// <param name="sourceFilter">按来源筛选（可选）</param>
        /// <param name="categoryFilter">按类别筛选（可选）</param>
        /// <param name="keyword">关键词，对来源/类别/内容做不区分大小写的包含匹配（可选）</param>
        /// <param name="from">起始时间（UTC，含），可选</param>
        /// <param name="to">结束时间（UTC，含），可选</param>
        /// <returns>调试输出条目列表</returns>
        List<DebugOutputEntry> GetEntries(int skip, int take, string? sourceFilter = null, string? categoryFilter = null, string? keyword = null, DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// 获取调试输出总条目数，可选按来源/类别/关键词/时间范围筛选。
        /// </summary>
        int GetCount(string? sourceFilter = null, string? categoryFilter = null, string? keyword = null, DateTime? from = null, DateTime? to = null);

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
        /// 仅保留最近的 N 条调试输出（按时间倒序），删除其余更旧的条目。
        /// </summary>
        /// <param name="keep">保留条数；小于等于 0 时清空全部</param>
        /// <returns>删除的条目数</returns>
        int TrimToRecent(int keep);

        /// <summary>
        /// 删除所有严格早于指定时间（UTC）的调试输出。
        /// </summary>
        /// <param name="cutoffUtc">时间边界（UTC），早于该时刻的条目被删除</param>
        /// <returns>删除的条目数</returns>
        int DeleteOlderThan(DateTime cutoffUtc);

        /// <summary>
        /// 清空全部调试输出。
        /// </summary>
        void ClearAll();
    }
}
