using System;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Standalone, resizable RimDoctor log window — the plain-language replacement
    /// for RimWorld's raw debug log. Hosts the same Log Doctor panel used in the
    /// RimDoctor tab (meaning / likely cause / fix / culprit, benign noise hidden).
    /// Opened by clicking the red error box (when enabled), by hotkey, or from the tab.
    /// </summary>
    public class Dialog_RimDoctorLog : Window
    {
        private readonly PanelLogDoctor panel = new PanelLogDoctor();

        public static Dialog_RimDoctorLog Instance { get; private set; }

        public Dialog_RimDoctorLog()
        {
            doCloseX = true;
            doCloseButton = false;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            onlyOneOfTypeAllowed = true;
            absorbInputAroundWindow = false;
            optionalTitle = "RimDoctor — Log";
        }

        public override Vector2 InitialSize => new Vector2(980f, 680f);

        /// <summary>
        /// Ensure the window is open. Idempotent — calling it while already open is a
        /// no-op (NOT a toggle). During worldgen the error path can fire the redirect
        /// many times a second; toggling here made the window strobe, so we just keep
        /// it open. Use the title-bar X to close.
        /// </summary>
        public static void OpenOrFocus()
        {
            try
            {
                if (Find.WindowStack != null && Find.WindowStack.IsOpen(typeof(Dialog_RimDoctorLog)))
                    return; // already open — do nothing (no strobe)
                Instance = new Dialog_RimDoctorLog();
                Find.WindowStack.Add(Instance);
            }
            catch (Exception e)
            {
                RDLog.Exception("Opening RimDoctor log window failed", e);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                panel.Draw(inRect);
            }
            catch (Exception e)
            {
                Widgets.Label(inRect, "The log window hit an error and was disabled for safety. See the log.");
                RDLog.Exception("RimDoctor log window render failed", e);
            }
        }

        public override void PreClose()
        {
            base.PreClose();
            Instance = null;
        }
    }
}
