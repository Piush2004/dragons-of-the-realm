using System.Collections.Generic;

namespace BlockRealm.Sim
{
    /// <summary>Sliding-window event counter per source (usually a network address), with bounded memory.</summary>
    public sealed class SourceLimiter
    {
        private readonly Dictionary<string, Queue<int>> _events = new Dictionary<string, Queue<int>>();
        private const int MaxSources = 4096;

        /// <summary>
        /// How many events this source produced within the last <paramref name="windowTicks"/> ticks. When the table is
        /// full of other sources and this one cannot be tracked, the answer is "too many": the limiter fails closed.
        /// </summary>
        public int Count(string source, int nowTick, int windowTicks)
        {
            Queue<int> q;
            if (!_events.TryGetValue(source, out q))
            {
                if (_events.Count >= MaxSources)
                {
                    Prune(nowTick);
                    if (_events.Count >= MaxSources) return int.MaxValue;
                }
                return 0;
            }
            while (q.Count > 0 && nowTick - q.Peek() > windowTicks) q.Dequeue();
            if (q.Count == 0) _events.Remove(source);
            return q.Count;
        }

        public void Add(string source, int nowTick)
        {
            Queue<int> q;
            if (!_events.TryGetValue(source, out q))
            {
                if (_events.Count >= MaxSources) Prune(nowTick);
                if (_events.Count >= MaxSources) return;      // Count() already reports such a source as over the limit
                q = new Queue<int>();
                _events[source] = q;
            }
            q.Enqueue(nowTick);
            if (q.Count > 64) q.Dequeue();
        }

        private void Prune(int nowTick)
        {
            List<string> stale = null;
            foreach (KeyValuePair<string, Queue<int>> kv in _events)
            {
                if (kv.Value.Count == 0 || nowTick - kv.Value.Peek() > 20 * 120)
                {
                    if (stale == null) stale = new List<string>();
                    stale.Add(kv.Key);
                }
            }
            if (stale != null) foreach (string k in stale) _events.Remove(k);
        }
    }
}
