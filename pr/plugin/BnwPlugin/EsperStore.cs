using System.Collections.Generic;
using BraveNewWorld.Rules;
using Last.Management;

namespace BraveNewWorld.PR
{
    // Keeps each character's EP / EL / banks in the save file. The PR games share one
    // save format; FF6 never uses FF2's keyword list (UserDataManager.OwnedKeyWaordList,
    // a List<int> saved as "ownedKeyWaordList"), so our block lives there:
    //
    //   marker "BNW1", then three packed words per character_status id 1..14
    //
    // Anything else in the list is left untouched. (The scenario-variable array would
    // have been the obvious place, but it only has 16 slots.)
    static class EsperStore
    {
        const int Marker = 0x424E5731;
        const int Characters = 14;
        const int Words = 3;
        const int BlockSize = 1 + Words * Characters;

        // The game computes stats on more than one thread (seen on the Steam Deck: a plain
        // Dictionary got corrupted), so everything here goes through one lock.
        static readonly object Sync = new object();
        static readonly Dictionary<int, EsperProgress> cache = new Dictionary<int, EsperProgress>();
        static string lastState = "";
        public static string State { get; private set; } = "not checked";

        public static void Invalidate()
        {
            lock (Sync) cache.Clear();
        }

        static Il2CppSystem.Collections.Generic.List<int> List()
        {
            var list = UserDataManager.Instance()?.OwnedKeyWaordList;
            SetState(list == null ? "no save data" : "ok");
            return list;
        }

        // Index of our marker, or -1.
        static int Find(Il2CppSystem.Collections.Generic.List<int> list)
        {
            int n = list.Count;
            for (int i = 0; i + BlockSize <= n; i++)
                if (list[i] == Marker) return i;
            return -1;
        }

        static void SetState(string s)
        {
            State = s;
            if (s != lastState) { lastState = s; Plugin.L.LogInfo($"[esper store] {s}"); }
        }

        public static bool Available
        {
            get { lock (Sync) return List() != null; }
        }

        public static EsperProgress Get(int statusId)
        {
            if (statusId < 1 || statusId > Characters) return EsperProgress.Empty;
            lock (Sync) return GetLocked(statusId);
        }

        static EsperProgress GetLocked(int statusId)
        {
            if (cache.TryGetValue(statusId, out var p)) return p.Clone();
            p = EsperProgress.Empty;
            var list = List();
            if (list != null)
            {
                int at = Find(list);
                if (at >= 0)
                {
                    int o = at + 1 + Words * (statusId - 1);
                    p = EsperProgress.Unpack(list[o], list[o + 1], list[o + 2]);
                }
            }
            cache[statusId] = p;
            return p.Clone();
        }

        // BNW's own flags (bit 0: Julian's Schematics, Schematics.cs): marker "BNWF" + one word, after the
        // character block (never inside it)
        const int FlagsMarker = 0x424E5746;

        static int FindFlags(Il2CppSystem.Collections.Generic.List<int> list)
        {
            int block = Find(list), n = list.Count;
            for (int i = 0; i + 1 < n; i++)
            {
                if (block >= 0 && i >= block && i < block + BlockSize) continue;
                if (list[i] == FlagsMarker) return i;
            }
            return -1;
        }

        public static int Flags
        {
            get
            {
                lock (Sync)
                {
                    var list = List();
                    if (list == null) return 0;
                    int at = FindFlags(list);
                    return at < 0 ? 0 : list[at + 1];
                }
            }
            set
            {
                lock (Sync)
                {
                    var list = List();
                    if (list == null) return;
                    int at = FindFlags(list);
                    if (at < 0)
                    {
                        list.Add(FlagsMarker);
                        list.Add(value);
                        Plugin.L.LogInfo("[esper store] started BNW's flags in this save");
                    }
                    else list[at + 1] = value;
                }
            }
        }

        public static bool Put(int statusId, EsperProgress p)
        {
            if (statusId < 1 || statusId > Characters) return false;
            lock (Sync) return PutLocked(statusId, p);
        }

        static bool PutLocked(int statusId, EsperProgress p)
        {
            var list = List();
            if (list == null) return false;
            int at = Find(list);
            if (at < 0)
            {
                at = list.Count;
                list.Add(Marker);
                for (int i = 1; i < BlockSize; i++) list.Add(0);
                Plugin.L.LogInfo("[esper store] started BNW esper data in this save");
            }
            var w = p.Pack();
            int o = at + 1 + Words * (statusId - 1);
            for (int i = 0; i < Words; i++) list[o + i] = w[i];
            cache[statusId] = p.Clone();
            return true;
        }
    }
}
