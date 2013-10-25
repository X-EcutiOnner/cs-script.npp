using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace CSScriptNpp
{
    /*todo:
     *  - create script deployment
     */

    public partial class Plugin
    {
        public const string PluginName = "CS-Script";
        public static int projectPanelId = -1;
        public static int outputPanelId = -1;
        public static int codeMapPanelId = -1;

        public static Dictionary<ShortcutKey, Tuple<string, Action>> internalShortcuts = new Dictionary<ShortcutKey, Tuple<string, Action>>();

        static internal void CommandMenuInit()
        {
            int index = 0;

            SetCommand(projectPanelId = index++, "Build (validate)", Build, new ShortcutKey(true, false, true, Keys.B));
            SetCommand(projectPanelId = index++, "Run", Run, new ShortcutKey(false, false, false, Keys.F5));
            SetCommand(index++, "---", null);
            SetCommand(projectPanelId = index++, "Project Panel", DoProjectPanel, Config.Instance.ShowProjectPanel);
            SetCommand(outputPanelId = index++, "Output Panel", DoOutputPanel, Config.Instance.ShowOutputPanel);
            SetCommand(codeMapPanelId = index++, "CodeMap Panel", DoCodeMapPanel, Config.Instance.ShowCodeMapPanel);
            SetCommand(index++, "---", null);
            LoadIntellisenseCommands(ref index);
            SetCommand(index++, "About", ShowAbout);

            BindInteranalShortcuts();

            KeyInterceptor.Instance.Install();
            KeyInterceptor.Instance.Add(Keys.F5);
            KeyInterceptor.Instance.Add(Keys.F4);
            KeyInterceptor.Instance.Add(Keys.F7);

            KeyInterceptor.Instance.KeyDown += Instance_KeyDown;

            //setup dependency injection, which may be overwritten by other plugins (e.g. NppScripts)
            Plugin.RunScript = () => Plugin.ProjectPanel.Run();
            Plugin.RunScriptAsExternal = () => Plugin.ProjectPanel.RunAsExternal();
            Plugin.DebugScript = () => Plugin.ProjectPanel.Debug();
        }

        static public Action RunScript;
        static public Action RunScriptAsExternal;
        static public Action DebugScript;

        //must be in a separate method to allow proper assembly probing
        static void LoadIntellisenseCommands(ref int cmdIndex)
        {
            CSScriptIntellisense.Plugin.CommandMenuInit(ref cmdIndex,
                 (index, name, handler, isCtrl, isAlt, isShift, key) =>
                 {
                     Plugin.SetCommand(index, name, handler, new ShortcutKey(isCtrl, isAlt, isShift, key));
                 });
        }

        static void BindInteranalShortcuts()
        {
            internalShortcuts.Add(new ShortcutKey(isCtrl: false, isAlt: false, isShift: false, key: Keys.F7), new Tuple<string, Action>(
                                  "Build", () =>
                                   {
                                       Build();
                                   }));
            internalShortcuts.Add(new ShortcutKey(isCtrl: true, isAlt: false, isShift: false, key: Keys.F7), new Tuple<string, Action>(
                                  "Load Current Document", () =>
                                  {
                                      DoProjectPanel();
                                      ShowProjectPanel();
                                      ProjectPanel.LoadCurrentDoc();
                                  }));
            internalShortcuts.Add(new ShortcutKey(isCtrl: false, isAlt: false, isShift: false, key: Keys.F5), new Tuple<string, Action>(
                                  "Run", () =>
                                  {
                                      if (Npp.IsCurrentFileHasExtension(".cs"))
                                          Run();
                                  }));
            internalShortcuts.Add(new ShortcutKey(isCtrl: true, isAlt: false, isShift: false, key: Keys.F5), new Tuple<string, Action>(
                                  "Run As External Process", () =>
                                  {
                                      if (Npp.IsCurrentFileHasExtension(".cs"))
                                          RunAsExternal();
                                  }));
            internalShortcuts.Add(new ShortcutKey(isCtrl: false, isAlt: false, isShift: false, key: Keys.F4), new Tuple<string, Action>(
                                  "Next File Location in Output", () =>
                                  {
                                      OutputPanel.TryNavigateToFileReference(toNext: true);
                                  }));
            internalShortcuts.Add(new ShortcutKey(isCtrl: true, isAlt: false, isShift: false, key: Keys.F4), new Tuple<string, Action>(
                                 "Previous File Location in Output", () =>
                                  {
                                      OutputPanel.TryNavigateToFileReference(toNext: false);
                                  }));
        }

        static void Instance_KeyDown(Keys key, int repeatCount, ref bool handled)
        {
            foreach (var shortcut in internalShortcuts.Keys)
                if ((byte)key == shortcut._key)
                {
                    Modifiers modifiers = KeyInterceptor.GetModifiers();

                    if (modifiers.IsCtrl == shortcut.IsCtrl && modifiers.IsShift == shortcut.IsShift && modifiers.IsAlt == shortcut.IsAlt)
                    {
                        handled = true;
                        var handler = internalShortcuts[shortcut];
                        handler.Item2();
                    }
                }
        }

        static public void ShowAbout()
        {
            using (var dialog = new AboutBox())
                dialog.ShowDialog();
        }

        static public OutputPanel OutputPanel;
        static public ProjectPanel ProjectPanel;
        static public CodeMapPanel CodeMapPanel;

        static public void DoProjectPanel()
        {
            ProjectPanel = ShowDockablePanel<ProjectPanel>("CS-Script", projectPanelId, NppTbMsg.DWS_DF_CONT_LEFT | NppTbMsg.DWS_ICONTAB | NppTbMsg.DWS_ICONBAR);
            ProjectPanel.Focus();
        }

        static public void ShowProjectPanel()
        {
            SetDockedPanelVisible(dockedManagedPanels[projectPanelId], projectPanelId, true);
        }

        static public void DoOutputPanel()
        {
            Plugin.OutputPanel = ShowDockablePanel<OutputPanel>("Output", outputPanelId, NppTbMsg.CONT_BOTTOM | NppTbMsg.DWS_ICONTAB | NppTbMsg.DWS_ICONBAR);
        }

        static public void DoCodeMapPanel()
        {
            Plugin.CodeMapPanel = ShowDockablePanel<CodeMapPanel>("C# Code Map", codeMapPanelId, NppTbMsg.CONT_RIGHT | NppTbMsg.DWS_ICONTAB | NppTbMsg.DWS_ICONBAR);
        }

        static public void Build()
        {
            if (runningScript == null)
            {
                if (Plugin.ProjectPanel == null)
                    DoProjectPanel();
                Plugin.ProjectPanel.Build();
            }
        }

        static public void Run()
        {
            if (runningScript == null)
            {
                if (Plugin.ProjectPanel == null)
                    DoProjectPanel();
                Plugin.RunScript();
            }
        }

        static public void RunAsExternal()
        {
            if (runningScript == null)
            {
                if (Plugin.ProjectPanel == null)
                    DoProjectPanel();
                Plugin.RunScriptAsExternal();
            }
        }

        static public OutputPanel ShowOutputPanel()
        {
            if (Plugin.OutputPanel == null)
                DoOutputPanel();
            else
                SetDockedPanelVisible(Plugin.OutputPanel, outputPanelId, true);

            UpdateLocalDebugInfo();
            return Plugin.OutputPanel;
        }

        static Process runningScript;

        public static Process RunningScript
        {
            get
            {
                return runningScript;
            }
            set
            {
                runningScript = value;
                UpdateLocalDebugInfo();
            }
        }

        static void UpdateLocalDebugInfo()
        {
            if (runningScript == null)
                Plugin.OutputPanel.localDebugPreffix = null;
            else
                Plugin.OutputPanel.localDebugPreffix = runningScript.Id.ToString() + ": ";
        }

        static internal void OnNppReady()
        {
            if (Config.Instance.ShowProjectPanel)
                DoProjectPanel();

            if (Config.Instance.ShowOutputPanel)
                DoOutputPanel();
            
            if (Config.Instance.ShowCodeMapPanel)
                DoCodeMapPanel();

            Intellisense.EnsureIntellisenseIntegration();
        }

        static internal void CleanUp()
        {
            Config.Instance.ShowProjectPanel = (dockedManagedPanels.ContainsKey(projectPanelId) && dockedManagedPanels[projectPanelId].Visible);
            Config.Instance.ShowOutputPanel = (dockedManagedPanels.ContainsKey(outputPanelId) && dockedManagedPanels[outputPanelId].Visible);
            Config.Instance.Save();
            OutputPanel.Clean();
        }

        public static void OnNotification(SCNotification data)
        {
        }

        static public void OnCurrentFileChanged()
        {
            if (CodeMapPanel != null)
                CodeMapPanel.RefreshContent();
        }

        public static void OnToolbarUpdate()
        {
            Plugin.FuncItems.RefreshItems();
            SetToolbarImage(Resources.Resources.css_logo_16x16_tb, projectPanelId);
        }
    }
}