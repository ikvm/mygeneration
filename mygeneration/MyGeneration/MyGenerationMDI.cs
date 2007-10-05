using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;

using WeifenLuo.WinFormsUI;
using WeifenLuo.WinFormsUI.Docking;

using Scintilla;
using Scintilla.Enums;
using Scintilla.Forms;
using Scintilla.Printing;
using Scintilla.Configuration.Legacy;
using Scintilla.Configuration;
using Scintilla.Configuration.SciTE;

//using Zeus.Projects;
using Zeus;
using MyGeneration.Forms;

namespace MyGeneration
{
    public partial class MyGenerationMDI : Form, IMyGenerationMDI
    {
        private const string DOCK_CONFIG_FILE = @"\settings\dock.config";
        private const string SCINTILLA_CONFIG_FILE = @"\settings\scintillanet.xml";
        private const string REPLACEMENT_SUFFIX = "$REPLACEMENT$.dll";

        private const string URL_HOMEPAGE = "http://www.mygenerationsoftware.com/home/";
        private const string URL_DOCUMENTATION = "http://www.mygenerationsoftware.com/Documentation.aspx";
        private const string URL_LATESTVERSION = "http://www.mygenerationsoftware.com/LatestVersion";
        private const string URL_SOURCEFORGE_DOWNLOAD = "http://sourceforge.net/project/showfiles.php?group_id=198893";

        private static ConfigFile configFile;
        private ScintillaConfigureDelegate configureDelegate;
        
        private FindForm findDialog = new FindForm();
        private ReplaceForm replaceDialog = new ReplaceForm();

        private Dictionary<string, IMyGenContent> dynamicContentWindows = new Dictionary<string, IMyGenContent>();
        private DefaultSettings settings;
        private LanguageMappings languageMappings;
        private DbTargetMappings dbTargetMappings;
        private MetaDataBrowser metaDataBrowser;
        private UserMetaData userMetaData;
        private GlobalUserMetaData globalUserMetaData;
        private MetaProperties metaProperties;
        private DefaultProperties options;
        private TemplateBrowser templateBrowser;
        private ConsoleForm consoleForm;
        private ErrorsForm errorsForm;
        
        private string startupPath;
        private string[] startupFiles;

        public MyGenerationMDI(string startupPath, params string[] args)
        {
            this.startupPath = startupPath;
            
            settings = DefaultSettings.Instance;

            //Any files that were locked when the TemplateLibrary downloaded and tried to replace them will be replaced now.
            ProcessReplacementFiles();

            InitializeComponent();

            startupFiles = args;

            EditorManager.AddNewDocumentMenuItems(newFileDynamicToolStripMenuItem_Click,
                newToolStripMenuItem.DropDownItems,
                toolStripDropDownButtonNew.DropDownItems);

            ContentManager.AddNewContentMenuItems(openContentDynamicToolStripMenuItem_Click, this.pluginsToolStripMenuItem,
                this.toolStrip1);

            this.RefreshRecentFiles();
        }

        private void MyGenerationMDI_Load(object sender, EventArgs e)
        {
            switch (settings.WindowState)
            {
                case "Maximized":

                    this.WindowState = FormWindowState.Maximized;
                    break;

                case "Minimized":

                    this.WindowState = FormWindowState.Minimized;
                    break;

                case "Normal":

                    int x = Convert.ToInt32(settings.WindowPosLeft);
                    int y = Convert.ToInt32(settings.WindowPosTop);
                    int w = Convert.ToInt32(settings.WindowPosWidth);
                    int h = Convert.ToInt32(settings.WindowPosHeight);

                    this.Location = new System.Drawing.Point(x, y);
                    this.Size = new Size(w, h);
                    break;
            }

            // Load up the scintilla configuration
            ConfigurationUtility cu = new ConfigurationUtility();

            FileInfo scintillaConfigFile = new FileInfo(startupPath + SCINTILLA_CONFIG_FILE);
            #region HACK: this needs to be cleaned up at some point.
            // If the file doesn't exist, create it.
            if (scintillaConfigFile.Exists)
            {
                //TODO: Retry this with a copy of the file until we can upgrade Scintilla.Net with a fix.
                int maxTries = 3;
                while (maxTries > 0)
                {
                    try
                    {
                        configFile = cu.LoadConfiguration(scintillaConfigFile.FullName) as ConfigFile;
                        break;
                    }
                    catch
                    {
                        if (--maxTries == 1)
                        {
                            File.Copy(scintillaConfigFile.FullName, scintillaConfigFile.FullName + ".tmp", true);
                            scintillaConfigFile = new FileInfo(scintillaConfigFile.FullName + ".tmp");
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(10);
                        }
                    }
                }
            }
            #endregion
            if (configFile != null)
            {
                configureDelegate = configFile.MasterScintilla.Configure; 
                ZeusScintillaControl.StaticConfigure = configureDelegate;
            }

            // Dock Content configuration
            DeserializeDockContent deserializeDockContent = new DeserializeDockContent(GetContentFromPersistString);
            string dockConfigFileName = startupPath + DOCK_CONFIG_FILE;

            if (File.Exists(dockConfigFileName))
            {
                dockPanel.LoadFromXml(dockConfigFileName, deserializeDockContent);
            }

            // Startup files from the command line
            if (this.startupFiles != null)
            {
                OpenDocuments(startupFiles);
            }

            // Show Default Properties if this is the first load.
            if (settings.FirstLoad)
            {
                if (this.OptionsDockContent.IsHidden)
                    this.OptionsDockContent.Show(this.dockPanel);

                this.OptionsDockContent.Activate();
            }
        }

