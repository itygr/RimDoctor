namespace RimDoctor
{
    public enum LogSeverity { Message, Warning, Error }

    /// <summary>A single captured log issue (deduped), with any matched advice + culprit.</summary>
    public class LogEntry
    {
        public LogSeverity severity;
        public string rawMessage;        // first line / summary used for display + dedup
        public string fullText;          // full text including stack trace, for the copy report
        public int occurrences = 1;      // how many times this signature was seen
        public int firstSeenFrame;       // ordering hint (monotonic counter)

        public LogAdviceRule advice;     // null if no rule matched
        public string culpritMod;        // best-effort attribution, may be null

        public string DedupKey;          // signature used to collapse repeats

        // UI: cached display height (computed once; -1 = not yet measured).
        public float cachedHeight = -1f;
        public float cachedForWidth = -1f;

        public bool HasAdvice => advice != null;
    }
}
