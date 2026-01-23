using Newtonsoft.Json;
using ShimmerChatLib.Interface;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShimmerChatBuiltin.RegexModifier
{
    public class RegexRule
    {
        public string Pattern { get; set; } = string.Empty;
        public string Replacement { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public System.Text.RegularExpressions.RegexOptions Options { get; set; } = System.Text.RegularExpressions.RegexOptions.None;
    }

    public class RegexSet
    {
        public string Name { get; set; } = string.Empty;
        public List<RegexRule> Rules { get; set; } = new();
    }

    public static class RegexManager
    {
        private const string SpaceId = "RegexModifier";
        private const string Key = "RegexSets";

        public static List<RegexSet> LoadRegexSets(IKVDataService kvData)
        {
            var json = kvData.Read(SpaceId, Key);
            if (string.IsNullOrEmpty(json))
            {
                return new List<RegexSet>();
            }

            try
            {
                return JsonConvert.DeserializeObject<List<RegexSet>>(json) ?? new List<RegexSet>();
            }
            catch
            {
                return new List<RegexSet>();
            }
        }

        public static void SaveRegexSets(IKVDataService kvData, List<RegexSet> sets)
        {
            var json = JsonConvert.SerializeObject(sets, Formatting.Indented);
            kvData.Write(SpaceId, Key, json);
        }

        public static RegexSet? GetRegexSet(IKVDataService kvData, string name)
        {
            var sets = LoadRegexSets(kvData);
            return sets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static void SaveRegexSet(IKVDataService kvData, RegexSet set)
        {
            var sets = LoadRegexSets(kvData);
            var existing = sets.FirstOrDefault(s => s.Name.Equals(set.Name, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                var index = sets.IndexOf(existing);
                sets[index] = set;
            }
            else
            {
                sets.Add(set);
            }
            
            SaveRegexSets(kvData, sets);
        }

        public static void DeleteRegexSet(IKVDataService kvData, string name)
        {
            var sets = LoadRegexSets(kvData);
            var existing = sets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                sets.Remove(existing);
                SaveRegexSets(kvData, sets);
            }
        }
    }
}