        private void PickFiles()
        {
            DefaultSettings settings = DefaultSettings.Instance;

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = Directory.GetCurrentDirectory();
            openFileDialog.Filter = EditorManager.OpenFileDialogString;
            openFileDialog.RestoreDirectory = true;
            openFileDialog.Multiselect = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                this.OpenDocuments(openFileDialog.FileNames);
            }
        }

        public void OpenContent(params string[] keys)
        {
            foreach (string key in keys)
            {
                if (dynamicContentWindows.ContainsKey(key))
                {
                    IMyGenContent mygenContent = dynamicContentWindows[key];
                    if (mygenContent.DockContent.Visible)
                    {
                        mygenContent.DockContent.Hide();
                    }
                    else
                    {
                        mygenContent.DockContent.Show(dockPanel);
                    }
                }
                else
                {
                    IMyGenContent mygenContent = ContentManager.CreateContent(this, key);
                    if (mygenContent != null)
                    {
                        dynamicContentWindows[key] = mygenContent;
                        mygenContent.DockContent.Show(dockPanel);
                    }
                }
            }
        }

        public void CreateDocument(params string[] fileTypes)
        {
            foreach (string fileType in fileTypes)
            {
                IMyGenDocument mygenDoc = EditorManager.CreateDocument(this, fileType);
                if (mygenDoc != null)
                {
                    mygenDoc.DockContent.Show(dockPanel);
                }
            }
        }

        public void OpenDocuments(params string[] filenames)
        {
            foreach (string file in filenames)
            {
                FileInfo info = new FileInfo(file);
                
                if (info.Exists)
                {
                    if (this.IsDocumentOpen(info.FullName))
                    {
                        this.FindDocument(info.FullName).DockContent.Activate();
                    }
                    else
                    {
                        bool isOpened = false;
                        IMyGenDocument mygenDoc = EditorManager.OpenDocument(this, info.FullName);
                        if (mygenDoc != null)
                        {
                            isOpened = true;
                            mygenDoc.DockContent.Show(dockPanel);
                            this.AddRecentFile(info.FullName);
                        }

                        if (!isOpened)
                        {
                            MessageBox.Show(this, string.Format("Unknown file type in file {0}", info.Name), "Unknown file type");
                        }
                    }
                }
            }
        }

