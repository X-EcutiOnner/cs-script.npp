﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using CSScriptIntellisense;
using CSScriptIntellisense.Interop;
using CSScriptNpp.Dialogs;
using Kbg.NppPluginNET.PluginInfrastructure;

namespace CSScriptNpp
{
    public partial class ProjectPanel : Form
    {
        static internal string currentScript;
        internal CodeMapPanel mapPanel;
        FavoritesPanel favPanel;

        public ProjectPanel()
        {
            InitializeComponent();

            this.VisibleChanged += ProjectPanel_VisibleChanged;

            UpdateButtonsTooltips();

            //tabControl1.Bac
            tabControl1.AddTab("Code Map", mapPanel = new CodeMapPanel());
            tabControl1.AddTab("Favorites", favPanel = new FavoritesPanel());

            favPanel.OnOpenScriptRequest = file =>
                {
                    if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        LoadScript(file);
                    else
                        Npp.Editor.OpenFile(file, true);
                };

            RefreshControls();
            ReloadScriptHistory();
            LoadReleaseNotes();

            treeView1.AttachMouseControlledZooming();

            toolStripPersistance = new ToolStripPersistance(toolStrip1, settingsFile);
            toolStripPersistance.Load();
        }

        protected override void WndProc(ref Message m)
        {
            //Listen for the closing of the dockable panel as the result of Npp native close ("cross") button on the window
            switch (m.Msg)
            {
                case win32.WM_NOTIFY:
                    var notify = (ScNotificationHeader)Marshal.PtrToStructure(m.LParam, typeof(ScNotificationHeader));
                    if (notify.Code == (int)DockMgrMsg.DMN_CLOSE)
                        Plugin.SetDockedPanelVisible(this, Plugin.projectPanelId, false);
                    break;
            }
            base.WndProc(ref m);
        }

        bool hideSecondaryPanelsScheduled = false;

        private void ProjectPanel_VisibleChanged(object sender, EventArgs e)
        {
            if (Config.Instance.SyncSecondaryPanelsWithProjectPanel)
            {
                if (!hideSecondaryPanelsScheduled)
                {
                    hideSecondaryPanelsScheduled = true;
                    Dispatcher.Schedule(300, () =>
                    {
                        if (!this.Visible)
                            Plugin.HideSecondaryPanels();
                        hideSecondaryPanelsScheduled = false;
                    });
                };
            }
        }

        ToolStripPersistance toolStripPersistance;

        string settingsFile = Path.Combine(PluginEnv.ConfigDir, "toolbar_buttons.txt");

        void UpdateButtonsTooltips()
        {
            validateBtn.EmbeddShortcutIntoTooltip(Config.Shortcuts.GetValue("_BuildFromMenu", "Ctrl+Shift+B") + " or " + Config.Shortcuts.GetValue("Build", "F7"));
            runBtn.EmbeddShortcutIntoTooltip(Config.Shortcuts.GetValue("_Run", "F5"));
            loadBtn.EmbeddShortcutIntoTooltip(Config.Shortcuts.GetValue("LoadCurrentDocument", "Ctrl+F7"));
        }

        void LoadReleaseNotes()
        {
            //System.Diagnostics.Debug.Assert(false);
            whatsNewPanel.Visible = false;
            string pluginVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            if (Config.Instance.ReleaseNotesViewedFor != pluginVersion)
            {
                whatsNewTxt.Text = CSScriptNpp.Resources.Resources.WhatsNew;
                whatsNewPanel.Visible = true;

                if (!CSScriptHelper.Integration.IsCssIntegrated())
                {
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            CSScriptHelper.Integration.IntegrateCSScript();
                            if (CSScriptHelper.Integration.IsCssIntegrated())
                                CSScriptHelper.Integration.ShowIntegrationInfo();
                            else
                                CSScriptHelper.Integration.ShowIntegrationWarning();
                        }
                        catch { }
                    });
                }

