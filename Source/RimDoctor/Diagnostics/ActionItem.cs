namespace RimDoctor
{
    public enum ActionSeverity { Critical = 3, High = 2, Medium = 1, Info = 0 }

    /// <summary>One actionable finding the user can address, from any subsystem.</summary>
    public class ActionItem
    {
        public ActionSeverity severity;
        public string source;       // "Log Doctor" | "Health" | "Sorter" | "Harmony" | "Textures"
        public string title;
        public string detail;
        public string culpritMod;   // may be null
        public string suggestion;   // what to do about it

        public string SeverityLabel
        {
            get
            {
                switch (severity)
                {
                    case ActionSeverity.Critical: return "CRITICAL";
                    case ActionSeverity.High: return "HIGH";
                    case ActionSeverity.Medium: return "MEDIUM";
                    default: return "INFO";
                }
            }
        }
    }
}
