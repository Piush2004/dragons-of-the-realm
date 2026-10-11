using System.Collections.Generic;

namespace BlockRealm.Sim
{
    /// <summary>Sliding-window event counter per source (usually a network address), with bounded memory.</summary>
    public sealed class SourceLimiter
    {
        private readonly Dictionary<string, Queue<int>> _events = new Dictionary<string, Queue<int>>();
        private readonly int _eventsPerSource;
        private readonly int _maxSources;

        /// <param name="eventsPerSource">
        /// How many recent events are remembered per source. It must exceed every threshold the caller compares
        /// against, or that threshold could never be reached.
        /// </param>
        public SourceLimiter(int eventsPerSource = 64, int maxSources = 16384)
        {
            _eventsPerSource = eventsPerSource;
            _maxSources = maxSources;
        }

        /// <summary>
        /// How many events this source produced within the last <paramref name="windowTicks"/> ticks. When the table is
        /// full of other active sources and this one cannot be tracked, the answer is "too many": the limiter fails
        /// closed (an attacker would otherwise escape limits by making the table overflow).
        /// </summary>
        public int Count(string source, int nowTick, int windowTicks)
        {
            Queue<int> q;
            if (!_events.TryGetValue(source, out q))
            {
                if (_events.Count >= _maxSources)
                {
                    Prune(nowTick, windowTicks);
                    if (_events.Count >= _maxSources) return int.MaxValue;
                }
                return 0;
            }
            while (q.Count > 0 && nowTick - q.Peek() > windowTicks) q.Dequeue();
            if (q.Count == 0) _events.Remove(source);
            return q.Count;
        }

        /// <summary>Like <see cref="Count"/>, but an untrackable source counts as clean (used where locking out is the greater risk).</summary>
        public int CountOpen(string source, int nowTick, int windowTicks)
        {
            int n = Count(source, nowTick, windowTicks);
            return n == int.MaxValue ? 0 : n;
        }

        public void Add(string source, int nowTick)
        {
            Queue<int> q;
            if (!_events.TryGetValue(source, out q))
            {
                if (_events.Count >= _maxSources) Prune(nowTick, 20 * 60);
                if (_events.Count >= _maxSources) return;      // Count() already reports such a source as over the limit
                q = new Queue<int>();
                _events[source] = q;
            }
            q.Enqueue(nowTick);
            while (q.Count > _eventsPerSource) q.Dequeue();
        }

        private void Prune(int nowTick, int windowTicks)
        {
            List<string> stale = null;
            foreach (KeyValuePair<string, Queue<int>> kv in _events)
            {
                if (kv.Value.Count == 0 || nowTick - kv.Value.Peek() > windowTicks)
                {
                    // Re-check with the newest event: a source is idle only if even its latest event is outside the window.
                    int newest = 0;
                    foreach (int t in kv.Value) newest = t;
                    if (kv.Value.Count == 0 || nowTick - newest > windowTicks)
                    {
                        if (stale == null) stale = new List<string>();
                        stale.Add(kv.Key);
                    }
                }
            }
            if (stale != null) foreach (string k in stale) _events.Remove(k);
        }
    }
}
