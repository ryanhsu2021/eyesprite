using System;
using System.Collections.Generic;
using System.Linq;
using EyeGuardian.Models;

namespace EyeGuardian.Services
{
    /// <summary>
    /// 内置提示语目录（原创文案，覆盖眨眼、远眺、提肛、拉伸、喝水、呼吸等）。
    /// 文案本身放在 Lang.*.xaml 的 tip.* 里，切语言自动跟着变；
    /// 用户改写过的条目以 TipItem.Text 为准，不随语言变化。
    /// </summary>
    public static class TipCatalog
    {
        /// <summary>目录版本：新增内置条目时 +1，已装用户启动时会把新条目补进列表。</summary>
        public const int Version = 2;

        /// <summary>内置条目 id，顺序即默认展示顺序。</summary>
        public static readonly string[] Ids =
        {
            "blink",   // 眨眼
            "faraway", // 远眺
            "kegel",   // 提肛
            "breathe", // 呼吸
            "neck",    // 肩颈
            "stand",   // 站立伸展
            "squat",   // 深蹲
            "eight",   // 眼球运动
            "palms",   // 搓手敷眼
            "temple",  // 按揉太阳穴
            "waist",   // 腰背
            "wrist",   // 手腕
            "water",   // 喝水
            "walk",    // 走动
            "focus",   // 远近调焦
            "yawn"     // 哈欠放松
        };

        /// <summary>目录里的全部条目（默认全启用、未改写）。</summary>
        public static List<TipItem> All()
        {
            return Ids.Select(id => new TipItem { Id = id, Text = "", Enabled = true }).ToList();
        }

        /// <summary>内置条目在当前语言下的文案；找不到返回空串。</summary>
        public static string BuiltinText(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var s = Loc.T("tip." + id);
            return s != null && s.StartsWith("tip.", StringComparison.Ordinal) ? "" : s;
        }

        /// <summary>取某条目的实际文案：用户改写优先，否则用内置（随语言）。</summary>
        public static string TextOf(TipItem item)
        {
            if (item == null) return "";
            var t = (item.Text ?? "").Trim();
            if (t.Length > 0) return t;
            return BuiltinText(item.Id);
        }

        /// <summary>把某个条目恢复成内置文案（清空改写）。</summary>
        public static void Restore(TipItem item)
        {
            if (item != null) item.Text = "";
        }

        /// <summary>当前启用的提示语列表（去空、去重）。</summary>
        public static List<string> ActiveTexts(AppSettings s)
        {
            var list = new List<string>();
            if (s?.TipItems == null) return list;
            foreach (var it in s.TipItems)
            {
                if (it == null || !it.Enabled) continue;
                var t = TextOf(it);
                if (t.Length == 0) continue;
                if (!list.Contains(t)) list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// 首次运行填充目录；软件升级后把新增的内置条目补进用户列表（不覆盖已有的勾选与改写）。
        /// </summary>
        public static void Sync(AppSettings s)
        {
            if (s == null) return;

            if (s.TipItems == null)
            {
                s.TipItems = All();

                // 兼容 v1 的「每行一条」自定义列表：转成自定义条目，避免用户数据丢失
                if (s.Tips != null)
                {
                    int n = 0;
                    foreach (var raw in s.Tips)
                    {
                        var t = (raw ?? "").Trim();
                        if (t.Length == 0) continue;
                        s.TipItems.Add(new TipItem { Id = $"custom:legacy{n++}", Text = t, Enabled = true });
                    }
                }
                s.TipsVersion = Version;
                return;
            }

            if (s.TipsVersion < Version)
            {
                var have = new HashSet<string>(
                    s.TipItems.Where(x => x != null).Select(x => x.Id),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var id in Ids)
                {
                    if (!have.Contains(id))
                        s.TipItems.Add(new TipItem { Id = id, Text = "", Enabled = true });
                }
                s.TipsVersion = Version;
            }

            // 内置条目始终按目录顺序排列，自定义条目稳定地排在最后
            s.TipItems = s.TipItems.Where(x => x != null).OrderBy(OrderOf).ToList();
        }

        private static int OrderOf(TipItem item)
        {
            if (item == null) return int.MaxValue;
            if (item.IsCustom) return Ids.Length + 1;
            int i = Array.IndexOf(Ids, item.Id);
            return i >= 0 ? i : Ids.Length;
        }

        /// <summary>新建一条空白自定义条目。</summary>
        public static TipItem NewCustom()
        {
            return new TipItem
            {
                Id = "custom:" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Text = "",
                Enabled = true
            };
        }
    }
}