        private IDockContent GetContentFromPersistString(string persistString)
        {
            IDockContent content = null;
            string[] parsedStrings = persistString.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (parsedStrings.Length == 1)
            {
                string type = parsedStrings[0];
                if (type == typeof(LanguageMappings).ToString())
                {
                    content = this.LanguageMappingsDockContent;
                }
                else if (type == typeof(DbTargetMappings).ToString())
                {
                    content = this.DbTargetMappingsDockContent;
                }
                else if (type == typeof(MetaProperties).ToString())
                {
                    content = this.MetaPropertiesDockContent;
                }
                else if (type == typeof(MetaDataBrowser).ToString())
                {
                    content = this.MetaDataBrowserDockContent;
                }
                else if (type == typeof(UserMetaData).ToString())
                {
                    content = this.UserMetaDataDockContent;
                }
                else if (type == typeof(GlobalUserMetaData).ToString())
                {
                    content = this.GlobalUserMetaDataDockContent;
                }
                else if (type == typeof(TemplateBrowser).ToString())
                {
                    content = this.TemplateBrowserDockContent;
                }
                else if (type == typeof(DefaultProperties).ToString())
                {
                    content = this.OptionsDockContent;
                }
                else if (type == typeof(ErrorsForm).ToString())
                {
                    content = this.ErrorsDockContent;
                }
                else if (type == typeof(ConsoleForm).ToString())
                {
                    content = this.ConsoleDockContent;
                }
                else
                {
                    // Preload all dynamicContentWindows here if needed
                    foreach (IContentManager cm in PluginManager.ContentManagers.Values) 
                    {
                        dynamicContentWindows[cm.Name] = cm.Create(this);
                    }
                    foreach (IMyGenContent c in dynamicContentWindows.Values)
                    {
                        if (type == c.GetType().ToString())
                        {
                            content = c.DockContent;
                            break;
                        }
                    }
                }
            }
            else if (parsedStrings.Length >= 2)
            {
                string type = parsedStrings[0];
                string arg = parsedStrings[1];

                IMyGenDocument doc;
                if (string.Equals(type, "file", StringComparison.CurrentCultureIgnoreCase))
                {
                    doc = EditorManager.OpenDocument(this, arg);
                    if (doc != null) content = doc.DockContent;
                }
                if (string.Equals(type, "type", StringComparison.CurrentCultureIgnoreCase))
                {
                    doc = EditorManager.CreateDocument(this, arg);
                    if (doc != null) content = doc.DockContent;
                }
            }

            return content;
        }

        private void dockPanel_ActiveContentChanged(object sender, EventArgs e)
        {
            
            IDockContent fe = this.dockPanel.ActiveContent as IDockContent;
            if (fe is IEditControl)
            {
                ToolStripManager.RevertMerge(toolStrip1);
            }
            else if (fe is IMyGenDocument)
            {
                ToolStripManager.RevertMerge(toolStrip1);
                IMyGenDocument mgd = fe as IMyGenDocument;
                if (mgd.ToolStrip != null)
                {
                    ToolStripManager.Merge(mgd.ToolStrip, this.toolStrip1);
                }
            }
            else if (fe == null)
            {
                bool foundDoc = false;
                foreach (DockContent c in dockPanel.Contents) {
                    if ((c is IMyGenDocument) && (!c.IsHidden))
                    {
                        foundDoc = true;
                        break;
                    }
                }
                if (!foundDoc) ToolStripManager.RevertMerge(toolStrip1);
            }
        }


        private bool Shutdown(bool allowPrevent)
        {
            bool shutdown = true;
            string dockConfigFileName = startupPath + DOCK_CONFIG_FILE;

            IMyGenContent bw = null;
            DockContentCollection coll = this.dockPanel.Contents;
            bool canClose = true;

            for (int i = 0; i < coll.Count; i++)
            {
                bw = coll[i] as IMyGenContent;

                // We need the MetaDataBrowser window to be closed last because it houses the UserMetaData.
                if (!(bw is MetaDataBrowser))
                {
                    canClose = bw.CanClose(allowPrevent);

                    if (allowPrevent && !canClose)
                    {
                        shutdown = false;
                        break;
                    }
                }

                // Close hidden windows.
                if (coll[i].DockHandler.IsHidden)
                {
                    coll[i].DockHandler.Close();
                }
            }

            if (shutdown)
            {
                dockPanel.SaveAsXml(dockConfigFileName);
            }

            return shutdown;
        }

