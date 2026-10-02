using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 极简 ini 解析器。只支持 [节名] 和 键=值 两种语法，# 与 ; 开头为注释。
    /// 游戏自身也用 ini，保持一致便于用户直接编辑。
    /// </summary>
    public class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>列出全部节名。</summary>
        public IEnumerable<string> SectionNames
        {
            get { return _sections.Keys; }
        }

        public bool HasSection(string section)
        {
            return _sections.ContainsKey(section);
        }

        /// <summary>节名是否存在，且至少有一个键。</summary>
        public bool HasKeys(string section)
        {
            Dictionary<string, string> s;
            return _sections.TryGetValue(section, out s) && s.Count > 0;
        }

        /// <summary>确保节存在，返回该节。</summary>
        public Dictionary<string, string> EnsureSection(string section)
        {
            Dictionary<string, string> s;
            if (!_sections.TryGetValue(section, out s))
            {
                s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections[section] = s;
            }
            return s;
        }

        /// <summary>写一个键。节不存在会自动创建。</summary>
        public void Set(string section, string key, string value)
        {
            EnsureSection(section)[key] = value ?? "";
        }

        /// <summary>移除某个键，节随之变空时保留空节。</summary>
        public bool Remove(string section, string key)
        {
            Dictionary<string, string> s;
            if (!_sections.TryGetValue(section, out s)) return false;
            return s.Remove(key);
        }

        /// <summary>把某个节的全部键值替换为给定内容。</summary>
        public void ReplaceSection(string section, IEnumerable<string> values)
        {
            var s = EnsureSection(section);
            s.Clear();
            int i = 0;
            foreach (string v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                s[KeyForIndex(i)] = v.Trim();
                i++;
            }
        }

        /// <summary>配置节里曲目条目的键名。带序号是为了让 IniHandler 能原样保留。</summary>
        public static string KeyForIndex(int index)
        {
            return "Track" + (index + 1).ToString("00");
        }

        /// <summary>
        /// 判断某个键是否是我们自己的曲目条目。
        /// 曲目必须写成 键=值 的形式，不能用裸行，
        /// 否则 Anchor Chain 用 IniHandler 回写配置时会丢掉没有等号的内容。
        /// </summary>
        public static bool IsTrackKey(string key)
        {
            return key.StartsWith("Track", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 读出某个场景节里的曲目路径列表，按键名排序。
        /// 同时兼容 Track01=path 与裸行两种写法。
        /// </summary>
        public List<string> GetTracks(string section)
        {
            var result = new List<string>();
            var s = EnsureSection(section);

            var bare = new List<string>();
            foreach (var kv in s)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                if (IsTrackKey(kv.Key)) result.Add(kv.Value);
                else bare.Add(kv.Value);
            }

            if (result.Count == 0) return bare;

            // 键=值 形式按 Track 编号排序，裸行保留原顺序
            bare.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>写回磁盘。保留 UTF-8 无 BOM，避免游戏读 ini 时出现乱码。</summary>
        public void Save(string path)
        {
            var sb = new StringBuilder();
            foreach (var sec in _sections)
            {
                sb.Append('[').Append(sec.Key).Append(']').AppendLine();
                foreach (var kv in sec.Value)
                {
                    sb.Append(kv.Key).Append('=').Append(kv.Value).AppendLine();
                }
                sb.AppendLine();
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// 从默认配置补齐缺失的节与键，不覆盖用户已有取值。
        /// 模拟 Anchor Chain 的 ACConfig 行为。返回 true 表示有新增。
        /// </summary>
        public bool BackfillFrom(IniFile defaults)
        {
            if (defaults == null) return false;

            bool changed = false;
            foreach (var sec in defaults._sections)
            {
                bool isNew = !_sections.ContainsKey(sec.Key);
                var target = EnsureSection(sec.Key);
                if (isNew) changed = true;

                foreach (var kv in sec.Value)
                {
                    if (!target.ContainsKey(kv.Key))
                    {
                        target[kv.Key] = kv.Value;
                        changed = true;
                    }
                }
            }
            return changed;
        }

        /// <summary>取某个节下的全部键值对，节不存在时返回空字典。</summary>
        public Dictionary<string, string> GetSection(string section)
        {
            Dictionary<string, string> result;
            if (_sections.TryGetValue(section, out result))
            {
                return result;
            }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Get(string section, string key, string fallback)
        {
            Dictionary<string, string> sec;
            if (_sections.TryGetValue(section, out sec))
            {
                string v;
                if (sec.TryGetValue(key, out v))
                {
                    return v;
                }
            }
            return fallback;
        }

        public float GetFloat(string section, string key, float fallback)
        {
            string raw = Get(section, key, null);
            float v;
            if (!string.IsNullOrEmpty(raw)
                && float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
            {
                return v;
            }
            return fallback;
        }

        public bool GetBool(string section, string key, bool fallback)
        {
            string raw = Get(section, key, null);
            if (string.IsNullOrEmpty(raw)) return fallback;
            raw = raw.Trim().ToLowerInvariant();
            if (raw == "true" || raw == "1" || raw == "yes" || raw == "on") return true;
            if (raw == "false" || raw == "0" || raw == "no" || raw == "off") return false;
            return fallback;
        }

        /// <summary>从文件读取。文件不存在时返回一个空实例。</summary>
        public static IniFile Load(string path)
        {
            var ini = new IniFile();
            if (!File.Exists(path)) return ini;

            string currentSection = "";
            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#' || line[0] == ';') continue;

                // 去掉行尾注释（仅在分隔符前有空格时视为注释，避免误伤路径中的 # ）
                int commentIdx = line.IndexOf(" #", StringComparison.Ordinal);
                if (commentIdx < 0) commentIdx = line.IndexOf(" ;", StringComparison.Ordinal);
                if (commentIdx > 0) line = line.Substring(0, commentIdx).Trim();

                if (line.Length == 0) continue;

                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close > 1)
                    {
                        currentSection = line.Substring(1, close - 1).Trim();
                        if (!ini._sections.ContainsKey(currentSection))
                        {
                            ini._sections[currentSection] =
                                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        }
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0)
                {
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (key.Length == 0) continue;

                    Dictionary<string, string> sec;
                    if (!ini._sections.TryGetValue(currentSection, out sec))
                    {
                        sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        ini._sections[currentSection] = sec;
                    }
                    sec[key] = val;
                }
                else
                {
                    // 裸行：在分类节下视为一个音频文件名，用自增键存放
                    Dictionary<string, string> sec;
                    if (!ini._sections.TryGetValue(currentSection, out sec))
                    {
                        sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        ini._sections[currentSection] = sec;
                    }
                    int n = 0;
                    while (sec.ContainsKey("file" + n)) n++;
                    sec["file" + n] = line;
                }
            }
            return ini;
        }
    }
}