                Config.Instance.ReleaseNotesViewedFor = pluginVersion;
                Config.Instance.Save();
            }
        }

        void ReloadScriptHistory()
        {
            this.historyBtn.DropDownItems.Clear();
            string[] files = Config.Instance.ScriptHistory.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
            if (files.Count() == 0)
            {
                this.historyBtn.DropDownItems.Add(new ToolStripMenuItem("empty") { Enabled = false });
            }
            else
            {
                foreach (string file in files)
                {
                    var item = new ToolStripMenuItem(file);
                    item.Click += (s, e) =>
                        {
                            string script = file;
                            if (File.Exists(script))
                            {
                                LoadScript(script);
                            }
                            else if (DialogResult.Yes == MessageBox.Show("File '" + script + "' cannot be found.\nDo you want to remove it from the Recent Scripts List?", "CS-Script", MessageBoxButtons.YesNo))
                            {
                                this.historyBtn.DropDownItems.Remove(item);

                                var scripts = Config.Instance.ScriptHistory.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                                                                           .Distinct()
                                                                           .Where(x => x != script)
                                                                           .ToArray();

                                Config.Instance.ScriptHistory = string.Join("|", scripts);
                                Config.Instance.Save();
                            }
                        };
                    this.historyBtn.DropDownItems.Add(item);
                }

                {
                    this.historyBtn.DropDownItems.Add(new ToolStripSeparator());
                    var item = new ToolStripMenuItem("Clear Recent Scripts List");
                    item.Click += (s, e) =>
                        {
                            this.historyBtn.DropDownItems.Clear();
                            Config.Instance.ScriptHistory = "";
                            Config.Instance.Save();
                            ReloadScriptHistory();
                        };
                    this.historyBtn.DropDownItems.Add(item);
                }
            }
        }

        void runBtn_Click(object sender, EventArgs e)
        {
            Plugin.RunScript();  //important not to call Run directly but run the injected Plugin.RunScript
        }

        void EditItem(string scriptFile)
        {
            Npp.Editor.OpenFile(scriptFile, true);
        }

        void newBtn_Click(object sender, EventArgs e)
        {
            using (var input = new ScripNameInput())
            {
                if (input.ShowDialog() != DialogResult.OK)
                    return;

                string scriptName = NormalizeScriptName(input.ScriptName ?? "New Script");

                int index = Directory.GetFiles(CSScriptHelper.ScriptsDir, scriptName + "*.cs").Length;

                string newScript = Path.Combine(CSScriptHelper.ScriptsDir, scriptName + ".cs");
                if (index != 0)
                {
                    int count = 0;
                    do
                    {
                        count++;
                        index++;
                        newScript = Path.Combine(CSScriptHelper.ScriptsDir, string.Format("{0}{1}.cs", scriptName, index));
                        if (count > 10)
                        {
                            MessageBox.Show("Too many script files with the similar name already exists.\nPlease specify a different file name or clean up some existing scripts.", "CS-Script");
                        }
                    }
                    while (File.Exists(newScript));
                }

                var p = new Process();
                p.StartInfo.FileName = "dotnet";
                p.StartInfo.Arguments = Config.Instance.ClasslessScriptByDefault ?
                                            $"\"{Runtime.cscs_asm}\" -new:toplevel \"{newScript}\"" :
                                            $"\"{Runtime.cscs_asm}\" -new:console \"{newScript}\"";

                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                p.WaitForExit();

                try
                {
                    PluginBase.Editor.Open(newScript);
                }
                catch
                {
                }
                PluginBase.GetCurrentDocument().GrabFocus();

                loadBtn.PerformClick();
            }
        }

        string NormalizeScriptName(string text)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) + "_";

            foreach (char c in invalid)
            {
                text = text.Replace(c.ToString(), "");
            }

            return text;
        }

        void SelectScript(string scriptFile)
        {
            if (treeView1.Nodes.Count > 0)
                foreach (TreeNode item in treeView1.Nodes[0].Nodes)
                    if (item.Tag is ProjectItem && string.Compare((item.Tag as ProjectItem).File, scriptFile, true) == 0)
                    {
                        treeView1.SelectedNode = item;
                        treeView1.Focus();
                        return;
                    }
        }

        bool CurrentDocumentBelongsToProject()
        {
            string file = Npp.Editor.GetCurrentFilePath();
            return DocumentBelongsToProject(file);
        }

        bool DocumentBelongsToProject(string file)
        {
            if (treeView1.Nodes.Count > 0)
                foreach (TreeNode item in treeView1.Nodes[0].Nodes)
                    if (item.Tag is ProjectItem && string.Compare((item.Tag as ProjectItem).File, file, true) == 0)
                        return true;

            return false;
        }

        string[] GetProjectDocuments()
        {
            var files = new List<string>();
            if (treeView1.Nodes.Count > 0)
                foreach (TreeNode item in treeView1.Nodes[0].Nodes)
                    if (item.Tag is ProjectItem)
                        files.Add((item.Tag as ProjectItem).File);

            return files.ToArray();
        }

        void synchBtn_Click(object sender, EventArgs e)
        {
            string path = Npp.Editor.GetCurrentFilePath();
            SelectScript(path);
        }

        void validateBtn_Click(object sender, EventArgs e)
        {
            Build();
        }

        void debugBtn_Click(object sender, EventArgs e)
        {
            if (currentScript == null || (Config.Instance.ReloadActiveScriptOnRun && currentScript != Npp.Editor.GetCurrentFilePath()))
                loadBtn.PerformClick();

            Plugin.DebugScript();
        }

        public void RunAsExternal()
        {
            Run(true);
        }

        public void Run()
        {
            Run(false);
        }

        void Run(bool asExternal)
        {
            if (currentScript == null || (Config.Instance.ReloadActiveScriptOnRun && currentScript != Npp.Editor.GetCurrentFilePath()))
                loadBtn.PerformClick();

            if (currentScript == null)
            {
                MessageBox.Show("Please load some script file first.", "CS-Script");
            }
            else
            {
                try
                {
                    if (!CurrentDocumentBelongsToProject())
                        EditItem(currentScript);

                    npp.SaveDocuments(GetProjectDocuments());

                    if (asExternal)
                    {
                        try
                        {
                            CSScriptHelper.ExecuteAsynch(currentScript);
                        }
                        catch (Exception e)
                        {
                            Plugin.ShowOutputPanel()
                                  .ShowBuildOutput()
                                  .WriteLine(e.Message)
                                  .SetCaretAtStart();
                        }
                    }
                    else
                    {
                        OutputPanel outputPanel = Plugin.ShowOutputPanel();

                        if (Config.Instance.StartDebugMonitorOnScriptExecution)
                            outputPanel.AttachDebgMonitor();

                        outputPanel.ClearAllDefaultOutputs();

                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                if (Config.Instance.InterceptConsole)
                                {
                                    CSScriptHelper.ExecuteScript(currentScript, OnRunStart, OnConsoleObjectOut);
                                }
                                else
                                {
                                    CSScriptHelper.ExecuteScript(currentScript, OnRunStart);
                                }
                            }
                            catch (Exception e)
                            {
                                this.InUiThread(() =>
                                {
                                    outputPanel.ShowBuildOutput()
                                               .WriteLine(e.Message)
                                               .SetCaretAtStart();
                                });
                            }
                            finally
                            {
                                this.InUiThread(() =>
                                {
                                    Plugin.RunningScript = null;
                                    RefreshControls();
                                    Npp.GetCurrentDocument().GrabFocus();
                                });
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Plugin.ShowOutputPanel()
                          .ShowBuildOutput()
                          .WriteLine(ex.Message)
                          .SetCaretAtStart();
                }
            }
        }

        public void Debug(bool breakOnFirstStep)
        {
            if (currentScript == null || (Config.Instance.ReloadActiveScriptOnRun && currentScript != Npp.Editor.GetCurrentFilePath()))
                loadBtn.PerformClick();

            if (currentScript == null)
            {
                MessageBox.Show("Please load some script file first.", "CS-Script");
            }
            else
            {
                try
                {
                    if (!CurrentDocumentBelongsToProject())
                        EditItem(currentScript);

                    npp.SaveDocuments(GetProjectDocuments());

                    try
                    {
                        CSScriptHelper.Debug(currentScript);
                    }
                    catch (Exception e)
                    {
                        Plugin.ShowOutputPanel()
                              .ShowBuildOutput()
                              .WriteLine(e.Message)
                              .SetCaretAtStart();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.ShowOutputPanel()
                          .ShowBuildOutput()
                          .WriteLine(ex.Message)
                          .SetCaretAtStart();
                }
            }
        }

        public void OpenInVS()
        {
            Cursor = Cursors.WaitCursor;

            PluginBase.Editor.SaveCurrentFile();

            try
            {
                var currFile = Npp.Editor.GetCurrentFilePath();

                CSScriptHelper.OpenAsVSProjectFor(currentScript ?? currFile);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }

            Cursor = Cursors.Default;
        }

        void OnRunStart(Process proc)
        {
            Plugin.RunningScript = proc;
            this.Invoke((Action)RefreshControls);
        }

        void OnConsoleObjectOut(object obj)
        {
            if (Plugin.OutputPanel.ConsoleOutput.IsEmpty)
                Plugin.OutputPanel.ShowConsoleOutput();

            if (obj is string)
                Plugin.OutputPanel.ConsoleOutput.WriteLine((string)obj);
            else
                foreach (char c in (char[])obj)
                    Plugin.OutputPanel.ConsoleOutput.WriteConsoleChar(c);
        }

        public void Build()
        {
            lock (this)
            {
                if (currentScript == null)
                    loadBtn.PerformClick();

                if (currentScript == null)
                {
                    MessageBox.Show("Please load some script file first.", "CS-Script");
                }
                else
                {
                    OutputPanel outputPanel = Plugin.ShowOutputPanel();

                    outputPanel.ShowBuildOutput();
                    outputPanel.BuildOutput.Clear();
                    outputPanel.BuildOutput.WriteLine("------ Build started: Script: " + Path.GetFileNameWithoutExtension(currentScript) + " ------");

                    try
                    {
                        if (!CurrentDocumentBelongsToProject())
                            EditItem(currentScript);

                        npp.SaveDocuments(GetProjectDocuments());

                        CSScriptHelper.Build(currentScript,
                                             line => outputPanel.BuildOutput.WriteLine(line));

                        outputPanel.BuildOutput.WriteLine(null)
                                               .WriteLine("========== Build: succeeded ==========")
                                               .SetCaretAtStart();
                    }
                    catch (Exception ex)
                    {
                        outputPanel.ShowBuildOutput()
                                   .WriteLine(null)
                                   .WriteLine(ex.Message)
                                   .WriteLine("========== Build: Failed ==========")
                                   .SetCaretAtStart();
                    }
                }
            }
        }

        void reloadBtn_Click(object sender, EventArgs e)
        {
            LoadScript(currentScript);
        }

        public void RefreshProjectStructure()
        {
            this.InUiThread(() =>
            {
                if (Npp.Editor.GetCurrentFilePath() == currentScript)
                {
                    try
                    {
                        Project project = CSScriptHelper.GenerateProjectFor(currentScript);

                        treeView1.BeginUpdate();

                        /*
                         root
                         references
                            assembly_1
                            assembly_2
                            assembly_n
                         script_1
                         script_2
                         script_N
                         */

                        TreeNode root = treeView1.Nodes[0];
                        TreeNode references = root.Nodes[0];

                        Action<TreeNode, string[]> updateNode =
                            (node, files) =>
                            {
                                string[] currentFiles = node.Nodes
                                                            .Cast<TreeNode>()
                                                            .Where(x => x.Tag is ProjectItem)
                                                            .Select(x => (x.Tag as ProjectItem).File)
                                                            .ToArray();

                                string[] newItems = files.Except(currentFiles).ToArray();

                                var orphantItems = node.Nodes
                                                       .Cast<TreeNode>()
                                                       .Where(x => x.Tag is ProjectItem)
                                                       .Where(x => !files.Contains((x.Tag as ProjectItem).File))
                                                       .Where(x => x != root && x != references)
                                                       .ToArray();

                                orphantItems.ForEach(x => node.Nodes.Remove(x));
                                newItems.ForEach(file =>
                                {
                                    int imageIndex = includeImage;
                                    var info = new ProjectItem(file) { IsPrimary = (file == project.PrimaryScript) };
                                    if (info.IsAssembly)
                                        imageIndex = assemblyImage;
                                    node.Nodes.Add(new TreeNode(info.Name) { ImageIndex = imageIndex, SelectedImageIndex = imageIndex, Tag = info, ToolTipText = file, ContextMenuStrip = itemContextMenu });
                                });
                            };

                        updateNode(references, project.Assemblies);
                        updateNode(root, project.SourceFiles);
                        root.Expand();

                        treeView1.EndUpdate();
                    }
                    catch (Exception e)
                    {
                        e.LogAsError();
                    }
                }
            });
        }

        void RefreshControls()
        {
            this.InUiThread(() =>
            {
                openInVsBtn.Visible = Utils.IsVS2017PlusAvailable;

                newBtn.Enabled = true;

                validateBtn.Enabled =
                reloadBtn.Enabled =
                synchBtn.Enabled =
                runBtn.Enabled = (treeView1.Nodes.Count > 0);

                bool running = (Plugin.RunningScript != null);
                runBtn.Enabled = !running;
                stopBtn.Enabled = running;

                if (running)
                {
                    validateBtn.Enabled =
                    debugBtn.Enabled =
                    openInVsBtn.Enabled =
                    loadBtn.Enabled =
                    newBtn.Enabled =
                    reloadBtn.Enabled = false;
                }
                else
                    loadBtn.Enabled = true;
            });
        }

        ProjectItem SelectedItem
        {
            get
            {
                if (treeView1.SelectedNode != null)
                    return treeView1.SelectedNode.Tag as ProjectItem;
                else
                    return null;
            }
        }

        void openInVsBtn_Click(object sender, EventArgs e)
        {
            OpenInVS();
        }

        void aboutBtn_Click(object sender, EventArgs e)
        {
            Plugin.ShowAbout();
        }

        void hlpBtn_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start("https://github.com/oleg-shilo/cs-script.npp/wiki");
            }
            catch { }
        }

        void loadBtn_Click(object sender, EventArgs e)
        {
            LoadCurrentDoc();
        }

        public void LoadCurrentDoc()
        {
            string path = Npp.Editor.GetCurrentFilePath();
            if (!File.Exists(path))
                PluginBase.Editor.SaveCurrentFile();

            path = Npp.Editor.GetCurrentFilePath(); // path may be already changed as the result of saving
            if (File.Exists(path))
                LoadScript(path);

            RefreshControls();

            Task.Factory.StartNew(CSScriptHelper.ClearVSDir);
        }

        const int scriptImage = 1;
        const int folderImage = 0;
        const int assemblyImage = 2;
        const int includeImage = 3;
        const int scriptVbImage = 4;

        void UnloadScript()
        {
            currentScript = null;
            treeView1.Nodes.Clear();
        }

        public void LoadScript(string scriptFile)
        {
            if (!string.IsNullOrWhiteSpace(scriptFile) && File.Exists(scriptFile))
            {
                if (!scriptFile.IsScriptFile())
                {
                    MessageBox.Show("The file type '" + Path.GetExtension(scriptFile) + "' is not supported.", "CS-Script");
                }
                else
                {
                    try
                    {
                        Npp.Editor.OpenFile(scriptFile, true);

                        Project project = CSScriptHelper.GenerateProjectFor(scriptFile);

                        /*
                        root
                        references
                           assembly_1
                           assembly_2
                           assembly_n
                        script_1
                        script_2
                        script_N
                        */

                        treeView1.BeginUpdate();
                        treeView1.Nodes.Clear();

                        TreeNode root = treeView1.Nodes.Add("Script '" + Path.GetFileNameWithoutExtension(scriptFile) + "'");
                        TreeNode references = root.Nodes.Add("References");

                        root.SelectedImageIndex =
                        root.ImageIndex = scriptFile.IsVbFile() ? scriptVbImage : scriptImage;
                        references.SelectedImageIndex =
                        references.ImageIndex = assemblyImage;

                        root.ContextMenuStrip = solutionContextMenu;
                        root.ToolTipText = "Script: " + scriptFile;

                        Action<TreeNode, string[]> populateNode =
                            (node, files) =>
                            {
                                foreach (var file in files)
                                {
                                    int imageIndex = includeImage;
                                    var info = new ProjectItem(file) { IsPrimary = (file == project.PrimaryScript) };
                                    if (info.IsPrimary)
                                        imageIndex = file.IsVbFile() ? scriptVbImage : scriptImage;
                                    if (info.IsAssembly)
                                        imageIndex = assemblyImage;
                                    node.Nodes.Add(new TreeNode(info.Name) { ImageIndex = imageIndex, SelectedImageIndex = imageIndex, Tag = info, ToolTipText = file, ContextMenuStrip = itemContextMenu });
                                };
                            };

                        populateNode(references, project.Assemblies);
                        populateNode(root, project.SourceFiles);
                        root.Expand();

                        treeView1.EndUpdate();

                        currentScript = scriptFile;

                        var history = Config.Instance.ScriptHistory.Split('|').ToList();
                        history.Remove(scriptFile);
                        history.Insert(0, scriptFile);

                        Config.Instance.ScriptHistory = string.Join("|", history.Take(Config.Instance.SciptHistoryMaxCount).ToArray());
                        Config.Instance.Save();
                        ReloadScriptHistory();
                    }
                    catch (Exception e)
                    {
                        //it is not a major use-case so doesn't matter why we failed
                        MessageBox.Show("Cannot load script.\nError: " + e.Message, "CS-Script");
                        e.LogAsError();
                    }
                }
            }
            else
            {
                MessageBox.Show("Script '" + scriptFile + "' does not exist.", "CS-Script");
            }
            RefreshControls();
        }

        void outputBtn_Click(object sender, EventArgs e)
        {
            Plugin.ToggleScondaryPanels();
        }

        void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (SelectedItem != null && !SelectedItem.IsAssembly)
            {
                EditItem(SelectedItem.File);
                PluginBase.Editor.SaveCurrentFile();
            }
        }

        void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            RefreshControls();
        }

        void stopBtn_Click(object sender, EventArgs e)
        {
            Plugin.Stop();
            RefreshControls();
        }

        void treeView1_AfterExpand(object sender, TreeViewEventArgs e)
        {
            if (e.Node == treeView1.Nodes[0].Nodes[0])
            {
                e.Node.ImageIndex = e.Node.SelectedImageIndex = folderImage;
            }
        }

        void treeView1_AfterCollapse(object sender, TreeViewEventArgs e)
        {
            if (e.Node == treeView1.Nodes[0].Nodes[0])
            {
                e.Node.ImageIndex = e.Node.SelectedImageIndex = assemblyImage;
            }
        }

        void unloadScriptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UnloadScript();
        }

        void openCommandPromptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WithSelectedNodeProjectItem(item =>
            {
                string file;
                if (treeView1.SelectedNode == treeView1.Nodes[0]) //root node
                    file = currentScript;
                else if (item != null)
                    file = item.File;
                else
                    return;

                string path = Path.GetDirectoryName(file);

                if (Directory.Exists(path))
                    Process.Start("cmd.exe", "/K \"cd " + path + "\"");
                else
                    MessageBox.Show("Directory '" + path + "' does not exist.", "CS-Script");
            });
        }

        void openContainingFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WithSelectedNodeProjectItem(item =>
            {
                if (item != null)
                {
                    string path = item.File;
                    if (File.Exists(path))
                        Process.Start("explorer.exe", "/select," + path);
                    else
                        MessageBox.Show("File '" + path + "' does not exist.", "CS-Script");
                }
            });
        }

        void WithSelectedNodeProjectItem(Action<ProjectItem> action)
        {
            if (treeView1.SelectedNode != null)
            {
                try
                {
                    action(treeView1.SelectedNode.Tag as ProjectItem);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString(), "CS-Script");
                }
            }
        }

        void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView1.SelectedNode = e.Node;
            }
        }

        void openScriptsFolderBtn_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", CSScriptHelper.ScriptsDir);
            }
            catch { }
        }

        void configBtn_Click(object sender, EventArgs e)
        {
            Plugin.ShowConfig();
        }

        void deployBtn_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;

            try
            {
                if (currentScript == null)
                    LoadCurrentDoc();

                if (currentScript != null) //may not necessarily be loaded successfully

                    using (var dialog = new DeploymentInput())
                        if (DialogResult.OK == dialog.ShowModal())
                        {
                            EditItem(currentScript);

                            npp.SaveDocuments(GetProjectDocuments());

                            var result = CSScriptHelper.Isolate(currentScript, dialog.AsScript, dialog.AsWindowApp, dialog.AsDll);
                            if (result != null)
                                Process.Start("explorer.exe", $"\"{result}\"");
                        }
            }
            catch (Exception ex)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show(ex.ToString(), "CS-Script");
            }
            Cursor.Current = Cursors.Default;
        }

        void shortcutsBtn_Click(object sender, EventArgs e)
        {
            using (var dialog = new PluginShortcuts())
                dialog.ShowModal();
        }

        void pictureBox1_Click(object sender, EventArgs e)
        {
            whatsNewPanel.Visible = false;
        }

        void treeView1_SizeChanged(object sender, EventArgs e)
        {
            treeView1.Invalidate(); //WinForm TreeView has nasty rendering artifact on window maximize
        }

        void organizeButtonsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(settingsFile);
            }
            catch { }
        }

        void restartNppBtn_Click(object sender, EventArgs e)
        {
            Utils.RestartNpp();
        }

        void ProjectPanel_Deactivate(object sender, EventArgs e)
        {
            this.Refresh();
            System.Diagnostics.Debug.WriteLine("ProjectPanel_Deactivate");
        }

        void favoritesBtn_Click(object sender, EventArgs e)
        {
            tabControl1.SelectTabWith(favPanel);
            favPanel.Add(Npp.Editor.GetCurrentFilePath());
        }
    }

    static class FormExtensions
    {
        public static TabControl AddTab(this TabControl control, string tabName, Form content)
        {
            var page = new TabPage
            {
                Padding = new System.Windows.Forms.Padding(3),
                TabIndex = control.TabPages.Count,
                Text = tabName,
                BackColor = System.Drawing.Color.White,
                UseVisualStyleBackColor = true
            };

            control.Controls.Add(page);

            content.TopLevel = false;
            content.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            content.Parent = page;
            page.Controls.Add(content);
            content.Dock = DockStyle.Fill;
            content.Visible = true;

            return control;
        }

        public static TabControl SelectTabWith(this TabControl control, Form content)
        {
            var tab = control.Controls
                             .Cast<Control>()
                             .Where(c => c is TabPage)
                             .Cast<TabPage>()
                             .Where(page => page.Controls.Contains(content))
                             .FirstOrDefault();
            if (tab != null)
                control.SelectedTab = tab;
            return control;
        }
    }
}