        private void MyGenerationMDI_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!this.Shutdown(true))
            {
                e.Cancel = true;
                return;
            }

            //Application.Exit();
        }

        #region DockManager Helper Methods
        public bool IsDocumentOpen(string text, params IMyGenDocument[] docsToExclude)
        {
            IMyGenDocument doc = this.FindDocument(text, docsToExclude);
            return (doc != null);
        }

        public IMyGenDocument FindDocument(string text, params IMyGenDocument[] docsToExclude)
        {
            IMyGenDocument found = null;
            if (dockPanel.DocumentStyle == DocumentStyle.SystemMdi)
            {
                foreach (Form form in MdiChildren)
                {
                    if (form is IMyGenDocument)
                    {
                        IMyGenDocument doc = form as IMyGenDocument;
                        if (doc.DocumentIndentity == text)
                        {
                            foreach (IMyGenDocument exclude in docsToExclude)
                            {
                                if (exclude == doc) doc = null;
                            }
                            if (doc != null)
                            {
                                found = doc;
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (IDockContent content in dockPanel.Documents)
                {
                    if (content is IMyGenDocument)
                    {
                        IMyGenDocument doc = content as IMyGenDocument;
                        if (doc.DocumentIndentity == text)
                        {
                            foreach (IMyGenDocument exclude in docsToExclude)
                            {
                                if (exclude == doc) doc = null;
                            }
                            if (doc != null)
                            {
                                found = doc;
                                break;
                            }
                        }
                    }
                }
            }

            return found;
        }
        #endregion

        #region Menu Events
        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PickFiles();
        }

        private void newFileDynamicToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem i = sender as ToolStripMenuItem;
            this.CreateDocument(i.Text);
        }

        private void openContentDynamicToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripItem i = sender as ToolStripItem;
            if (string.IsNullOrEmpty(i.Text)) 
                this.OpenContent(i.ToolTipText);
            else 
                this.OpenContent(i.Text);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            NewAbout ab = new NewAbout();
            ab.ShowDialog(this);
        }

        private void jScriptTemplateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CreateDocument(ZeusConstants.Engines.MICROSOFT_SCRIPT, ZeusConstants.Languages.JSCRIPT);
        }

        private void vBScriptTemplateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CreateDocument(ZeusConstants.Engines.MICROSOFT_SCRIPT, ZeusConstants.Languages.VBSCRIPT);
        }

        private void cTemplateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CreateDocument(ZeusConstants.Engines.DOT_NET_SCRIPT, ZeusConstants.Languages.CSHARP);
        }

        private void vBNetTemplateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CreateDocument(ZeusConstants.Engines.DOT_NET_SCRIPT, ZeusConstants.Languages.VBNET);
        }

        private void projectToolStripMenuItem1_Click(object sender, EventArgs e)
        {
        }

        private void defaultSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.OptionsDockContent.IsHidden)
            {
                this.OptionsDockContent.Show(this.dockPanel);
            }
            else
            {
                this.OptionsDockContent.Activate();
            }
        }

        private void contentsToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void recentFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)
            {
                ToolStripMenuItem item = sender as ToolStripMenuItem;
                string file = item.Text;
                if (File.Exists(file))
                {
                    AddRecentFile(item.Text);
                    OpenDocuments(item.Text);
                }
                else
                {
                    // May want to add text to resource file for internationalization
                    MessageBox.Show(this, string.Format("The file \"{0}\" no longer exists.", item.Text), "File Missing"); 
                    RemoveRecentFile(item.Text);
                }
            }
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StartVersionCheck();
        }
        #endregion

        #region Toolstrip Button Events
        private void toolStripButtonOpen_Click(object sender, EventArgs e)
        {
            PickFiles();
        }

        private void toolStripButtonTemplateBrowser_Click(object sender, EventArgs e)
        {
            if (this.TemplateBrowserDockContent.IsHidden)
            {
                this.TemplateBrowserDockContent.Show(this.dockPanel);
            }
            else
            {
                this.TemplateBrowserDockContent.Activate();
            }
        }

        private void toolStripButtonOptions_Click(object sender, EventArgs e)
        {
            if (this.OptionsDockContent.IsHidden)
            {
                this.OptionsDockContent.Show(this.dockPanel);
            }
            else
            {
                this.OptionsDockContent.Hide();
            }
        }

        private void toolStripButtonMyMetaBrowser_Click(object sender, EventArgs e)
        {
            if (this.MetaDataBrowserDockContent.IsHidden)
            {
                this.MetaDataBrowserDockContent.Show(this.dockPanel);
            }
            else
            {
                this.MetaDataBrowserDockContent.Activate();
            }
        }

        private void toolStripButtonMyMetaProperties_Click(object sender, EventArgs e)
        {
            if (this.MetaPropertiesDockContent.IsHidden)
            {
                this.MetaPropertiesDockContent.Show(this.dockPanel);
            }
            else
            {
                this.MetaPropertiesDockContent.Activate();
            }
        }

        private void toolStripButtonLangMappings_Click(object sender, EventArgs e)
        {
            if (this.LanguageMappingsDockContent.IsHidden)
            {
                this.LanguageMappingsDockContent.Show(this.dockPanel);
            }
            else
            {
                this.LanguageMappingsDockContent.Activate();
            }
        }

        private void toolStripButtonDbTargetMappings_Click(object sender, EventArgs e)
        {
            if (this.DbTargetMappingsDockContent.IsHidden)
            {
                this.DbTargetMappingsDockContent.Show(this.dockPanel);
            }
            else
            {
                this.DbTargetMappingsDockContent.Activate();
            }
        }

        private void toolStripButtonLocalAliases_Click(object sender, EventArgs e)
        {
            if (this.UserMetaDataDockContent.IsHidden)
            {
                this.UserMetaDataDockContent.Show(this.dockPanel);
            }
            else
            {
                this.UserMetaDataDockContent.Activate();
            }
        }

        private void toolStripButtonGlobalAliases_Click(object sender, EventArgs e)
        {
            if (this.GlobalUserMetaDataDockContent.IsHidden)
            {
                this.GlobalUserMetaDataDockContent.Show(this.dockPanel);
            }
            else
            {
                this.GlobalUserMetaDataDockContent.Activate();
            }
        }

        private void toolStripButtonConsole_Click(object sender, EventArgs e)
        {
            if (this.ConsoleDockContent.IsHidden)
            {
                this.ConsoleDockContent.Show(this.dockPanel);
            }
            else
            {
                this.ConsoleDockContent.Activate();
            }
        }

        private void toolStripButtonErrors_Click(object sender, EventArgs e)
        {
            if (this.ErrorsDockContent.IsHidden)
            {
                this.ErrorsDockContent.Show(this.dockPanel);
            }
            else
            {
                this.ErrorsDockContent.Activate();
            }
        }
        #endregion

        #region Lazy Load Windows
        public ConsoleForm ConsoleDockContent
        {
            get
            {
                if ((consoleForm != null) && consoleForm.IsDisposed) consoleForm = null;
                if (consoleForm == null) consoleForm = new ConsoleForm(this);
                return consoleForm;
            }
        }
        public ErrorsForm ErrorsDockContent
        {
            get
            {
                if ((errorsForm != null) && errorsForm.IsDisposed) errorsForm = null;
                if (errorsForm == null) errorsForm = new ErrorsForm(this);
                return errorsForm;
            }
        }
        public DefaultProperties OptionsDockContent
        {
            get
            {
                if ((options != null) && options.IsDisposed) options = null;
                if (options == null) options = new DefaultProperties(this);
                return options;
            }
        }

        public TemplateBrowser TemplateBrowserDockContent
        {
            get
            {
                if ((templateBrowser != null) && templateBrowser.IsDisposed) templateBrowser = null;
                if (templateBrowser == null) templateBrowser = new TemplateBrowser(this);
                return templateBrowser;
            }
        }

        public LanguageMappings LanguageMappingsDockContent
        {
            get
            {
                if ((languageMappings != null) && languageMappings.IsDisposed) languageMappings = null;
                if (languageMappings == null) languageMappings = new LanguageMappings(this);
                return languageMappings;
            }
        }

        public DbTargetMappings DbTargetMappingsDockContent
        {
            get
            {
                if ((dbTargetMappings != null) && dbTargetMappings.IsDisposed) dbTargetMappings = null;
                if (dbTargetMappings == null) dbTargetMappings = new DbTargetMappings(this);
                return dbTargetMappings;
            }
        }

        public MetaDataBrowser MetaDataBrowserDockContent
        {
            get
            {
                if ((metaDataBrowser != null) && metaDataBrowser.IsDisposed) metaDataBrowser = null;
                if (metaDataBrowser == null) metaDataBrowser = new MetaDataBrowser(this, 
                    MetaPropertiesDockContent, UserMetaDataDockContent, GlobalUserMetaDataDockContent);
                return metaDataBrowser;
            }
        }

        public UserMetaData UserMetaDataDockContent
        {
            get
            {
                if ((userMetaData != null) && userMetaData.IsDisposed) userMetaData = null;
                if (userMetaData == null)
                {
                    userMetaData = new UserMetaData(this);
                    userMetaData.MetaDataBrowser = this.MetaDataBrowserDockContent;
                }
                return userMetaData;
            }
        }

        public GlobalUserMetaData GlobalUserMetaDataDockContent
        {
            get
            {
                if ((globalUserMetaData != null) && globalUserMetaData.IsDisposed) globalUserMetaData = null;
                if (globalUserMetaData == null)
                {
                    globalUserMetaData = new GlobalUserMetaData(this);
                    globalUserMetaData.MetaDataBrowser = this.MetaDataBrowserDockContent;
                }
                return globalUserMetaData;
            }
        }

        public MetaProperties MetaPropertiesDockContent
        {
            get
            {
                if ((metaProperties != null) && metaProperties.IsDisposed) metaProperties = null;
                if (metaProperties == null) metaProperties = new MetaProperties(this);
                return metaProperties;
            }
        }
        #endregion

        #region Replaces files that couldn't be replaced until locks were removed
        private void ProcessReplacementFiles()
        {
            DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(Application.ExecutablePath));
            foreach (FileInfo info in dir.GetFiles("*" + REPLACEMENT_SUFFIX))
            {
                FileInfo fileToReplace = new FileInfo(info.FullName.Replace(REPLACEMENT_SUFFIX, ".dll"));
                try
                {
                    if (fileToReplace.Exists)
                    {
                        fileToReplace.MoveTo(fileToReplace.FullName + "." + DateTime.Now.ToString("yyyyMMddhhmmss") + ".bak");
                    }

                    info.MoveTo(fileToReplace.FullName);
                }
#if DEBUG
				catch (Exception ex) { throw ex; }
#else
                catch { }
#endif
            }
        }
        #endregion

        #region Refresh the Recent files menu item
        public void RefreshRecentFiles()
        {
            // Clear the Recent Items List
            this.recentFilesToolStripMenuItem.DropDownItems.Clear();

            DefaultSettings ds = DefaultSettings.Instance;
            if (ds.RecentFiles.Count == 0)
            {
                recentFilesToolStripMenuItem.Visible = false;
            }
            else
            {
                recentFilesToolStripMenuItem.Visible = true;

                foreach (string path in ds.RecentFiles)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(path);
                    item.Click += new EventHandler(recentFilesToolStripMenuItem_Click);
                    recentFilesToolStripMenuItem.DropDownItems.Add(item);
                }
            }
        }

        public void AddRecentFile(string path)
        {

            if (settings.RecentFiles.Contains(path))
            {
                settings.RecentFiles.Remove(path);
            }

            settings.RecentFiles.Insert(0, path);
            settings.Save();

            RefreshRecentFiles();
        }

        public void RemoveRecentFile(string path)
        {
            if (settings.RecentFiles.Contains(path))
            {
                settings.RecentFiles.Remove(path);
            }
            settings.Save();

            RefreshRecentFiles();
        }
        #endregion

        #region Check Application Version on Server

        private void StartVersionCheck()
        {
            if (DefaultSettings.Instance.CheckForNewBuild)
            {
                MyGeneration.com.mygenerationsoftware.www.VersionInfo versionInfo = new MyGeneration.com.mygenerationsoftware.www.VersionInfo();

                try
                {
                    string newVersion = versionInfo.GetVersion();
                    System.Reflection.Assembly asmblyMyGen = System.Reflection.Assembly.GetAssembly(typeof(NewAbout));
                    string currentVersion = asmblyMyGen.GetName().Version.ToString();

                    System.Version currentVersionObject = asmblyMyGen.GetName().Version;
                    System.Version newVersionObject = new System.Version(newVersion);

                    if (newVersionObject > currentVersionObject)
                    {
                        this.Update();

                        UpdatesForm form = new UpdatesForm();
                        form.NewVersion = newVersion;
                        form.ThisVersion = currentVersion;
                        form.UpgradeText = versionInfo.GetUpdateText();
                        DialogResult result = form.ShowDialog();

                        if (result == DialogResult.OK)
                        {
                            DownloadLatestVersion();
                        }
                    }
                    else if (newVersionObject < currentVersionObject)
                    {
                        DialogResult result = MessageBox.Show(this, 
                            @"You are running an currently unreleased build. Do you want to check the SourceForge page for updates now?", 
                            "Version Information", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        
                        if (result == DialogResult.Yes)
                        {
                            Zeus.WindowsTools.LaunchBrowser(URL_SOURCEFORGE_DOWNLOAD);
                        }
                    }
                }
                catch (Exception wsex)
                {
                    this.ErrorsOccurred(wsex);
                }
            }
        }

        private void DownloadLatestVersion()
        {
            Zeus.WindowsTools.LaunchBrowser(URL_LATESTVERSION, System.Diagnostics.ProcessWindowStyle.Minimized, true);
        }
        #endregion

        #region Show OLEDBDialog Dialog

        protected string BrowseOleDbConnectionString(string connstr)
        {
            MSDASC.DataLinksClass dl = new MSDASC.DataLinksClass();

            ADODB.Connection conn = new ADODB.ConnectionClass();
            conn.ConnectionString = connstr;

            object objCn = (object)conn;

            if (dl.PromptEdit(ref objCn))
            {
                return conn.ConnectionString;
            }

            return null;
        }
        #endregion

        #region IMyGenerationMDI Members

        public FindForm FindDialog
        {
            get { return findDialog; }
        }

        public ReplaceForm ReplaceDialog
        {
            get { return replaceDialog; }
        }

        public ScintillaConfigureDelegate ConfigureDelegate
        {
            get { return configureDelegate; }
        }

        public DockPanel DockPanel
        {
            get { return dockPanel;  }
        }

        private void docItemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem i = sender as ToolStripMenuItem;
            if (i != null)
            {
                IMyGenDocument mgd = this.FindDocument(i.Tag.ToString());
                mgd.DockContent.Activate();
            }
        }

        private void windowToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            windowToolStripMenuItem.DropDownItems.Clear();
            foreach (DockContent doc in this.dockPanel.Contents)
            {
                if (doc is IMyGenDocument)
                {
                    IMyGenDocument mgd = doc as IMyGenDocument;
                    if (!doc.IsHidden)
                    {

                        ToolStripMenuItem i = new ToolStripMenuItem(doc.Text, null, docItemToolStripMenuItem_Click);
                        i.Tag = mgd.DocumentIndentity;
                        if (doc.IsActivated) i.Checked = true;
                        windowToolStripMenuItem.DropDownItems.Add(i);
                    }
                }
            }
                if (windowToolStripMenuItem.DropDownItems.Count == 0) {
                }

        }

        public void SendAlert(IMyGenContent sender, string command, params object[] args)
        {
            IMyGenContent contentItem = null;
            DockContentCollection contents = this.dockPanel.Contents;

            DefaultSettings settings = DefaultSettings.Instance;

            for (int i = 0; i < contents.Count; i++)
            {
                contentItem = contents[i] as IMyGenContent;

                if (contentItem != null)
                {
                    contentItem.ProcessAlert(sender, command, args);
                }
            }
        }

        public object PerformMdiFuntion(IMyGenContent sender, string function, params object[] args)
        {
            if (function.Equals("showoledbdialog", StringComparison.CurrentCultureIgnoreCase) &&
                args.Length == 1)
            {
                return BrowseOleDbConnectionString(args[0].ToString());
            }
            return null;
        }

        public IMyGenConsole Console
        {
            get { return this.ConsoleDockContent; }
        }

        public IMyGenErrorList ErrorList
        {
            get { return this.ErrorsDockContent; }
        }

        public void WriteConsole(string text, params object[] args)
        {
            ConsoleDockContent.Write(text, args);
        }

        public void ErrorsOccurred(params Exception[] exs)
        {
            ErrorsDockContent.AddErrors(exs);
            foreach (Exception ex in exs) ConsoleDockContent.Write(ex);
        }

        #endregion

        #region Error Handling

        public void UnhandledExceptions(object sender, UnhandledExceptionEventArgs args)
        {
            try
            {
                // Most likey the application is terminating on this method
                Exception ex = (Exception)args.ExceptionObject;
                HandleError(ex);
            }
            catch { }
        }

        public void OnThreadException(object sender, ThreadExceptionEventArgs t)
        {
            try
            {
                Exception ex = (Exception)t.Exception;
                HandleError(ex);
            }
            catch { }
        }

        private void HandleError(Exception ex)
        {
            try
            {
                if (this.ErrorsDockContent != null)
                {
                    this.ErrorsDockContent.AddErrors(ex);
                }
                if (this.ConsoleDockContent != null)
                {
                    this.ConsoleDockContent.Write(ex);
                }
            }
            catch { }
        }
        #endregion
    }
}